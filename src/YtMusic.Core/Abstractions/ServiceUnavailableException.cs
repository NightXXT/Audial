using System;

namespace YtMusic.Core.Abstractions;

public sealed class ServiceUnavailableException : ProviderException
{
	public ServiceUnavailableException(string message, Exception? inner = null)
		: base(message, inner)
	{
	}
}
