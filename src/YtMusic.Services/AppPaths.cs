using System;
using System.IO;

namespace YtMusic.Services;

public static class AppPaths
{
	public static string DataDirectory
	{
		get
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtMusicDesktop");
			Directory.CreateDirectory(text);
			return text;
		}
	}
}
