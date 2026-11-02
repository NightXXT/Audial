using System;

namespace YtMusic.Core.Abstractions;

public sealed class SessionExpiredException : ProviderException
{
	public SessionExpiredException(string message, Exception? inner = null)
		: base(message, inner)
	{
	}
}
