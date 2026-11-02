using System;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Models;

namespace YtMusic.Core.Abstractions;

public interface IAuthProvider
{
	AuthState State { get; }

	event EventHandler? StateChanged;

	Task<bool> TryRestoreSessionAsync(CancellationToken ct);

	Task SignInAsync(CancellationToken ct);

	Task SignOutAsync(CancellationToken ct);

	Task<string> GetAccessTokenAsync(CancellationToken ct);
}
