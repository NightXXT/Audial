using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.App.Platform;

public sealed class NativeAudioPlayer : IPlayer, IDisposable
{
	private readonly YoutubeClient _youtube = new YoutubeClient();

	private readonly IUiDispatcher _ui;

	private readonly DispatcherQueueTimer _timer;

	private readonly LibVLC _engine;

	private MediaPlayer? _media;

	private Media? _source;

	private long _generation;

	private double _volume = 0.8;

	private bool _disposed;

	public string Name => "Áudio nativo · LibVLC · integração experimental";

	public PlayerState State { get; private set; }

	public TimeSpan Position { get; private set; }

	public TimeSpan Duration { get; private set; }

	public event EventHandler<PlayerState>? StateChanged;

	public event EventHandler? PositionChanged;

	public event EventHandler? TrackEnded;

	public event EventHandler<PlaybackFailure>? Failed;

	public NativeAudioPlayer(IUiDispatcher ui)
	{
		_ui = ui;
		LibVLCSharp.Shared.Core.Initialize();
		_engine = new LibVLC("--no-video", "--quiet");
		_timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		_timer.Interval = TimeSpan.FromMilliseconds(200.0);
		_timer.Tick += delegate
		{
			if (_media != null)
			{
				Position = TimeSpan.FromMilliseconds(Math.Max(0L, _media.Time));
				if (_media.Length > 0)
				{
					Duration = TimeSpan.FromMilliseconds(_media.Length);
				}
				this.PositionChanged?.Invoke(this, EventArgs.Empty);
			}
		};
		_timer.Start();
	}

	public async Task LoadAndPlayAsync(Track track, CancellationToken ct)
	{
		long generation = ++_generation;
		ReleaseMedia();
		Position = TimeSpan.Zero;
		Duration = track.Duration ?? TimeSpan.Zero;
		SetState(PlayerState.Loading);
		this.PositionChanged?.Invoke(this, EventArgs.Empty);
		try
		{
			MediaPlayer media;
			using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
			{
				timeout.CancelAfter(TimeSpan.FromSeconds(50.0));
				AudioOnlyStreamInfo audioOnlyStreamInfo = (from x in (await _youtube.Videos.Streams.GetManifestAsync(track.Id, timeout.Token)).GetAudioOnlyStreams()
					where x.Container == Container.Mp4
					orderby x.Bitrate descending
					select x).FirstOrDefault() ?? throw new InvalidOperationException("Audio unavailable");
				ct.ThrowIfCancellationRequested();
				if (_disposed || generation != _generation)
				{
					return;
				}
				_source = new Media(_engine, audioOnlyStreamInfo.Url, FromType.FromLocation);
				_source.AddOption(":no-video");
				_source.AddOption(":network-caching=1000");
				media = new MediaPlayer(_engine);
				_media = media;
				media.Playing += delegate
				{
					OnUi(delegate
					{
						media.Volume = (int)Math.Round(_volume * 100.0);
						SetState(PlayerState.Playing);
					});
				};
				media.Paused += delegate
				{
					OnUi(delegate
					{
						SetState(PlayerState.Paused);
					});
				};
				media.Opening += delegate
				{
					OnUi(delegate
					{
						SetState(PlayerState.Loading);
					});
				};
				media.EncounteredError += delegate
				{
					OnUi(delegate
					{
						Fail("Não foi possível reproduzir o áudio desta faixa. Tente novamente ou escolha outra música.");
					});
				};
				media.EndReached += delegate
				{
					OnUi(delegate
					{
						if (Position < TimeSpan.FromSeconds(1.0) && Duration > TimeSpan.FromSeconds(5.0))
						{
							Fail("O fluxo de áudio terminou antes de começar. Tente novamente ou escolha outra música.");
						}
						else
						{
							SetState(PlayerState.Ended);
							this.TrackEnded?.Invoke(this, EventArgs.Empty);
						}
					});
				};
				if (!media.Play(_source))
				{
					Fail("Não foi possível iniciar o player de áudio.");
				}
			}
			void OnUi(Action action)
			{
				Action action2 = action;
				_ui.Post(delegate
				{
					if (!_disposed && _media == media)
					{
						action2();
					}
				});
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested || _disposed || generation != _generation)
		{
			throw;
		}
		catch (Exception ex2) when (!(ex2 is OutOfMemoryException))
		{
			if (generation == _generation && !_disposed)
			{
				int? value = ((ex2 is HttpRequestException { StatusCode: { } statusCode }) ? new int?((int)statusCode) : null);
				App.Diagnose($"Audio resolution failed for {track.Id}: {ex2.GetType().Name}{((!value.HasValue) ? "" : $" HTTP {value}")}");
				ReleaseMedia();
				Fail((value.GetValueOrDefault() == 400) ? "O YouTube recusou a solicitação desta faixa (HTTP 400). Tente outra versão da música ou tente novamente mais tarde." : "Não foi possível abrir o áudio desta faixa. Tente outra música ou tente novamente.");
			}
		}
	}

	public Task PauseAsync()
	{
		_media?.SetPause(pause: true);
		return Task.CompletedTask;
	}

	public Task ResumeAsync()
	{
		_media?.SetPause(pause: false);
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position)
	{
		if (_media != null && _media.IsSeekable)
		{
			_media.Time = (long)Math.Clamp(position.TotalMilliseconds, 0.0, Math.Max(0.0, Duration.TotalMilliseconds));
			Position = position;
			this.PositionChanged?.Invoke(this, EventArgs.Empty);
		}
		return Task.CompletedTask;
	}

	public Task SetVolumeAsync(double volume)
	{
		_volume = Math.Clamp(volume, 0.0, 1.0);
		if (_media != null)
		{
			_media.Volume = (int)Math.Round(_volume * 100.0);
		}
		return Task.CompletedTask;
	}

	private void SetState(PlayerState state)
	{
		if (State != state)
		{
			State = state;
			App.Diagnose($"Native audio state: {state}");
			this.StateChanged?.Invoke(this, state);
		}
	}

	private void Fail(string message)
	{
		SetState(PlayerState.Error);
		this.Failed?.Invoke(this, new PlaybackFailure(message, IsNotEmbeddable: false));
	}

	private void ReleaseMedia()
	{
		MediaPlayer? media = _media;
		_media = null;
		media?.Stop();
		media?.Dispose();
		_source?.Dispose();
		_source = null;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_generation++;
			_timer.Stop();
			ReleaseMedia();
			_engine.Dispose();
		}
	}
}
