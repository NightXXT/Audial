namespace YtMusic.Core.Models;

public enum AuthState
{
	SignedOut,
	SigningIn,
	SignedIn,
	SessionExpired,
	PublicAccess
}
