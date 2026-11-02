namespace YtMusic.Core.Abstractions;

public sealed class ProviderConfigurationException : ProviderException
{
	public ProviderConfigurationException(string message)
		: base(message)
	{
	}
}
