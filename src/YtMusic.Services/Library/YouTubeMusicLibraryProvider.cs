using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Library;

public sealed class YouTubeMusicLibraryProvider : IMusicLibraryProvider, ILibraryProvider
{
	private static readonly Uri ServiceRoot = new Uri("https://music.youtube.com/youtubei/v1/");

	private readonly HttpClient _http;

	private readonly IAuthProvider _auth;

	private readonly bool _authenticate;

	public LibraryCapabilities Capabilities => LibraryCapabilities.Playlists | LibraryCapabilities.LikedTracks | LibraryCapabilities.SearchTracks | LibraryCapabilities.SearchPlaylists | LibraryCapabilities.SearchArtists | LibraryCapabilities.SearchAlbums;

	public YouTubeMusicLibraryProvider(HttpClient http, IAuthProvider auth, bool authenticate = true)
	{
		_http = http;
		_auth = auth;
		_authenticate = authenticate;
	}

	public Task<Page<PlaylistSummary>> GetPlaylistsAsync(string? pageToken, CancellationToken ct)
	{
		EnsurePersonalLibraryAvailable();
		return BrowseCollectionsAsync("FEmusic_liked_playlists", pageToken, ct);
	}

	public Task<Page<PlaylistSummary>> GetAlbumsAsync(string? pageToken, CancellationToken ct)
	{
		EnsurePersonalLibraryAvailable();
		return BrowseCollectionsAsync("FEmusic_liked_albums", pageToken, ct);
	}

	public Task<Page<PlaylistSummary>> GetHomeAsync(string? pageToken, CancellationToken ct)
	{
		return BrowseCollectionsAsync("FEmusic_home", pageToken, ct);
	}

	private async Task<Page<PlaylistSummary>> BrowseCollectionsAsync(string browseId, string? pageToken, CancellationToken ct)
	{
		using JsonDocument jsonDocument = await SendAsync("browse", new Dictionary<string, object> { ["browseId"] = browseId }, pageToken, ct, useGoogleAuthorization: false);
		return new Page<PlaylistSummary>(ParseCollections(jsonDocument.RootElement), Continuation(jsonDocument.RootElement));
	}

	public Task<Page<Track>> GetLikedTracksAsync(string? pageToken, CancellationToken ct)
	{
		EnsurePersonalLibraryAvailable();
		return GetPlaylistTracksAsync("LM", pageToken, ct);
	}

	public async Task<Page<Track>> GetPlaylistTracksAsync(string playlistId, string? pageToken, CancellationToken ct)
	{
		string value = ((playlistId.StartsWith("MP", StringComparison.Ordinal) || playlistId.StartsWith("VL", StringComparison.Ordinal)) ? playlistId : ("VL" + playlistId));
		using JsonDocument jsonDocument = await SendAsync("browse", new Dictionary<string, object> { ["browseId"] = value }, pageToken, ct, useGoogleAuthorization: false);
		return new Page<Track>(ParseTracks(jsonDocument.RootElement), Continuation(jsonDocument.RootElement));
	}

	public async Task<SearchResults> SearchAsync(string query, SearchKind kind, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return SearchResults.Empty;
		}
		string value = kind switch
		{
			SearchKind.Tracks => "EgWKAQIIAWoMEA4QChADEAQQCRAF", 
			SearchKind.Albums => "EgWKAQIYAWoMEA4QChADEAQQCRAF", 
			SearchKind.Artists => "EgWKAQIgAWoMEA4QChADEAQQCRAF", 
			SearchKind.Playlists => "Eg-KAQwIABAAGAAgACgBMABqChAEEAMQCRAFEAo%3D", 
			_ => throw new ArgumentOutOfRangeException("kind"), 
		};
		using JsonDocument jsonDocument = await SendAsync("search", new Dictionary<string, object>
		{
			["query"] = query.Trim(),
			["params"] = value
		}, null, ct, useGoogleAuthorization: false);
		return kind switch
		{
			SearchKind.Tracks => SearchResults.Empty with
			{
				Tracks = ParseTracks(jsonDocument.RootElement)
			}, 
			SearchKind.Artists => SearchResults.Empty with
			{
				Channels = ParseArtists(jsonDocument.RootElement)
			}, 
			_ => SearchResults.Empty with
			{
				Playlists = ParseCollections(jsonDocument.RootElement)
			}, 
		};
	}

	private void EnsurePersonalLibraryAvailable()
	{
		if (!_authenticate)
		{
			throw new ProviderConfigurationException("Conecte sua conta Google para acessar sua biblioteca pessoal.");
		}
		throw new ProviderConfigurationException("A busca pública funciona, mas o YouTube Music rejeita OAuth de aplicativo desktop para acessar a biblioteca pessoal. Abra suas coleções no YouTube Music oficial.");
	}

	private async Task<JsonDocument> SendAsync(string endpoint, Dictionary<string, object> body, string? continuation, CancellationToken ct, bool useGoogleAuthorization = true)
	{
		body["context"] = new
		{
			client = new
			{
				clientName = "WEB_REMIX",
				clientVersion = "1." + DateTime.UtcNow.ToString("yyyyMMdd") + ".01.00",
				hl = "pt",
				gl = "BR"
			},
			user = new { }
		};
		string text = endpoint + "?alt=json";
		if (continuation != null)
		{
			text = text + "&ctoken=" + Uri.EscapeDataString(continuation) + "&continuation=" + Uri.EscapeDataString(continuation);
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(ServiceRoot, text));
		request.Headers.Referrer = new Uri("https://music.youtube.com/");
		request.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
		request.Headers.TryAddWithoutValidation("X-Goog-Request-Time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
		request.Headers.UserAgent.ParseAdd("AudialDesktop/0.4");
		if (useGoogleAuthorization && _authenticate)
		{
			HttpRequestHeaders headers = request.Headers;
			headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.GetAccessTokenAsync(ct));
		}
		request.Content = JsonContent.Create(body);
		try
		{
			using HttpResponseMessage response = await _http.SendAsync(request, ct);
			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				throw new SessionExpiredException("A sessão do YouTube Music expirou. Conecte novamente sua conta.");
			}
			if (response.StatusCode == HttpStatusCode.Forbidden)
			{
				throw new ProviderException("O YouTube Music recusou esta autorização. Use a nova conexão do Music; a sessão antiga do YouTube comum não tem a permissão necessária.");
			}
			if (!response.IsSuccessStatusCode)
			{
				throw new ServiceUnavailableException($"O YouTube Music não respondeu à solicitação (HTTP {response.StatusCode}). Tente novamente.");
			}
			return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
		}
		catch (HttpRequestException)
		{
			throw new ServiceUnavailableException("Não foi possível conectar ao YouTube Music. Confira sua conexão.");
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			throw new ServiceUnavailableException("O YouTube Music demorou para responder. Tente novamente.");
		}
		catch (JsonException)
		{
			throw new ServiceUnavailableException("O formato da resposta do YouTube Music mudou. Esta integração experimental precisa ser atualizada.");
		}
	}

	public static IReadOnlyList<Track> ParseTracks(JsonElement root)
	{
		List<Track> list = new List<Track>();
		JsonElement jsonElement = Find(root, "musicResponsiveHeaderRenderer").Concat(Find(root, "musicDetailHeaderRenderer")).FirstOrDefault();
		string text = Text(At(jsonElement, "title"));
		string text2 = Artwork(jsonElement);
		foreach (JsonElement item in Renderers(root))
		{
			JsonElement[] source = Flex(item);
			JsonElement node = source.FirstOrDefault();
			string text3 = Text(At(item, "title"));
			if (string.IsNullOrWhiteSpace(text3))
			{
				text3 = Text(node);
			}
			string text4 = StringAt(At(item, "playlistItemData"), "videoId") ?? FirstString(At(node, "runs"), "videoId") ?? FirstString(At(item, "overlay"), "videoId") ?? FirstString(At(item, "navigationEndpoint"), "videoId");
			if (!string.IsNullOrWhiteSpace(text3) && !string.IsNullOrWhiteSpace(text4) && At(item, "isUnavailable").ValueKind != JsonValueKind.True)
			{
				JsonElement[] array = source.Skip(1).ToArray();
				JsonElement[] source2 = array.SelectMany((JsonElement x) => Array(At(x, "runs"))).ToArray();
				string[] array2 = (from x in source2
					where (FirstString(At(x, "navigationEndpoint"), "browseId") ?? "").StartsWith("UC", StringComparison.Ordinal)
					select StringAt(x, "text") into x
					where !string.IsNullOrWhiteSpace(x)
					select x).ToArray();
				JsonElement node2 = source2.FirstOrDefault((JsonElement x) => (FirstString(At(x, "navigationEndpoint"), "browseId") ?? "").StartsWith("MP", StringComparison.Ordinal));
				string text5 = ((array2.Length != 0) ? string.Join(", ", array2) : ((array.Length != 0) ? Text(array[0]).Split('•')[0].Trim() : ""));
				if (string.IsNullOrWhiteSpace(text5))
				{
					text5 = "Artista";
				}
				string text6 = FirstDuration(At(item, "fixedColumns")) ?? array.Select(FirstDuration).FirstOrDefault((string x) => x != null);
				TimeSpan? duration = null;
				if (text6 != null)
				{
					int[] array3 = text6.Split(':').Select(int.Parse).ToArray();
					duration = ((array3.Length == 3) ? new TimeSpan(array3[0], array3[1], array3[2]) : new TimeSpan(0, array3[0], array3[1]));
				}
				list.Add(new Track(text4, text3, text5, StringAt(node2, "text") ?? (string.IsNullOrWhiteSpace(text) ? null : text), duration, Artwork(item) ?? text2));
			}
		}
		return list.DistinctBy((Track x) => x.Id).ToArray();
	}

	public static IReadOnlyList<PlaylistSummary> ParseCollections(JsonElement root)
	{
		List<PlaylistSummary> list = new List<PlaylistSummary>();
		foreach (JsonElement item in Renderers(root))
		{
			JsonElement node = At(item, "title");
			string text = Text(node);
			if (string.IsNullOrWhiteSpace(text))
			{
				node = Flex(item).FirstOrDefault();
				text = Text(node);
			}
			string text2 = FirstString(At(item, "navigationEndpoint"), "browseId") ?? FirstString(node, "browseId");
			if (!string.IsNullOrWhiteSpace(text) && text2 != null && (text2.StartsWith("VL", StringComparison.Ordinal) || text2.StartsWith("MP", StringComparison.Ordinal)))
			{
				if (text2.StartsWith("VL", StringComparison.Ordinal))
				{
					string text3 = text2;
					text2 = text3.Substring(2, text3.Length - 2);
				}
				if ((!(text2 == "LM") && !(text2 == "SE")) || 1 == 0)
				{
					list.Add(new PlaylistSummary(text2, text, null, Artwork(item)));
				}
			}
		}
		return list.DistinctBy((PlaylistSummary x) => x.Id).ToArray();
	}

	private static IReadOnlyList<ChannelSummary> ParseArtists(JsonElement root)
	{
		List<ChannelSummary> list = new List<ChannelSummary>();
		foreach (JsonElement item in Renderers(root))
		{
			JsonElement node = At(item, "title");
			string text = Text(node);
			if (string.IsNullOrWhiteSpace(text))
			{
				node = Flex(item).FirstOrDefault();
				text = Text(node);
			}
			string text2 = FirstString(At(item, "navigationEndpoint"), "browseId") ?? FirstString(node, "browseId");
			if (!string.IsNullOrWhiteSpace(text) && text2 != null && text2.StartsWith("UC", StringComparison.Ordinal))
			{
				list.Add(new ChannelSummary(text2, text, Artwork(item)));
			}
		}
		return list.DistinctBy((ChannelSummary x) => x.Id).ToArray();
	}

	private static IEnumerable<JsonElement> Renderers(JsonElement node)
	{
		if (node.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in node.EnumerateObject())
			{
				bool flag;
				switch (item.Name)
				{
				case "musicResponsiveListItemRenderer":
				case "musicTwoRowItemRenderer":
				case "musicMultiRowListItemRenderer":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					yield return item.Value;
					continue;
				}
				foreach (JsonElement item2 in Renderers(item.Value))
				{
					yield return item2;
				}
			}
		}
		else
		{
			if (node.ValueKind != JsonValueKind.Array)
			{
				yield break;
			}
			foreach (JsonElement item3 in node.EnumerateArray())
			{
				foreach (JsonElement item4 in Renderers(item3))
				{
					yield return item4;
				}
			}
		}
	}

	private static JsonElement[] Flex(JsonElement row)
	{
		return (from x in Array(At(row, "flexColumns"))
			select At(At(x, "musicResponsiveListItemFlexColumnRenderer"), "text")).ToArray();
	}

	private static string? Artwork(JsonElement row)
	{
		return (from x in Find(At(row, "thumbnail"), "thumbnails").Concat(Find(At(row, "thumbnailRenderer"), "thumbnails")).SelectMany(Array)
			select StringAt(x, "url")).LastOrDefault((string x) => x?.StartsWith("https://", StringComparison.Ordinal) ?? false);
	}

	private static string? FirstDuration(JsonElement node)
	{
		return (from x in Find(node, "text")
			select (x.ValueKind != JsonValueKind.String) ? Text(x) : x.GetString()).FirstOrDefault((string x) => x != null && Regex.IsMatch(x, "^\\d{1,3}:\\d{2}(?::\\d{2})?$"));
	}

	private static string? Continuation(JsonElement root)
	{
		foreach (JsonElement item in Find(root, "nextContinuationData"))
		{
			string text = StringAt(item, "continuation");
			if (text != null)
			{
				return text;
			}
		}
		foreach (JsonElement item2 in Find(root, "continuationCommand"))
		{
			string text2 = StringAt(item2, "token");
			if (text2 != null)
			{
				return text2;
			}
		}
		return null;
	}

	private static JsonElement At(JsonElement node, string name)
	{
		if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
		{
			return default(JsonElement);
		}
		return value;
	}

	private static string? StringAt(JsonElement node, string name)
	{
		JsonElement jsonElement = At(node, name);
		if (jsonElement.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return jsonElement.GetString();
	}

	private static IEnumerable<JsonElement> Array(JsonElement node)
	{
		if (node.ValueKind != JsonValueKind.Array)
		{
			return System.Array.Empty<JsonElement>();
		}
		return node.EnumerateArray().ToArray();
	}

	private static string Text(JsonElement node)
	{
		return StringAt(node, "simpleText") ?? string.Concat(from x in Array(At(node, "runs"))
			select StringAt(x, "text"));
	}

	private static string? FirstString(JsonElement node, string name)
	{
		return (from x in Find(node, name)
			where x.ValueKind == JsonValueKind.String
			select x.GetString()).FirstOrDefault();
	}

	private static IEnumerable<JsonElement> Find(JsonElement node, string name)
	{
		if (node.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in node.EnumerateObject())
			{
				if (item.Name == name)
				{
					yield return item.Value;
					continue;
				}
				foreach (JsonElement item2 in Find(item.Value, name))
				{
					yield return item2;
				}
			}
		}
		else
		{
			if (node.ValueKind != JsonValueKind.Array)
			{
				yield break;
			}
			foreach (JsonElement item3 in node.EnumerateArray())
			{
				foreach (JsonElement item4 in Find(item3, name))
				{
					yield return item4;
				}
			}
		}
	}
}
