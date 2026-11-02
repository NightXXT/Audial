using System.Threading;
using System.Threading.Tasks;

namespace YtMusic.Core.Abstractions;

public interface ISecureTokenStore
{
	Task SaveAsync(string payload, CancellationToken ct);

	Task<string?> LoadAsync(CancellationToken ct);

	Task ClearAsync(CancellationToken ct);
}
