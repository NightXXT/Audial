using System;
using System.Linq;
using DiscordRPC;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.App.Platform;

public sealed class DiscordPresenceService : IDisposable
{
	private readonly AppSettings _settings;

	private readonly PlaybackCoordinator _playback;

	private readonly IUiDispatcher _dispatcher;

	private DiscordRpcClient? _client;

	private string? _clientId;

	private bool _disposed;

	public DiscordPresenceService(AppSettings settings, PlaybackCoordinator playback, IUiDispatcher dispatcher)
	{
		_settings = settings;
		_playback = playback;
		_dispatcher = dispatcher;
		_playback.CurrentTrackChanged += OnPlaybackChanged;
		_playback.Player.StateChanged += OnPlaybackStateChanged;
	}

	public void ApplySettings()
	{
		if (_disposed)
		{
			return;
		}
		string text = _settings.DiscordApplicationId?.Trim();
		if (!_settings.DiscordPresenceEnabled || string.IsNullOrEmpty(text) || !text.All(char.IsAsciiDigit))
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

	private void RefreshPresence()
	{
		if (_disposed || _client == null)
		{
			return;
		}
		Track currentTrack = _playback.CurrentTrack;
		if (_playback.Player.State != PlayerState.Playing || (object)currentTrack == null || currentTrack.IsDemo)
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
		if (timeSpan > TimeSpan.Zero)
		{
			TimeSpan timeSpan2 = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0L, timeSpan.Ticks));
			DateTime dateTime = DateTime.UtcNow - timeSpan2;
			timestamps = new Timestamps(dateTime, dateTime + timeSpan);
		}
		try
		{
			_client.SetPresence(new RichPresence
			{
				Details = text,
				State = ((text2.Length == 0) ? "YouTube Music" : text2),
				Timestamps = timestamps,
				Assets = new Assets
				{
					LargeImageKey = "audial_logo",
					LargeImageText = "Audial"
				}
			}.WithType(ActivityType.Listening));
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
	}

	private void ClearPresence()
	{
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
		if (text.Length > limit)
		{
			return text.Substring(0, limit - 1) + "…";
		}
		return text;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_playback.CurrentTrackChanged -= OnPlaybackChanged;
			_playback.Player.StateChanged -= OnPlaybackStateChanged;
			ReleaseClient();
		}
	}
}
