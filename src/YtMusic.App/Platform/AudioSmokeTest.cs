using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Platform;

internal static class AudioSmokeTest
{
	public static async Task RunAsync()
	{
		PlayerViewModel vm = App.GetService<PlayerViewModel>();
		IPlayer player = vm.Playback.Player;
		try
		{
			Track[] tracks = (await App.GetService<ILibraryProvider>().SearchAsync("Alok", SearchKind.Tracks, CancellationToken.None)).Tracks.Take(2).ToArray();
			if (tracks.Length != 2)
			{
				throw new InvalidOperationException("Catalog returned fewer than two tracks");
			}
			await vm.Playback.PlayQueueAsync(tracks, 0);
			await UntilAsync(() => player.Position.TotalSeconds > 4.0 && player.State == PlayerState.Playing);
			App.Diagnose($"AUDIO TEST initial play OK position={player.Position.TotalSeconds:F1}");
			await vm.PlayPauseCommand.ExecuteAsync(null);
			await UntilAsync(() => player.State == PlayerState.Paused);
			await Task.Delay(800);
			TimeSpan paused = player.Position;
			await Task.Delay(1500);
			if (Math.Abs((player.Position - paused).TotalSeconds) > 0.7)
			{
				throw new InvalidOperationException("Pause did not stop progress");
			}
			App.Diagnose("AUDIO TEST pause command OK");
			await vm.Playback.SeekAsync(TimeSpan.FromSeconds(30.0));
			await vm.PlayPauseCommand.ExecuteAsync(null);
			await UntilAsync(() => player.State == PlayerState.Playing && player.Position.TotalSeconds > 32.0);
			App.Diagnose($"AUDIO TEST resume and seek OK position={player.Position.TotalSeconds:F1}");
			await vm.NextCommand.ExecuteAsync(null);
			await UntilAsync(() => player.State == PlayerState.Playing && player.Position.TotalSeconds > 3.0);
			if (vm.Playback.CurrentTrack?.Id != tracks[1].Id)
			{
				throw new InvalidOperationException("Next did not change track");
			}
			App.Diagnose($"AUDIO TEST next command OK position={player.Position.TotalSeconds:F1}");
			await vm.PlayPauseCommand.ExecuteAsync(null);
			App.Diagnose("AUDIO TEST PASS");
		}
		catch (Exception ex)
		{
			await player.PauseAsync();
			App.Diagnose("AUDIO TEST FAIL " + ex.GetType().Name);
		}
	}

	private static async Task UntilAsync(Func<bool> condition)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(40.0);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				throw new TimeoutException();
			}
			await Task.Delay(250);
		}
	}
}
