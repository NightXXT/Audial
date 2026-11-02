using System;

namespace YtMusic.Core.Models;

[Flags]
public enum LibraryCapabilities
{
	None = 0,
	Playlists = 1,
	LikedTracks = 2,
	SearchTracks = 4,
	SearchPlaylists = 8,
	SearchArtists = 0x10,
	SearchAlbums = 0x20
}
