using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Auth;

public sealed class GoogleOAuthProvider : IAuthProvider
{
	private sealed record StoredSession(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

	private sealed class TokenResponse
	{
		[JsonPropertyName("access_token")]
		public string? AccessToken { get; set; }

		[JsonPropertyName("refresh_token")]
		public string? RefreshToken { get; set; }

		[JsonPropertyName("expires_in")]
		public int ExpiresIn { get; set; }

		[JsonPropertyName("error")]
		public string? Error { get; set; }
	}

	private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

	private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

	private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

	public const string ReadOnlyScope = "https://www.googleapis.com/auth/youtube.readonly";

	public const string MusicScope = "https://www.googleapis.com/auth/youtube";

	private readonly string _scope;

	private readonly HttpClient _http;

	private readonly ISecureTokenStore _store;

	private readonly IBrowserLauncher _browser;

	private readonly Func<GoogleOAuthOptions?> _optionsFactory;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private StoredSession? _session;

	public AuthState State { get; private set; }

	public event EventHandler? StateChanged;

	public GoogleOAuthProvider(HttpClient http, ISecureTokenStore store, IBrowserLauncher browser, Func<GoogleOAuthOptions?>? optionsFactory = null, string scope = "https://www.googleapis.com/auth/youtube.readonly")
	{
		_http = http;
		_store = store;
		_browser = browser;
		_optionsFactory = optionsFactory ?? new Func<GoogleOAuthOptions>(GoogleOAuthOptions.Load);
		_scope = scope;
	}

	public async Task<bool> TryRestoreSessionAsync(CancellationToken ct)
	{
		string text = await _store.LoadAsync(ct);
		if (text == null)
		{
			SetState(AuthState.SignedOut);
			return false;
		}
		try
		{
			_session = JsonSerializer.Deserialize<StoredSession>(text);
		}
		catch (JsonException)
		{
			_session = null;
		}
		if ((object)_session == null || string.IsNullOrEmpty(_session.RefreshToken))
		{
			await _store.ClearAsync(ct);
			_session = null;
			SetState(AuthState.SignedOut);
			return false;
		}
		SetState(AuthState.SignedIn);
		return true;
	}

	public async Task SignInAsync(CancellationToken ct)
	{
		GoogleOAuthOptions options = _optionsFactory() ?? throw new ProviderConfigurationException("Client OAuth do Google não configurado. Veja a seção \"Credenciais\" do README.md.");
		AuthState previous = State;
		SetState(AuthState.SigningIn);
		try
		{
			string verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
			string value = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
			string state = Base64Url(RandomNumberGenerator.GetBytes(24));
			string redirectUri = $"http://127.0.0.1:{GetFreePort()}/";
			string uriString = "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new Dictionary<string, string>
			{
				["client_id"] = options.ClientId,
				["redirect_uri"] = redirectUri,
				["response_type"] = "code",
				["scope"] = _scope,
				["code_challenge"] = value,
				["code_challenge_method"] = "S256",
				["state"] = state,
				["access_type"] = "offline",
				["prompt"] = "select_account consent"
			});
			using HttpListener listener = new HttpListener();
			listener.Prefixes.Add(redirectUri);
			listener.Start();
			_browser.Open(new Uri(uriString));
			Task<HttpListenerContext> contextTask = listener.GetContextAsync();
			Task obj = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5.0), ct));
			ct.ThrowIfCancellationRequested();
			if (obj != contextTask)
			{
				throw new ProviderException("Tempo esgotado esperando o login no navegador.");
			}
			HttpListenerContext obj2 = await contextTask;
			NameValueCollection queryString = obj2.Request.QueryString;
			string code = queryString["code"];
			string returnedState = queryString["state"];
			string error = queryString["error"];
			await RespondAsync(obj2, error == null && code != null);
			if (error != null)
			{
				throw new ProviderException("O login foi cancelado ou negado.");
			}
			if (code == null || !string.Equals(returnedState, state, StringComparison.Ordinal))
			{
				throw new ProviderException("Resposta de login inválida.");
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>
			{
				["client_id"] = options.ClientId,
				["code"] = code,
				["code_verifier"] = verifier,
				["grant_type"] = "authorization_code",
				["redirect_uri"] = redirectUri
			};
			if (options.ClientSecret != null)
			{
				dictionary["client_secret"] = options.ClientSecret;
			}
			TokenResponse tokenResponse = await PostTokenAsync(dictionary, ct);
			if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
			{
				throw new ProviderException("O Google não devolveu um token de renovação. Tente entrar novamente.");
			}
			string replaced = _session?.RefreshToken;
			_session = new StoredSession(tokenResponse.AccessToken, tokenResponse.RefreshToken, DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
			await _store.SaveAsync(JsonSerializer.Serialize(_session), ct);
			SetState(AuthState.SignedIn);
			if (!string.IsNullOrEmpty(replaced) && replaced != tokenResponse.RefreshToken)
			{
				await RevokeAsync(replaced);
			}
		}
		catch
		{
			SetState((previous != AuthState.SigningIn) ? previous : AuthState.SignedOut);
			throw;
		}
	}

	public async Task SignOutAsync(CancellationToken ct)
	{
		string refresh = _session?.RefreshToken;
		_session = null;
		await _store.ClearAsync(ct);
		SetState(AuthState.SignedOut);
		if (!string.IsNullOrEmpty(refresh))
		{
			await RevokeAsync(refresh);
		}
	}

	private async Task RevokeAsync(string token)
	{
		try
		{
			using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
			using (await _http.PostAsync(RevokeEndpoint, content))
			{
			}
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException) ? 1 : 0) != 0)
		{
		}
	}

	public async Task<string> GetAccessTokenAsync(CancellationToken ct)
	{
		await _gate.WaitAsync(ct);
		try
		{
			StoredSession session = _session ?? throw new SessionExpiredException("Entre na sua conta para continuar.");
			if (session.ExpiresAtUtc - DateTime.UtcNow > TimeSpan.FromSeconds(60.0))
			{
				return session.AccessToken;
			}
			GoogleOAuthOptions googleOAuthOptions = _optionsFactory() ?? throw new ProviderConfigurationException("Client OAuth do Google não configurado. Veja a seção \"Credenciais\" do README.md.");
			Dictionary<string, string> dictionary = new Dictionary<string, string>
			{
				["client_id"] = googleOAuthOptions.ClientId,
				["refresh_token"] = session.RefreshToken,
				["grant_type"] = "refresh_token"
			};
			if (googleOAuthOptions.ClientSecret != null)
			{
				dictionary["client_secret"] = googleOAuthOptions.ClientSecret;
			}
			TokenResponse token;
			try
			{
				token = await PostTokenAsync(dictionary, ct);
			}
			catch (SessionExpiredException)
			{
				_session = null;
				await _store.ClearAsync(ct);
				SetState(AuthState.SessionExpired);
				throw;
			}
			_session = new StoredSession(token.AccessToken, string.IsNullOrEmpty(token.RefreshToken) ? session.RefreshToken : token.RefreshToken, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
			await _store.SaveAsync(JsonSerializer.Serialize(_session), ct);
			return _session.AccessToken;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
	{
		HttpResponseMessage response;
		try
		{
			using FormUrlEncodedContent content = new FormUrlEncodedContent(form);
			response = await _http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
		}
		catch (HttpRequestException inner)
		{
			throw new ServiceUnavailableException("Sem conexão com o Google.", inner);
		}
		catch (TaskCanceledException inner2) when (!ct.IsCancellationRequested)
		{
			throw new ServiceUnavailableException("Tempo esgotado falando com o Google.", inner2);
		}
		using (response)
		{
			TokenResponse token = null;
			try
			{
				token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
			}
			catch (JsonException)
			{
			}
			if (response.IsSuccessStatusCode && token?.AccessToken != null)
			{
				return token;
			}
			if (token?.Error == "invalid_grant")
			{
				throw new SessionExpiredException("Sua sessão expirou ou foi revogada. Entre novamente.");
			}
			throw new ProviderException("O Google recusou a autenticação (" + (token?.Error ?? ((int)response.StatusCode).ToString()) + ").");
		}
	}

	private static async Task RespondAsync(HttpListenerContext context, bool ok)
	{
		string s = (ok ? "<html><body style=\"font-family:sans-serif;background:#111;color:#eee;text-align:center;padding-top:20vh\"><h2>Login concluído</h2><p>Você já pode fechar esta aba e voltar ao aplicativo.</p></body></html>" : "<html><body style=\"font-family:sans-serif;background:#111;color:#eee;text-align:center;padding-top:20vh\"><h2>Login não concluído</h2><p>Volte ao aplicativo e tente novamente.</p></body></html>");
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		context.Response.ContentType = "text/html; charset=utf-8";
		context.Response.ContentLength64 = bytes.Length;
		await context.Response.OutputStream.WriteAsync(bytes);
		context.Response.Close();
	}

	private void SetState(AuthState state)
	{
		if (State != state)
		{
			State = state;
			this.StateChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private static int GetFreePort()
	{
		TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
		tcpListener.Start();
		int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		return port;
	}

	private static string Query(Dictionary<string, string> p)
	{
		return string.Join("&", p.Select<KeyValuePair<string, string>, string>((KeyValuePair<string, string> kv) => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
	}

	private static string Base64Url(byte[] data)
	{
		return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
	}
}
