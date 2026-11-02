using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.Services.Settings;

public sealed class ListeningHistory : IDisposable
{
	private readonly PlaybackCoordinator _playback;

	private readonly string _path = Path.Combine(AppPaths.DataDirectory, "listening-history.json");

	private List<Track> _items = new List<Track>();

	public IReadOnlyList<Track> Items => _items;

	public ListeningHistory(PlaybackCoordinator playback)
	{
		_playback = playback;
		try
		{
			if (File.Exists(_path))
			{
				_items = (JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(_path)) ?? new List<Track>()).Take(30).ToList();
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
		_playback.Player.StateChanged += OnStateChanged;
	}

	private void OnStateChanged(object? sender, PlayerState state)
	{
		if (state != PlayerState.Playing)
		{
			return;
		}
		Track track = _playback.CurrentTrack;
		if ((object)track == null || track.IsDemo)
		{
			return;
		}
		_items = new Track[1] { track }.Concat(_items.Where((Track x) => x.Id != track.Id)).Take(30).ToList();
		try
		{
			Directory.CreateDirectory(AppPaths.DataDirectory);
			File.WriteAllText(_path, JsonSerializer.Serialize(_items));
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
	}

	public void Dispose()
	{
		_playback.Player.StateChanged -= OnStateChanged;
	}
}
