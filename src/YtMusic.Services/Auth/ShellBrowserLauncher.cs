using System;
using System.Diagnostics;
using YtMusic.Core.Abstractions;

namespace YtMusic.Services.Auth;

public sealed class ShellBrowserLauncher : IBrowserLauncher
{
	public void Open(Uri uri)
	{
		string scheme = uri.Scheme;
		if ((!(scheme == "https") && !(scheme == "http")) || 1 == 0)
		{
			throw new ArgumentException("Somente http/https.", "uri");
		}
		Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
		{
			UseShellExecute = true
		});
	}
}
