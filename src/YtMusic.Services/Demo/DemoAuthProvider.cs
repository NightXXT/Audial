using System;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Demo;

public sealed class DemoAuthProvider : IAuthProvider
{
	public AuthState State { get; private set; }

	public event EventHandler? StateChanged;

	public Task<bool> TryRestoreSessionAsync(CancellationToken ct)
	{
		Set(AuthState.SignedIn);
		return Task.FromResult(result: true);
	}

	public Task SignInAsync(CancellationToken ct)
	{
		Set(AuthState.SignedIn);
		return Task.CompletedTask;
	}

	public Task SignOutAsync(CancellationToken ct)
	{
		Set(AuthState.SignedOut);
		return Task.CompletedTask;
	}

	public Task<string> GetAccessTokenAsync(CancellationToken ct)
	{
		return Task.FromResult("demo");
	}

	private void Set(AuthState s)
	{
		if (State != s)
		{
			State = s;
			this.StateChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
