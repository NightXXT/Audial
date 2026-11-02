namespace YtMusic.Presentation.ViewModels;

public sealed record TrackListRequest(TrackListKind Kind, string? PlaylistId, string Title);
