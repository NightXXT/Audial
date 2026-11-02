using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Core.Playback;

public sealed class PlaybackCoordinator
{
	private readonly IPlayer _player;

	private readonly PlaybackQueue _queue;

	private CancellationTokenSource? _loadCts;

	private int _consecutiveFailures;

	public Func<Task>? RemoteNext { get; set; }

	public Func<Task>? RemotePrevious { get; set; }

	public IPlayer Player => _player;

	public PlaybackQueue Queue => _queue;

	public Track? CurrentTrack => _queue.Current;

	public event EventHandler? CurrentTrackChanged;

	public event EventHandler<string>? PlaybackError;

	public PlaybackCoordinator(IPlayer player, PlaybackQueue queue)
	{
		_player = player;
		_queue = queue;
		_player.TrackEnded += delegate
		{
			SafeAsync(NextAsync);
		};
		_player.Failed += delegate(object? _, PlaybackFailure f)
		{
			PlaybackFailure f2 = f;
			_ = SafeAsync(() => OnFailedAsync(f2));
		};
	}

	public async Task PlayQueueAsync(IReadOnlyList<Track> tracks, int startIndex)
	{
		_consecutiveFailures = 0;
		_queue.Replace(tracks, startIndex);
		await LoadCurrentAsync();
	}

	public async Task PlayIndexAsync(int index)
	{
		_consecutiveFailures = 0;
		if (_queue.MoveTo(index))
		{
			await LoadCurrentAsync();
		}
	}

	public void Enqueue(Track track)
	{
		_queue.Add(track);
	}

	public async Task ClearAsync()
	{
		_loadCts?.Cancel();
		await _player.PauseAsync();
		_queue.Clear();
		this.CurrentTrackChanged?.Invoke(this, EventArgs.Empty);
	}

	public async Task RemoveAsync(int index)
	{
		if (index < 0 || index >= _queue.Items.Count)
		{
			return;
		}
		bool removingCurrent = index == _queue.CurrentIndex;
		if (removingCurrent)
		{
			await _player.PauseAsync();
		}
		_queue.RemoveAt(index);
		if (removingCurrent)
		{
			if ((object)_queue.Current != null)
			{
				await LoadCurrentAsync();
			}
			else
			{
				this.CurrentTrackChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public async Task TogglePlayPauseAsync()
	{
		if ((object)_queue.Current == null)
		{
			if (_queue.Items.Count != 0)
			{
				_queue.MoveTo(0);
				await LoadCurrentAsync();
			}
			return;
		}
		switch (_player.State)
		{
		case PlayerState.Playing:
			await _player.PauseAsync();
			return;
		case PlayerState.Paused:
			await _player.ResumeAsync();
			return;
		}
		if ((object)_queue.Current == null && _queue.Items.Count > 0)
		{
			_queue.MoveTo(0);
		}
		if ((object)_queue.Current != null)
		{
			await LoadCurrentAsync();
		}
	}

	public async Task NextAsync()
	{
		if (RemoteNext != null)
		{
			await RemoteNext();
		}
		else if (_queue.MoveNext())
		{
			await LoadCurrentAsync();
		}
	}

	public async Task PreviousAsync()
	{
		if (RemotePrevious != null)
		{
			await RemotePrevious();
		}
		else if (_player.Position > TimeSpan.FromSeconds(3.0) || !_queue.HasPrevious)
		{
			await _player.SeekAsync(TimeSpan.Zero);
		}
		else if (_queue.MovePrevious())
		{
			await LoadCurrentAsync();
		}
	}

	public Task SeekAsync(TimeSpan position)
	{
		return _player.SeekAsync(position);
	}

	public Task SetVolumeAsync(double volume)
	{
		return _player.SetVolumeAsync(Math.Clamp(volume, 0.0, 1.0));
	}

	private async Task LoadCurrentAsync()
	{
		_loadCts?.Cancel();
		_loadCts = new CancellationTokenSource();
		CancellationToken token = _loadCts.Token;
		this.CurrentTrackChanged?.Invoke(this, EventArgs.Empty);
		Track current = _queue.Current;
		if ((object)current == null)
		{
			return;
		}
		try
		{
			await _player.LoadAndPlayAsync(current, token);
			_consecutiveFailures = 0;
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task OnFailedAsync(PlaybackFailure failure)
	{
		_consecutiveFailures++;
		this.PlaybackError?.Invoke(this, failure.Message);
		if (failure.IsNotEmbeddable && _queue.HasNext && _consecutiveFailures < _queue.Items.Count)
		{
			await NextAsync();
		}
	}

	private async Task SafeAsync(Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
			this.PlaybackError?.Invoke(this, "Falha ao controlar a reprodução.");
		}
	}
}
