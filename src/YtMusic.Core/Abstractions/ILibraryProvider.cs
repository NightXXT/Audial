using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Models;

namespace YtMusic.Core.Abstractions;

public interface ILibraryProvider
{
	LibraryCapabilities Capabilities { get; }

	Task<Page<PlaylistSummary>> GetPlaylistsAsync(string? pageToken, CancellationToken ct);

	Task<Page<Track>> GetLikedTracksAsync(string? pageToken, CancellationToken ct);

	Task<Page<Track>> GetPlaylistTracksAsync(string playlistId, string? pageToken, CancellationToken ct);

	Task<SearchResults> SearchAsync(string query, SearchKind kind, CancellationToken ct);
}
