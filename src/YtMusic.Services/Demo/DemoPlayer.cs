using System;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Demo;

public sealed class DemoPlayer : IPlayer, IDisposable
{
	private readonly Timer _timer;

	private readonly object _lock = new object();

	private TimeSpan _position;

	public string Name => "SIMULADO (DEMO) — não toca áudio";

	public PlayerState State { get; private set; }

	public TimeSpan Position
	{
		get
		{
			lock (_lock)
			{
				return _position;
			}
		}
	}

	public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(3.0);


	public event EventHandler<PlayerState>? StateChanged;

	public event EventHandler? PositionChanged;

	public event EventHandler? TrackEnded;

	public event EventHandler<PlaybackFailure>? Failed;

	public DemoPlayer()
	{
		_timer = new Timer(Tick, null, -1, -1);
	}

	public Task LoadAndPlayAsync(Track track, CancellationToken ct)
	{
		lock (_lock)
		{
			_position = TimeSpan.Zero;
		}
		Duration = track.Duration ?? TimeSpan.FromMinutes(3.0);
		SetState(PlayerState.Playing);
		_timer.Change(500, 500);
		return Task.CompletedTask;
	}

	public Task PauseAsync()
	{
		_timer.Change(-1, -1);
		SetState(PlayerState.Paused);
		return Task.CompletedTask;
	}

	public Task ResumeAsync()
	{
		_timer.Change(500, 500);
		SetState(PlayerState.Playing);
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position)
	{
		lock (_lock)
		{
			_position = position;
		}
		this.PositionChanged?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public Task SetVolumeAsync(double volume)
	{
		return Task.CompletedTask;
	}

	private void Tick(object? _)
	{
		bool flag;
		lock (_lock)
		{
			_position += TimeSpan.FromMilliseconds(500.0);
			flag = _position >= Duration;
		}
		this.PositionChanged?.Invoke(this, EventArgs.Empty);
		if (flag)
		{
			_timer.Change(-1, -1);
			SetState(PlayerState.Ended);
			this.TrackEnded?.Invoke(this, EventArgs.Empty);
		}
	}

	private void SetState(PlayerState s)
	{
		State = s;
		this.StateChanged?.Invoke(this, s);
	}

	public void Dispose()
	{
		_timer.Dispose();
	}
}
