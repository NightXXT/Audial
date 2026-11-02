using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;

namespace YtMusic.Services.Auth;

[SupportedOSPlatform("windows")]
public sealed class DpapiTokenStore : ISecureTokenStore
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YtMusicDesktop.session.v1");

	private readonly string _path;

	public DpapiTokenStore(string? path = null)
	{
		_path = path ?? Path.Combine(AppPaths.DataDirectory, "session.bin");
	}

	public async Task SaveAsync(string payload, CancellationToken ct)
	{
		byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(payload), Entropy, DataProtectionScope.CurrentUser);
		string tmp = _path + ".tmp";
		await File.WriteAllBytesAsync(tmp, bytes, ct);
		File.Move(tmp, _path, overwrite: true);
	}

	public async Task<string?> LoadAsync(CancellationToken ct)
	{
		if (!File.Exists(_path))
		{
			return null;
		}
		try
		{
			byte[] bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path, ct), Entropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(bytes);
		}
		catch (CryptographicException)
		{
			return null;
		}
	}

	public Task ClearAsync(CancellationToken ct)
	{
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
		return Task.CompletedTask;
	}
}
