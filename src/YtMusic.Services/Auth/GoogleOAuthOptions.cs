using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YtMusic.Services.Auth;

public sealed record GoogleOAuthOptions(string ClientId, string? ClientSecret)
{
	public static string DefaultFilePath => Path.Combine(AppPaths.DataDirectory, "google-oauth.json");

	public const string ClientIdVariable = "YTMUSIC_GOOGLE_CLIENT_ID";

	public const string ClientSecretVariable = "YTMUSIC_GOOGLE_CLIENT_SECRET";

	public static GoogleOAuthOptions Parse(string json)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		JsonElement jsonElement = jsonDocument.RootElement;
		if (jsonElement.ValueKind != JsonValueKind.Object)
		{
			throw new FormatException("Selecione o JSON de um cliente OAuth do tipo App para computador.");
		}
		if (jsonElement.TryGetProperty("web", out var value) || jsonElement.TryGetProperty("type", out value))
		{
			throw new FormatException("Este arquivo não é de um cliente OAuth para computador. Crie um cliente do tipo App para computador no Google Cloud.");
		}
		if (jsonElement.TryGetProperty("installed", out var value2))
		{
			jsonElement = value2;
		}
		if (jsonElement.ValueKind != JsonValueKind.Object)
		{
			throw new FormatException("O arquivo OAuth não tem uma configuração válida para computador.");
		}
		JsonElement value3;
		string text = ((!jsonElement.TryGetProperty("client_id", out value3) || value3.ValueKind != JsonValueKind.String) ? null : value3.GetString()?.Trim());
		JsonElement value4;
		string text2 = ((!jsonElement.TryGetProperty("client_secret", out value4) || value4.ValueKind != JsonValueKind.String) ? null : value4.GetString()?.Trim());
		if (text == null || !Regex.IsMatch(text, "^[0-9]+-[A-Za-z0-9_-]+\\.apps\\.googleusercontent\\.com$"))
		{
			throw new FormatException("O arquivo não contém um ID de cliente Google válido. Importe o JSON baixado do Google Cloud.");
		}
		return new GoogleOAuthOptions(text, string.IsNullOrWhiteSpace(text2) ? null : text2);
	}

	public static async Task ImportAsync(string sourcePath, CancellationToken ct = default(CancellationToken))
	{
		if (new FileInfo(sourcePath).Length > 65536)
		{
			throw new FormatException("O arquivo é grande demais para uma configuração OAuth. Selecione o JSON baixado do Google Cloud.");
		}
		GoogleOAuthOptions googleOAuthOptions = Parse(await File.ReadAllTextAsync(sourcePath, ct));
		string contents = JsonSerializer.Serialize(new
		{
			client_id = googleOAuthOptions.ClientId,
			client_secret = googleOAuthOptions.ClientSecret
		}, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		string destination = DefaultFilePath;
		string temporary = destination + ".tmp";
		await File.WriteAllTextAsync(temporary, contents, ct);
		File.Move(temporary, destination, overwrite: true);
	}

	public static GoogleOAuthOptions? Load()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("YTMUSIC_GOOGLE_CLIENT_ID");
		string environmentVariable2 = Environment.GetEnvironmentVariable("YTMUSIC_GOOGLE_CLIENT_SECRET");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return new GoogleOAuthOptions(environmentVariable.Trim(), string.IsNullOrWhiteSpace(environmentVariable2) ? null : environmentVariable2.Trim());
		}
		try
		{
			if (!File.Exists(DefaultFilePath))
			{
				return null;
			}
			return Parse(File.ReadAllText(DefaultFilePath));
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is UnauthorizedAccessException || ex is FormatException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	[CompilerGenerated]
	private GoogleOAuthOptions(GoogleOAuthOptions original)
	{
		ClientId = original.ClientId;
		ClientSecret = original.ClientSecret;
	}
}
