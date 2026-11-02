namespace YtMusic.Core.Models;

public sealed record PlaylistSummary(string Id, string Title, int? ItemCount, string? ArtworkUrl, bool IsDemo = false);
