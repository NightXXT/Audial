using System;
using System.IO;
using System.Text.Json;
using YtMusic.Core.Abstractions;

namespace YtMusic.Services.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly string _path;

	public JsonSettingsStore(string? path = null)
	{
		_path = path ?? Path.Combine(AppPaths.DataDirectory, "settings.json");
	}

	public AppSettings Load()
	{
		try
		{
			if (!File.Exists(_path))
			{
				return new AppSettings();
			}
			return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			return new AppSettings();
		}
	}

	public void Save(AppSettings settings)
	{
		string text = _path + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(settings, Options));
		File.Move(text, _path, overwrite: true);
	}
}
