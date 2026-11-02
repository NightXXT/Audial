using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace YtMusic.Core.Models;

public sealed record SearchResults(IReadOnlyList<Track> Tracks, IReadOnlyList<PlaylistSummary> Playlists, IReadOnlyList<ChannelSummary> Channels)
{
	public static SearchResults Empty { get; } = new SearchResults(Array.Empty<Track>(), Array.Empty<PlaylistSummary>(), Array.Empty<ChannelSummary>());


	[CompilerGenerated]
	private SearchResults(SearchResults original)
	{
		Tracks = original.Tracks;
		Playlists = original.Playlists;
		Channels = original.Channels;
	}
}
