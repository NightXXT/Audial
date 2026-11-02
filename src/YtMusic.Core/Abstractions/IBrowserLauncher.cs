using System;

namespace YtMusic.Core.Abstractions;

public interface IBrowserLauncher
{
	void Open(Uri uri);
}
