using System;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Auth;

public sealed class PublicMusicAuthProvider : IAuthProvider
{
	public AuthState State => AuthState.PublicAccess;

	public event EventHandler? StateChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public Task<bool> TryRestoreSessionAsync(CancellationToken ct)
	{
		return Task.FromResult(result: true);
	}

	public Task SignInAsync(CancellationToken ct)
	{
		throw new ProviderConfigurationException("Use Conectar conta para abrir a autorização do YouTube Music.");
	}

	public Task SignOutAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}

	public Task<string> GetAccessTokenAsync(CancellationToken ct)
	{
		throw new ProviderConfigurationException("O catálogo público não usa credenciais.");
	}
}
