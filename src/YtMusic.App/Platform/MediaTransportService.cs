using System;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Media;
using Windows.Storage.Streams;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.App.Platform;

public sealed class MediaTransportService : IDisposable
{
	private readonly SystemMediaTransportControls _smtc;

	private readonly PlaybackCoordinator _playback;

	private readonly IUiDispatcher _ui;

	public MediaTransportService(Window window, PlaybackCoordinator playback, IUiDispatcher ui)
	{
		_playback = playback;
		_ui = ui;
		nint windowHandle = WindowNative.GetWindowHandle(window);
		_smtc = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
		_smtc.IsEnabled = true;
		_smtc.IsPlayEnabled = true;
		_smtc.IsPauseEnabled = true;
		_smtc.IsNextEnabled = true;
		_smtc.IsPreviousEnabled = true;
		_smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
		_smtc.ButtonPressed += OnButtonPressed;
		_playback.CurrentTrackChanged += OnTrackChanged;
		_playback.Player.StateChanged += OnStateChanged;
	}

	private void OnTrackChanged(object? sender, EventArgs e)
	{
		_ui.Post(UpdateDisplay);
	}

	private void OnStateChanged(object? sender, PlayerState state)
	{
		_ui.Post(delegate
		{
			UpdateStatus(state);
		});
	}

	public void Dispose()
	{
		_smtc.ButtonPressed -= OnButtonPressed;
		_playback.CurrentTrackChanged -= OnTrackChanged;
		_playback.Player.StateChanged -= OnStateChanged;
		_smtc.IsEnabled = false;
	}

	private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
	{
		SystemMediaTransportControlsButton button = args.Button;
		_ui.Post(async delegate
		{
			_ = 2;
			try
			{
				switch (button)
				{
				case SystemMediaTransportControlsButton.Play:
				case SystemMediaTransportControlsButton.Pause:
					await _playback.TogglePlayPauseAsync();
					break;
				case SystemMediaTransportControlsButton.Next:
					await _playback.NextAsync();
					break;
				case SystemMediaTransportControlsButton.Previous:
					await _playback.PreviousAsync();
					break;
				}
			}
			catch (Exception ex) when (((ex is InvalidOperationException || ex is OperationCanceledException) ? 1 : 0) != 0)
			{
			}
		});
	}

	private void UpdateDisplay()
	{
		Track currentTrack = _playback.CurrentTrack;
		SystemMediaTransportControlsDisplayUpdater displayUpdater = _smtc.DisplayUpdater;
		if ((object)currentTrack == null)
		{
			displayUpdater.ClearAll();
			displayUpdater.Update();
			return;
		}
		displayUpdater.Type = MediaPlaybackType.Music;
		displayUpdater.MusicProperties.Title = currentTrack.Title;
		displayUpdater.MusicProperties.Artist = currentTrack.Artist;
		displayUpdater.MusicProperties.AlbumTitle = currentTrack.Album ?? "";
		if (Uri.TryCreate(currentTrack.ArtworkUrl, UriKind.Absolute, out Uri result) && result.Scheme == Uri.UriSchemeHttps)
		{
			displayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(result);
		}
		displayUpdater.Update();
	}

	private void UpdateStatus(PlayerState state)
	{
		SystemMediaTransportControls smtc = _smtc;
		MediaPlaybackStatus playbackStatus;
		switch (state)
		{
		case PlayerState.Playing:
			playbackStatus = MediaPlaybackStatus.Playing;
			break;
		case PlayerState.Paused:
			playbackStatus = MediaPlaybackStatus.Paused;
			break;
		case PlayerState.Loading:
			playbackStatus = MediaPlaybackStatus.Changing;
			break;
		case PlayerState.Idle:
		case PlayerState.Ended:
		case PlayerState.Error:
			playbackStatus = MediaPlaybackStatus.Stopped;
			break;
		default:
			playbackStatus = MediaPlaybackStatus.Closed;
			break;
		}
		smtc.PlaybackStatus = playbackStatus;
	}
}
