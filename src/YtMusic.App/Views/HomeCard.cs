using YtMusic.Core.Models;

namespace YtMusic.App.Views;

public sealed class HomeCard
{
	public string Id { get; }

	public string Title { get; }

	public string Subtitle { get; }

	public string Detail { get; }

	public string? Artwork { get; }

	public Track? Track { get; }

	public HomeCard(string id, string title, string subtitle, string detail, string? artwork, Track? track = null)
	{
		Id = id;
		Title = title;
		Subtitle = subtitle;
		Detail = detail;
		Artwork = artwork;
		Track = track;
	}
}
