using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.Services.Together;

public enum RoomRole
{
	None,
	Host,
	Guest
}

/// <summary>
/// A listening room: the host owns the music and each guest plays the same track on their own
/// computer. Only small sync messages travel between peers, never audio.
/// All state changes run on the UI dispatcher.
/// </summary>
public sealed class ListenRoom : IDisposable
{
	public const int MaxGuests = 7;

	private const string HostId = "host";

	private const int NameLimit = 24;

	private const int TextLimit = 256;

	private const int MessageLimit = 16384;

	private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(4.0);

	private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(20.0);

	private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(30.0);

	private static readonly TimeSpan DriftTolerance = TimeSpan.FromSeconds(2.0);

	private static readonly TimeSpan SeekCooldown = TimeSpan.FromSeconds(4.0);

	private static readonly Regex TrackId = new Regex("^[A-Za-z0-9_-]{6,32}$", RegexOptions.Compiled);

	public static readonly IReadOnlyList<string> Reactions = new string[4] { "🔥", "💖", "🎧", "⚡" };

	private sealed class Peer
	{
		public required string Id { get; init; }

		public required RoomLink Link { get; init; }

		public string Name { get; set; } = "Convidado";

		public bool Joined { get; set; }

		public DateTime LastSeen { get; set; } = DateTime.UtcNow;
	}

	private readonly PlaybackCoordinator _playback;

	private readonly IUiDispatcher _ui;

	private readonly Dictionary<string, Peer> _peers = new Dictionary<string, Peer>(StringComparer.Ordinal);

	private readonly Dictionary<string, List<string>> _earlyCandidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly string _selfId = Guid.NewGuid().ToString("N");

	private RoomSignal? _signal;

	private Timer? _timer;

	private TaskCompletionSource<bool>? _joining;

	private string _name = "";

	private bool _answered;

	private Func<Task>? _savedNext;

	private Func<Task>? _savedPrevious;

	private TimeSpan _lastPosition;

	private long _lastPositionAt;

	private Track? _hostTrack;

	private TimeSpan _hostPosition;

	private bool _hostPlaying;

	private long _hostStateAt;

	private TimeSpan _roundTrip;

	private long _lastSeekAt = long.MinValue / 2;

	private bool _disposed;

	public RoomRole Role { get; private set; }

	public string? Code { get; private set; }

	public IReadOnlyList<string> Members { get; private set; } = Array.Empty<string>();

	public string? Notice { get; private set; }

	public event EventHandler? Changed;

	public event Action<string, string>? ReactionReceived;

	public ListenRoom(PlaybackCoordinator playback, IUiDispatcher ui)
	{
		_playback = playback;
		_ui = ui;
		_playback.CurrentTrackChanged += OnCurrentTrackChanged;
		_playback.Player.StateChanged += OnPlayerStateChanged;
		_playback.Player.PositionChanged += OnPositionChanged;
	}

	public void Create(string name)
	{
		Reset();
		_name = CleanName(name);
		Code = RoomSignal.NewCode();
		Role = RoomRole.Host;
		Members = new string[1] { _name };
		Notice = null;
		OpenSignal();
		StartTimer();
		RaiseChanged();
	}

	/// <summary>Connects to a room. On failure, returns false and leaves the reason in <see cref="Notice"/>.</summary>
	public async Task<bool> JoinAsync(string code, string name)
	{
		Reset();
		_name = CleanName(name);
		Code = code;
		Role = RoomRole.Guest;
		Members = new string[1] { _name };
		Notice = null;
		TaskCompletionSource<bool> joining = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_joining = joining;
		_savedNext = _playback.RemoteNext;
		_savedPrevious = _playback.RemotePrevious;
		_playback.RemoteNext = () => Task.CompletedTask;
		_playback.RemotePrevious = () => Task.CompletedTask;
		OpenSignal();
		RaiseChanged();
		try
		{
			Peer peer = AddPeer(HostId);
			await peer.Link.OfferAsync();
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
			Fail("Não foi possível iniciar a conexão.");
			return false;
		}
		bool opened = await Task.WhenAny(joining.Task, Task.Delay(JoinTimeout)) == joining.Task && joining.Task.Result;
		if (!opened)
		{
			if (Role == RoomRole.Guest)
			{
				Fail(_answered ? "A sala respondeu, mas sua rede bloqueou a conexão direta." : "Sala não encontrada. Confira o código e se quem criou ainda está nela.");
			}
			return false;
		}
		StartTimer();
		return true;
	}

	public void Leave()
	{
		if (Role != RoomRole.None)
		{
			Broadcast(new
			{
				t = "bye"
			});
			Reset();
			Notice = null;
			RaiseChanged();
		}
	}

	public void React(string emoji)
	{
		if (Role != RoomRole.None && Reactions.Contains(emoji))
		{
			Broadcast(new
			{
				t = "react",
				name = _name,
				emoji = emoji
			});
			this.ReactionReceived?.Invoke("Você", emoji);
		}
	}

	private void OpenSignal()
	{
		_signal = new RoomSignal(Code!);
		_signal.Received += delegate(RoomSignalMessage message)
		{
			_ui.Post(delegate
			{
				OnSignal(message);
			});
		};
		_signal.Start();
	}

	private void StartTimer()
	{
		_timer = new Timer(delegate
		{
			_ui.Post(Tick);
		}, null, HeartbeatInterval, HeartbeatInterval);
	}

	private Peer AddPeer(string id)
	{
		Peer peer = null;
		RoomLink link = new RoomLink((string kind, string data) => SendSignalAsync(id, kind, data));
		peer = new Peer
		{
			Id = id,
			Link = link
		};
		link.Opened += delegate
		{
			_ui.Post(delegate
			{
				OnPeerOpened(peer);
			});
		};
		link.Closed += delegate
		{
			_ui.Post(delegate
			{
				OnPeerClosed(peer);
			});
		};
		link.Received += delegate(string text)
		{
			_ui.Post(delegate
			{
				OnPeerMessage(peer, text);
			});
		};
		_peers[id] = peer;
		return peer;
	}

	private async Task SendSignalAsync(string to, string kind, string data)
	{
		RoomSignal? signal = _signal;
		if (signal == null)
		{
			return;
		}
		try
		{
			await signal.SendAsync(new RoomSignalMessage((Role == RoomRole.Host) ? HostId : _selfId, to, kind, data));
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
	}

	private void OnSignal(RoomSignalMessage message)
	{
		string me = (Role == RoomRole.Host) ? HostId : _selfId;
		if (Role == RoomRole.None || message.To != me)
		{
			return;
		}
		switch (message.Kind)
		{
		case "offer":
			if (Role == RoomRole.Host)
			{
				_ = AcceptGuestAsync(message.From, message.Data);
			}
			break;
		case "answer":
			if (Role == RoomRole.Guest && _peers.TryGetValue(HostId, out Peer host))
			{
				_answered = true;
				host.Link.AcceptAnswer(message.Data);
			}
			break;
		case "ice":
			if (_peers.TryGetValue(message.From, out Peer peer))
			{
				peer.Link.AddCandidate(message.Data);
			}
			else if (Role == RoomRole.Host)
			{
				if (!_earlyCandidates.TryGetValue(message.From, out List<string> list))
				{
					list = (_earlyCandidates[message.From] = new List<string>());
				}
				list.Add(message.Data);
			}
			break;
		case "full":
			if (Role == RoomRole.Guest)
			{
				Fail("A sala está cheia.");
			}
			break;
		}
	}

	private async Task AcceptGuestAsync(string id, string sdp)
	{
		if (_peers.ContainsKey(id))
		{
			return;
		}
		if (_peers.Count >= MaxGuests)
		{
			await SendSignalAsync(id, "full", "");
			return;
		}
		try
		{
			Peer peer = AddPeer(id);
			await peer.Link.AnswerAsync(sdp);
			if (_earlyCandidates.Remove(id, out List<string> list))
			{
				foreach (string item in list)
				{
					peer.Link.AddCandidate(item);
				}
			}
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
			RemovePeer(id);
		}
	}

	private void OnPeerOpened(Peer peer)
	{
		peer.LastSeen = DateTime.UtcNow;
		if (Role == RoomRole.Guest && _peers.ContainsKey(peer.Id))
		{
			peer.Link.Send(JsonSerializer.Serialize(new
			{
				t = "hello",
				name = _name
			}));
			_signal?.Dispose();
			_signal = null;
			_joining?.TrySetResult(true);
			RaiseChanged();
		}
	}

	private void OnPeerClosed(Peer peer)
	{
		if (!_peers.TryGetValue(peer.Id, out Peer current) || current != peer)
		{
			return;
		}
		if (Role == RoomRole.Guest)
		{
			Fail("A conexão com a sala caiu.");
		}
		else
		{
			RemovePeer(peer.Id);
		}
	}

	private void RemovePeer(string id)
	{
		if (_peers.Remove(id, out Peer peer))
		{
			peer.Link.Dispose();
			if (peer.Joined)
			{
				PublishMembers();
			}
		}
	}

	private void OnPeerMessage(Peer peer, string text)
	{
		if (Role == RoomRole.None || text.Length > MessageLimit || !_peers.ContainsKey(peer.Id))
		{
			return;
		}
		peer.LastSeen = DateTime.UtcNow;
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			JsonElement root = jsonDocument.RootElement;
			string kind = root.GetProperty("t").GetString() ?? "";
			if (Role == RoomRole.Host)
			{
				OnGuestMessage(peer, kind, root, text);
			}
			else
			{
				OnHostMessage(kind, root);
			}
		}
		catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException || ex is FormatException)
		{
		}
	}

	private void OnGuestMessage(Peer peer, string kind, JsonElement root, string text)
	{
		switch (kind)
		{
		case "hello":
			peer.Name = CleanName(root.GetProperty("name").GetString());
			peer.Joined = true;
			PublishMembers();
			SendState(peer);
			break;
		case "ping":
			peer.Link.Send(JsonSerializer.Serialize(new
			{
				t = "pong",
				at = root.GetProperty("at").GetInt64()
			}));
			break;
		case "bye":
			RemovePeer(peer.Id);
			break;
		case "react":
		{
			string emoji = root.GetProperty("emoji").GetString() ?? "";
			if (Reactions.Contains(emoji))
			{
				this.ReactionReceived?.Invoke(peer.Name, emoji);
				foreach (Peer item in _peers.Values)
				{
					if (item != peer)
					{
						item.Link.Send(JsonSerializer.Serialize(new
						{
							t = "react",
							name = peer.Name,
							emoji = emoji
						}));
					}
				}
			}
			break;
		}
		}
	}

	private void OnHostMessage(string kind, JsonElement root)
	{
		switch (kind)
		{
		case "state":
			_hostTrack = ReadTrack(root.GetProperty("track"));
			_hostPosition = TimeSpan.FromSeconds(root.GetProperty("pos").GetDouble());
			_hostPlaying = root.GetProperty("playing").GetBoolean();
			_hostStateAt = _clock.ElapsedMilliseconds;
			_ = FollowHostAsync();
			break;
		case "members":
			Members = root.GetProperty("list").EnumerateArray().Select((JsonElement e) => CleanName(e.GetString())).ToArray();
			RaiseChanged();
			break;
		case "pong":
			_roundTrip = TimeSpan.FromMilliseconds(Math.Max(0L, _clock.ElapsedMilliseconds - root.GetProperty("at").GetInt64()));
			break;
		case "react":
		{
			string emoji = root.GetProperty("emoji").GetString() ?? "";
			if (Reactions.Contains(emoji))
			{
				this.ReactionReceived?.Invoke(CleanName(root.GetProperty("name").GetString()), emoji);
			}
			break;
		}
		case "bye":
			Fail("Quem criou a sala encerrou a sessão.");
			break;
		}
	}

	private async Task FollowHostAsync()
	{
		if (Role != RoomRole.Guest)
		{
			return;
		}
		try
		{
			Track? track = _hostTrack;
			IPlayer player = _playback.Player;
			if ((object?)track == null)
			{
				if (player.State == PlayerState.Playing)
				{
					await player.PauseAsync();
				}
				return;
			}
			if (_playback.CurrentTrack?.Id != track.Id)
			{
				await _playback.PlayQueueAsync(new Track[1] { track }, 0);
				return;
			}
			PlayerState state = player.State;
			if (state != PlayerState.Playing && state != PlayerState.Paused)
			{
				return;
			}
			if (_hostPlaying && state == PlayerState.Paused)
			{
				await player.ResumeAsync();
			}
			else if (!_hostPlaying && state == PlayerState.Playing)
			{
				await player.PauseAsync();
			}
			TimeSpan expected = ExpectedPosition();
			long now = _clock.ElapsedMilliseconds;
			if ((expected - player.Position).Duration() > DriftTolerance && now - _lastSeekAt > (long)SeekCooldown.TotalMilliseconds)
			{
				_lastSeekAt = now;
				await player.SeekAsync(expected);
			}
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
	}

	private TimeSpan ExpectedPosition()
	{
		if (!_hostPlaying)
		{
			return _hostPosition;
		}
		return _hostPosition + TimeSpan.FromMilliseconds(_clock.ElapsedMilliseconds - _hostStateAt) + _roundTrip / 2.0;
	}

	private void OnCurrentTrackChanged(object? sender, EventArgs e)
	{
		_ui.Post(BroadcastState);
	}

	private void OnPlayerStateChanged(object? sender, PlayerState e)
	{
		_ui.Post(delegate
		{
			if (Role == RoomRole.Host)
			{
				BroadcastState();
			}
			else if (Role == RoomRole.Guest && _peers.Count > 0)
			{
				_ = FollowHostAsync();
			}
		});
	}

	private void OnPositionChanged(object? sender, EventArgs e)
	{
		if (Role != RoomRole.Host)
		{
			return;
		}
		TimeSpan position = _playback.Player.Position;
		long elapsed = _clock.ElapsedMilliseconds;
		bool jumped = (position - _lastPosition).Duration() > TimeSpan.FromMilliseconds(elapsed - _lastPositionAt) + DriftTolerance;
		_lastPosition = position;
		_lastPositionAt = elapsed;
		if (jumped)
		{
			_ui.Post(BroadcastState);
		}
	}

	private void Tick()
	{
		if (Role == RoomRole.None)
		{
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		foreach (Peer item in _peers.Values.Where((Peer p) => utcNow - p.LastSeen > PeerTimeout).ToArray())
		{
			if (Role == RoomRole.Guest)
			{
				Fail("A conexão com a sala caiu.");
				return;
			}
			RemovePeer(item.Id);
		}
		if (Role == RoomRole.Host)
		{
			BroadcastState();
			return;
		}
		Broadcast(new
		{
			t = "ping",
			at = _clock.ElapsedMilliseconds
		});
	}

	private void BroadcastState()
	{
		if (Role != RoomRole.Host)
		{
			return;
		}
		foreach (Peer value in _peers.Values)
		{
			if (value.Joined)
			{
				SendState(value);
			}
		}
	}

	private void SendState(Peer peer)
	{
		Track? track = _playback.CurrentTrack;
		if ((object?)track != null && track.IsDemo)
		{
			track = null;
		}
		PlayerState state = _playback.Player.State;
		peer.Link.Send(JsonSerializer.Serialize(new
		{
			t = "state",
			track = (((object?)track == null) ? null : new
			{
				id = track.Id,
				title = track.Title,
				artist = track.Artist,
				album = track.Album,
				duration = track.Duration?.TotalSeconds,
				artwork = track.ArtworkUrl
			}),
			pos = _playback.Player.Position.TotalSeconds,
			playing = (state == PlayerState.Playing || state == PlayerState.Loading)
		}));
	}

	private void PublishMembers()
	{
		List<string> list = new List<string> { _name };
		list.AddRange(from p in _peers.Values
			where p.Joined
			select p.Name);
		Members = list;
		Broadcast(new
		{
			t = "members",
			list = list
		});
		RaiseChanged();
	}

	private void Broadcast(object message)
	{
		string text = JsonSerializer.Serialize(message);
		foreach (Peer value in _peers.Values)
		{
			value.Link.Send(text);
		}
	}

	private static Track? ReadTrack(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string id = element.GetProperty("id").GetString() ?? "";
		if (!TrackId.IsMatch(id))
		{
			return null;
		}
		string? artwork = element.GetProperty("artwork").GetString();
		if (artwork != null && (artwork.Length > 2048 || !artwork.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
		{
			artwork = null;
		}
		JsonElement duration = element.GetProperty("duration");
		return new Track(id, Limit(element.GetProperty("title").GetString()), Limit(element.GetProperty("artist").GetString()), element.GetProperty("album").GetString() is string album ? Limit(album) : null, (duration.ValueKind == JsonValueKind.Number) ? new TimeSpan?(TimeSpan.FromSeconds(duration.GetDouble())) : null, artwork);
	}

	private static string Limit(string? text)
	{
		text ??= "";
		return (text.Length <= TextLimit) ? text : text.Substring(0, TextLimit);
	}

	private static string CleanName(string? name)
	{
		string text = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		if (text.Length == 0)
		{
			return "Convidado";
		}
		return (text.Length <= NameLimit) ? text : text.Substring(0, NameLimit);
	}

	private void Fail(string notice)
	{
		_joining?.TrySetResult(false);
		Reset();
		Notice = notice;
		RaiseChanged();
	}

	private void Reset()
	{
		if (Role == RoomRole.Guest)
		{
			_playback.RemoteNext = _savedNext;
			_playback.RemotePrevious = _savedPrevious;
		}
		_timer?.Dispose();
		_timer = null;
		_signal?.Dispose();
		_signal = null;
		foreach (Peer value in _peers.Values)
		{
			value.Link.Dispose();
		}
		_peers.Clear();
		_earlyCandidates.Clear();
		_joining = null;
		_answered = false;
		_hostTrack = null;
		_roundTrip = TimeSpan.Zero;
		Code = null;
		Role = RoomRole.None;
		Members = Array.Empty<string>();
	}

	private void RaiseChanged()
	{
		this.Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_playback.CurrentTrackChanged -= OnCurrentTrackChanged;
			_playback.Player.StateChanged -= OnPlayerStateChanged;
			_playback.Player.PositionChanged -= OnPositionChanged;
			Leave();
		}
	}
}
