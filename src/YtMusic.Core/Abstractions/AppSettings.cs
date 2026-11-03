namespace YtMusic.Core.Abstractions;

public sealed class AppSettings
{
	public bool MinimizeToTray { get; set; }

	public bool UseTransparency { get; set; } = true;


	public bool ReduceMotion { get; set; }

	public bool PreferDataSaver { get; set; }

	public double Volume { get; set; } = 0.8;


	public bool DiscordPresenceEnabled { get; set; }

	public string DiscordApplicationId { get; set; } = "";

	public bool LiveOverlayEnabled { get; set; }


	public bool DemoMode { get; set; }
}
