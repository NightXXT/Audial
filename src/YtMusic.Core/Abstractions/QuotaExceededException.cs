using System;

namespace YtMusic.Core.Abstractions;

public sealed class QuotaExceededException : ProviderException
{
	public QuotaExceededException(string message, Exception? inner = null)
		: base(message, inner)
	{
	}
}
