using System;

namespace YtMusic.Core.Abstractions;

public class ProviderException : Exception
{
	public ProviderException(string message, Exception? inner = null)
		: base(message, inner)
	{
	}
}
