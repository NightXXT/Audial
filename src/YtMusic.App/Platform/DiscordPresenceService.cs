using System;
using System.Linq;
using System.Text;
using DiscordRPC;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.App.Platform;

public sealed class DiscordPresenceService : IDisposable
{
	private const string DefaultApplicationId = "1561465254960169070";

	private const string LogoUrl = "https://raw.githubusercontent.com/NightXXT/Audial/main/docs/logo.png";

	private const string DownloadUrl = "https://github.com/NightXXT/Audial/releases/latest";

	private const int ImageKeyLimit = 256;

	private const int ButtonUrlLimit = 128;

	private static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(3.0);

	private readonly AppSettings _settings;

	private readonly PlaybackCoordinator _playback;

	private readonly IUiDispatcher _dispatcher;

	private DiscordRpcClient? _client;

	private string? _clientId;

	private bool _shown;

	private DateTime? _startedAt;

	private bool _disposed;

	public DiscordPresenceService(AppSettings settings, PlaybackCoordinator playback, IUiDispatcher dispatcher)
	{
		_settings = settings;
		_playback = playback;
		_dispatcher = dispatcher;
		_playback.CurrentTrackChanged += OnPlaybackChanged;
		_playback.Player.StateChanged += OnPlaybackStateChanged;
		_playback.Player.PositionChanged += OnPositionChanged;
	}

	public void ApplySettings()
	{
		if (_disposed)
		{
			return;
		}
		string text = _settings.DiscordApplicationId?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			text = DefaultApplicationId;
		}
		if (!_settings.DiscordPresenceEnabled || !text.All(char.IsAsciiDigit))
		{
			ReleaseClient();
			return;
		}
		if (_client == null || !string.Equals(_clientId, text, StringComparison.Ordinal))
		{
			ReleaseClient();
			try
			{
				_client = new DiscordRpcClient(text);
				_client.Initialize();
				_clientId = text;
			}
			catch (Exception ex) when (!(ex is OutOfMemoryException))
			{
				ReleaseClient();
				return;
			}
		}
		RefreshPresence();
	}

	private void OnPlaybackChanged(object? sender, EventArgs e)
	{
		_dispatcher.Post(RefreshPresence);
	}

	private void OnPlaybackStateChanged(object? sender, PlayerState e)
	{
		_dispatcher.Post(RefreshPresence);
	}

	private void OnPositionChanged(object? sender, EventArgs e)
	{
		if (!_shown)
		{
			return;
		}
		bool outOfSync = (_startedAt is DateTime startedAt)
			? (DateTime.UtcNow - startedAt - _playback.Player.Position).Duration() > SeekTolerance
			: _playback.Player.Duration > TimeSpan.Zero;
		if (outOfSync)
		{
			_dispatcher.Post(RefreshPresence);
		}
	}

	private void RefreshPresence()
	{
		if (_disposed || _client == null)
		{
			return;
		}
		PlayerState state = _playback.Player.State;
		if (state == PlayerState.Loading)
		{
			return;
		}
		Track currentTrack = _playback.CurrentTrack;
		if (state != PlayerState.Playing || (object)currentTrack == null || currentTrack.IsDemo)
		{
			ClearPresence();
			return;
		}
		string text = Clean(currentTrack.Title, 128);
		string text2 = Clean(currentTrack.Artist, 128);
		if (text.Length == 0)
		{
			ClearPresence();
			return;
		}
		TimeSpan position = _playback.Player.Position;
		TimeSpan timeSpan = currentTrack.Duration ?? _playback.Player.Duration;
		Timestamps timestamps = null;
		DateTime? startedAt = null;
		if (timeSpan > TimeSpan.Zero)
		{
			TimeSpan timeSpan2 = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0L, timeSpan.Ticks));
			DateTime dateTime = DateTime.UtcNow - timeSpan2;
			timestamps = new Timestamps(dateTime, dateTime + timeSpan);
			startedAt = dateTime;
		}
		string album = Clean(currentTrack.Album, 128);
		try
		{
			_client.SetPresence(new RichPresence
			{
				Details = text,
				State = ((text2.Length == 0) ? "YouTube Music" : text2),
				Timestamps = timestamps,
				Assets = new Assets
				{
					LargeImageKey = Artwork(currentTrack),
					LargeImageText = ((album.Length < 2) ? "Audial" : album),
					SmallImageKey = LogoUrl,
					SmallImageText = "Audial"
				},
				Buttons = Buttons(currentTrack)
			}.WithType(ActivityType.Listening).WithStatusDisplay(StatusDisplayType.Details));
			_shown = true;
			_startedAt = startedAt;
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
	}

	private static string Artwork(Track track)
	{
		string? url = track.ArtworkUrl;
		if (url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Encoding.UTF8.GetByteCount(url) > ImageKeyLimit)
		{
			return LogoUrl;
		}
		return url;
	}

	private static DiscordRPC.Button[] Buttons(Track track)
	{
		DiscordRPC.Button download = new DiscordRPC.Button
		{
			Label = "Baixar o Audial",
			Url = DownloadUrl
		};
		string listen = "https://music.youtube.com/watch?v=" + Uri.EscapeDataString(track.Id);
		if (listen.Length > ButtonUrlLimit)
		{
			return new DiscordRPC.Button[1] { download };
		}
		return new DiscordRPC.Button[2]
		{
			new DiscordRPC.Button
			{
				Label = "Ouvir no YouTube Music",
				Url = listen
			},
			download
		};
	}

	private void ClearPresence()
	{
		_shown = false;
		_startedAt = null;
		try
		{
			_client?.ClearPresence();
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
	}

	private void ReleaseClient()
	{
		DiscordRpcClient client = _client;
		_client = null;
		_clientId = null;
		_shown = false;
		_startedAt = null;
		if (client == null)
		{
			return;
		}
		try
		{
			client.ClearPresence();
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
		try
		{
			client.Dispose();
		}
		catch (Exception ex2) when (!(ex2 is OutOfMemoryException))
		{
		}
	}

	private static string Clean(string? value, int limit)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		string text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		if (Encoding.UTF8.GetByteCount(text) <= limit)
		{
			return text;
		}
		int length = Math.Min(text.Length, limit - 3);
		while (Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > limit - 3)
		{
			length--;
		}
		if (char.IsHighSurrogate(text[length - 1]))
		{
			length--;
		}
		return text.Substring(0, length) + "…";
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_playback.CurrentTrackChanged -= OnPlaybackChanged;
			_playback.Player.StateChanged -= OnPlaybackStateChanged;
			_playback.Player.PositionChanged -= OnPositionChanged;
			ReleaseClient();
		}
	}
}
