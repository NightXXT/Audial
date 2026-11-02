using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Models;

namespace YtMusic.Core.Abstractions;

public interface IMusicLibraryProvider : ILibraryProvider
{
	Task<Page<PlaylistSummary>> GetAlbumsAsync(string? pageToken, CancellationToken ct);

	Task<Page<PlaylistSummary>> GetHomeAsync(string? pageToken, CancellationToken ct);
}
