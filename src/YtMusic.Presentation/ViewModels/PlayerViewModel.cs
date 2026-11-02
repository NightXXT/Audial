using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.Presentation.ViewModels;

public sealed class PlayerViewModel : ObservableObject
{
	private readonly PlaybackCoordinator _playback;

	private readonly IUiDispatcher _ui;

	private readonly AppSettings _settings;

	private bool _updatingFromPlayer;

	private bool _isSeeking;

	private double _positionSeconds;

	private double _durationSeconds = 1.0;

	private double _volume;

	private PlayerState _state;

	public IAsyncRelayCommand PlayPauseCommand { get; }

	public IAsyncRelayCommand NextCommand { get; }

	public IAsyncRelayCommand PreviousCommand { get; }

	public PlaybackCoordinator Playback => _playback;

	public Track? CurrentTrack => _playback.CurrentTrack;

	public bool HasTrack => (object)CurrentTrack != null;

	public string Title => CurrentTrack?.Title ?? "Nada tocando";

	public string Artist => CurrentTrack?.Artist ?? "";

	public string Album => CurrentTrack?.Album ?? "";

	public string? ArtworkUrl => CurrentTrack?.ArtworkUrl;

	public bool IsDemoTrack => CurrentTrack?.IsDemo ?? false;

	public PlayerState State => _state;

	public bool IsPlaying => _state == PlayerState.Playing;

	public bool IsLoading => _state == PlayerState.Loading;

	public string PlayerName => _playback.Player.Name;

	public double DurationSeconds
	{
		get
		{
			return _durationSeconds;
		}
		private set
		{
			SetProperty(ref _durationSeconds, value, "DurationSeconds");
		}
	}

	public double PositionSeconds
	{
		get
		{
			return _positionSeconds;
		}
		set
		{
			if (SetProperty(ref _positionSeconds, value, "PositionSeconds"))
			{
				OnPropertyChanged("PositionText");
				OnPropertyChanged("ElapsedText");
				OnPropertyChanged("DurationText");
				if (!_updatingFromPlayer)
				{
					_playback.SeekAsync(TimeSpan.FromSeconds(value));
				}
			}
		}
	}

	public double Volume
	{
		get
		{
			return _volume * 100.0;
		}
		set
		{
			double num = Math.Clamp(value / 100.0, 0.0, 1.0);
			if (!(Math.Abs(num - _volume) < 0.001))
			{
				_volume = num;
				_settings.Volume = num;
				OnPropertyChanged("Volume");
				_playback.SetVolumeAsync(num);
			}
		}
	}

	public string ElapsedText => HasTrack ? Format(TimeSpan.FromSeconds(_positionSeconds)) : "0:00";

	public string DurationText => HasTrack ? Format(TimeSpan.FromSeconds(_durationSeconds)) : "0:00";

	public string PositionText
	{
		get
		{
			if (!HasTrack)
			{
				return "0:00 / 0:00";
			}
			return Format(TimeSpan.FromSeconds(_positionSeconds)) + " / " + Format(TimeSpan.FromSeconds(_durationSeconds));
		}
	}

	public event EventHandler<string>? ErrorRaised;

	public PlayerViewModel(PlaybackCoordinator playback, IUiDispatcher ui, AppSettings settings)
	{
		_playback = playback;
		_ui = ui;
		_settings = settings;
		_volume = settings.Volume;
		PlayPauseCommand = new AsyncRelayCommand(_playback.TogglePlayPauseAsync);
		NextCommand = new AsyncRelayCommand(_playback.NextAsync);
		PreviousCommand = new AsyncRelayCommand(_playback.PreviousAsync);
		_playback.CurrentTrackChanged += delegate
		{
			_ui.Post(RefreshTrack);
		};
		_playback.PlaybackError += delegate(object? _, string msg)
		{
			string msg2 = msg;
			_ui.Post(delegate
			{
				this.ErrorRaised?.Invoke(this, msg2);
			});
		};
		_playback.Player.StateChanged += delegate(object? _, PlayerState s)
		{
			_ui.Post(delegate
			{
				_state = s;
				OnPropertyChanged("IsPlaying");
				OnPropertyChanged("IsLoading");
				OnPropertyChanged("State");
				RefreshPosition();
			});
		};
		_playback.Player.PositionChanged += delegate
		{
			_ui.Post(RefreshPosition);
		};
		_playback.SetVolumeAsync(_volume);
	}

	public void BeginSeeking()
	{
		_isSeeking = true;
	}

	public async Task SeekToAsync(double seconds)
	{
		double value = Math.Clamp(double.IsFinite(seconds) ? seconds : 0.0, 0.0, Math.Max(0.0, DurationSeconds));
		try
		{
			await _playback.SeekAsync(TimeSpan.FromSeconds(value));
		}
		finally
		{
			_isSeeking = false;
			RefreshPosition();
		}
	}

	public static string Format(TimeSpan t)
	{
		if (!(t.TotalHours >= 1.0))
		{
			return t.ToString("m\\:ss");
		}
		return t.ToString("h\\:mm\\:ss");
	}

	private void RefreshTrack()
	{
		_isSeeking = false;
		OnPropertyChanged("CurrentTrack");
		OnPropertyChanged("HasTrack");
		OnPropertyChanged("Title");
		OnPropertyChanged("Artist");
		OnPropertyChanged("Album");
		OnPropertyChanged("ArtworkUrl");
		OnPropertyChanged("IsDemoTrack");
		RefreshPosition();
	}

	private void RefreshPosition()
	{
		if (_isSeeking)
		{
			return;
		}
		_updatingFromPlayer = true;
		try
		{
			double totalSeconds = _playback.Player.Duration.TotalSeconds;
			DurationSeconds = ((totalSeconds > 0.0) ? totalSeconds : (CurrentTrack?.Duration?.TotalSeconds).GetValueOrDefault(1.0));
			PositionSeconds = Math.Min(_playback.Player.Position.TotalSeconds, DurationSeconds);
			OnPropertyChanged("PositionText");
				OnPropertyChanged("ElapsedText");
				OnPropertyChanged("DurationText");
		}
		finally
		{
			_updatingFromPlayer = false;
		}
	}
}
