using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.App.Platform;

/// <summary>
/// Feeds the public overlay page. Live apps only accept HTTPS links and browsers block public pages
/// from reading this computer, so the song goes through a private ntfy.sh topic that the page follows.
/// Messages are only sent when something changes, which keeps the relay's daily limit out of reach.
/// </summary>
public sealed class LiveOverlayService : IDisposable
{
	private const string PageUrl = "https://nightxxt.github.io/Audial/overlay.html";

	private const string RelayUrl = "https://ntfy.sh/audial-live-";

	private const int TextLimit = 200;

	private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.0);

	private static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(3.0);

	private readonly AppSettings _settings;

	private readonly ISettingsStore _store;

	private readonly PlaybackCoordinator _playback;

	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(10.0)
	};

	private readonly Timer _timer;

	private string? _lastKey;

	private DateTime? _startedAt;

	private bool _enabled;

	public string Url => PageUrl + "#" + OverlayId();

	public LiveOverlayService(AppSettings settings, ISettingsStore store, PlaybackCoordinator playback)
	{
		_settings = settings;
		_store = store;
		_playback = playback;
		_timer = new Timer(delegate
		{
			_ = PublishAsync();
		});
		_playback.CurrentTrackChanged += OnChanged;
		_playback.Player.StateChanged += OnStateChanged;
		_playback.Player.PositionChanged += OnPositionChanged;
	}

	public void ApplySettings()
	{
		bool enabled = _settings.LiveOverlayEnabled;
		if (enabled == _enabled)
		{
			return;
		}
		_enabled = enabled;
		_lastKey = null;
		if (enabled)
		{
			Schedule();
		}
		else
		{
			_ = SendAsync(Stopped());
		}
	}

	private string OverlayId()
	{
		if (string.IsNullOrEmpty(_settings.LiveOverlayId))
		{
			_settings.LiveOverlayId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
			try
			{
				_store.Save(_settings);
			}
			catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
			{
			}
		}
		return _settings.LiveOverlayId;
	}

	private void OnChanged(object? sender, EventArgs e)
	{
		Schedule();
	}

	private void OnStateChanged(object? sender, PlayerState e)
	{
		Schedule();
	}

	private void OnPositionChanged(object? sender, EventArgs e)
	{
		if (_enabled && _startedAt is DateTime startedAt && (DateTime.UtcNow - startedAt - _playback.Player.Position).Duration() > SeekTolerance)
		{
			_lastKey = null;
			Schedule();
		}
	}

	private void Schedule()
	{
		if (_enabled)
		{
			_timer.Change(Debounce, Timeout.InfiniteTimeSpan);
		}
	}

	private async Task PublishAsync()
	{
		IPlayer player = _playback.Player;
		PlayerState state = player.State;
		if (!_enabled || state == PlayerState.Loading)
		{
			return;
		}
		Track? track = _playback.CurrentTrack;
		bool playing = (object?)track != null && state == PlayerState.Playing;
		string key = playing ? ("playing:" + track!.Id) : "stopped";
		if (key == _lastKey)
		{
			return;
		}
		_lastKey = key;
		if (!playing)
		{
			_startedAt = null;
			await SendAsync(Stopped());
			return;
		}
		TimeSpan position = player.Position;
		_startedAt = DateTime.UtcNow - position;
		string? artwork = track!.ArtworkUrl;
		await SendAsync(JsonSerializer.Serialize(new
		{
			playing = true,
			title = Limit(track.Title),
			artist = Limit(track.Artist),
			artwork = (artwork != null && artwork.Length < 1000 && artwork.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ? artwork : null,
			position = position.TotalSeconds,
			duration = (track.Duration ?? player.Duration).TotalSeconds,
			sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	private async Task SendAsync(string body)
	{
		try
		{
			using StringContent content = new StringContent(body, Encoding.UTF8);
			using (await _http.PostAsync(RelayUrl + OverlayId(), content))
			{
			}
		}
		catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is ObjectDisposedException)
		{
		}
	}

	private static string Stopped()
	{
		return "{\"playing\":false}";
	}

	private static string Limit(string text)
	{
		return (text.Length <= TextLimit) ? text : text.Substring(0, TextLimit);
	}

	public void Dispose()
	{
		_playback.CurrentTrackChanged -= OnChanged;
		_playback.Player.StateChanged -= OnStateChanged;
		_playback.Player.PositionChanged -= OnPositionChanged;
		_timer.Dispose();
		if (_enabled)
		{
			_enabled = false;
			SendAsync(Stopped()).Wait(TimeSpan.FromSeconds(2.0));
		}
	}
}
