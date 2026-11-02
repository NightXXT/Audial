using System;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Models;

namespace YtMusic.Core.Abstractions;

public interface IPlayer
{
	string Name { get; }

	PlayerState State { get; }

	TimeSpan Position { get; }

	TimeSpan Duration { get; }

	event EventHandler<PlayerState>? StateChanged;

	event EventHandler? PositionChanged;

	event EventHandler? TrackEnded;

	event EventHandler<PlaybackFailure>? Failed;

	Task LoadAndPlayAsync(Track track, CancellationToken ct);

	Task PauseAsync();

	Task ResumeAsync();

	Task SeekAsync(TimeSpan position);

	Task SetVolumeAsync(double volume);
}
