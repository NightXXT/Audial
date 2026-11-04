using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YtMusic.App.Platform;

public sealed record AppUpdate(Version Version, string Url, string Sha256);

/// <summary>
/// Looks for a newer installer in the GitHub releases of the project. The download only runs when
/// its URL belongs to this repository and its hash matches the digest published by GitHub.
/// </summary>
public sealed class UpdateService
{
	private const string LatestRelease = "https://api.github.com/repos/NightXXT/Audial/releases/latest";

	private const string DownloadPrefix = "https://github.com/NightXXT/Audial/releases/download/";

	private const string InstallerName = "AudialSetup.exe";

	private static readonly HttpClient Http = CreateClient();

	public Version CurrentVersion { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

	public bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(10.0)
		};
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Audial");
		return httpClient;
	}

	private static Version Normalize(Version version)
	{
		return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
	}

	public async Task<AppUpdate?> CheckAsync(CancellationToken ct)
	{
		using HttpResponseMessage response = await Http.GetAsync(LatestRelease, ct);
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null; // no published release (closed beta): nothing to update to
		}
		response.EnsureSuccessStatusCode();
		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
		JsonElement root = document.RootElement;
		string tag = root.GetProperty("tag_name").GetString() ?? "";
		if (!Version.TryParse(tag.TrimStart('v'), out Version? version) || Normalize(version) <= CurrentVersion)
		{
			return null;
		}
		foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
		{
			string url = asset.GetProperty("browser_download_url").GetString() ?? "";
			string digest = asset.TryGetProperty("digest", out JsonElement value) ? (value.GetString() ?? "") : "";
			if (asset.GetProperty("name").GetString() == InstallerName && url.StartsWith(DownloadPrefix, StringComparison.Ordinal) && digest.StartsWith("sha256:", StringComparison.Ordinal))
			{
				return new AppUpdate(Normalize(version), url, digest.Substring("sha256:".Length));
			}
		}
		return null;
	}

	public async Task<string> DownloadAsync(AppUpdate update, CancellationToken ct)
	{
		string folder = Path.Combine(Path.GetTempPath(), "Audial-update");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, InstallerName);
		using (HttpResponseMessage response = await Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, ct))
		{
			response.EnsureSuccessStatusCode();
			await using FileStream file = File.Create(path);
			await response.Content.CopyToAsync(file, ct);
		}
		await using (FileStream file = File.OpenRead(path))
		{
			byte[] hash = await SHA256.HashDataAsync(file, ct);
			if (string.Equals(Convert.ToHexString(hash), update.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return path;
			}
		}
		File.Delete(path);
		throw new InvalidDataException("O instalador baixado não confere com o publicado.");
	}

	public void StartInstaller(string path)
	{
		Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /RESTARTAPP")
		{
			UseShellExecute = true
		});
	}
}
