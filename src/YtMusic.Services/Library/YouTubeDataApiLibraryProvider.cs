using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Library;

public sealed class YouTubeDataApiLibraryProvider : ILibraryProvider
{
	private const string BaseUrl = "https://www.googleapis.com/youtube/v3/";

	private readonly HttpClient _http;

	private readonly IAuthProvider _auth;

	private string? _likesPlaylistId;

	public LibraryCapabilities Capabilities => LibraryCapabilities.Playlists | LibraryCapabilities.LikedTracks | LibraryCapabilities.SearchTracks | LibraryCapabilities.SearchPlaylists | LibraryCapabilities.SearchArtists;

	public YouTubeDataApiLibraryProvider(HttpClient http, IAuthProvider auth)
	{
		_http = http;
		_auth = auth;
	}

	public async Task<Page<PlaylistSummary>> GetPlaylistsAsync(string? pageToken, CancellationToken ct)
	{
		using JsonDocument doc = await GetAsync("playlists?part=snippet,contentDetails&mine=true&maxResults=50" + PageParam(pageToken), ct);
		List<PlaylistSummary> list = new List<PlaylistSummary>();
		foreach (JsonElement item in Items(doc))
		{
			string @string = item.GetProperty("id").GetString();
			JsonElement property = item.GetProperty("snippet");
			JsonElement value;
			JsonElement value2;
			int? itemCount = ((item.TryGetProperty("contentDetails", out value) && value.TryGetProperty("itemCount", out value2)) ? new int?(value2.GetInt32()) : null);
			list.Add(new PlaylistSummary(@string, property.GetProperty("title").GetString() ?? "(sem título)", itemCount, Thumbnail(property)));
		}
		return new Page<PlaylistSummary>(list, NextToken(doc));
	}

	public async Task<Page<Track>> GetLikedTracksAsync(string? pageToken, CancellationToken ct)
	{
		if (_likesPlaylistId == null)
		{
			_likesPlaylistId = await ResolveLikesPlaylistAsync(ct);
		}
		return await GetPlaylistTracksAsync(_likesPlaylistId, pageToken, ct);
	}

	public async Task<Page<Track>> GetPlaylistTracksAsync(string playlistId, string? pageToken, CancellationToken ct)
	{
		using JsonDocument doc = await GetAsync("playlistItems?part=snippet,contentDetails&maxResults=50&playlistId=" + Uri.EscapeDataString(playlistId) + PageParam(pageToken), ct);
		List<(string Id, string Title, string Artist, string? Art)> drafts = new List<(string, string, string, string)>();
		foreach (JsonElement item in Items(doc))
		{
			JsonElement property = item.GetProperty("snippet");
			string text = property.GetProperty("title").GetString() ?? "";
			if ((!(text == "Private video") && !(text == "Deleted video")) || 1 == 0)
			{
				string @string = item.GetProperty("contentDetails").GetProperty("videoId").GetString();
				if (!string.IsNullOrEmpty(@string))
				{
					string name = StringOrNull(property, "videoOwnerChannelTitle") ?? StringOrNull(property, "channelTitle") ?? "Desconhecido";
					drafts.Add((@string, text, CleanArtist(name), Thumbnail(property)));
				}
			}
		}
		Dictionary<string, TimeSpan> durations = await GetDurationsAsync(drafts.Select<(string, string, string, string), string>(((string Id, string Title, string Artist, string Art) d) => d.Id), ct);
		return new Page<Track>(drafts.Select<(string, string, string, string), Track>(((string Id, string Title, string Artist, string Art) d) => new Track(d.Id, d.Title, d.Artist, null, durations.GetValueOrDefault(d.Id), d.Art)).ToList(), NextToken(doc));
	}

	public async Task<SearchResults> SearchAsync(string query, SearchKind kind, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return SearchResults.Empty;
		}
		string text = Uri.EscapeDataString(query.Trim());
		switch (kind)
		{
		case SearchKind.Tracks:
		{
			using JsonDocument doc = await GetAsync("search?part=snippet&type=video&videoCategoryId=10&videoEmbeddable=true&maxResults=25&q=" + text, ct);
			List<(string Id, string Title, string Artist, string? Art)> drafts = new List<(string, string, string, string)>();
			foreach (JsonElement item in Items(doc))
			{
				string string3 = item.GetProperty("id").GetProperty("videoId").GetString();
				if (!string.IsNullOrEmpty(string3))
				{
					JsonElement property3 = item.GetProperty("snippet");
					drafts.Add((string3, WebUtility.HtmlDecode(property3.GetProperty("title").GetString() ?? ""), CleanArtist(StringOrNull(property3, "channelTitle") ?? "Desconhecido"), Thumbnail(property3)));
				}
			}
			Dictionary<string, TimeSpan> durations = await GetDurationsAsync(drafts.Select<(string, string, string, string), string>(((string Id, string Title, string Artist, string Art) d) => d.Id), ct);
			List<Track> tracks = drafts.Select<(string, string, string, string), Track>(((string Id, string Title, string Artist, string Art) d) => new Track(d.Id, d.Title, d.Artist, null, durations.GetValueOrDefault(d.Id), d.Art)).ToList();
			return SearchResults.Empty with
			{
				Tracks = tracks
			};
		}
		case SearchKind.Playlists:
		{
			using JsonDocument doc3 = await GetAsync("search?part=snippet&type=playlist&maxResults=25&q=" + text, ct);
			List<PlaylistSummary> list2 = new List<PlaylistSummary>();
			foreach (JsonElement item2 in Items(doc3))
			{
				string string2 = item2.GetProperty("id").GetProperty("playlistId").GetString();
				if (!string.IsNullOrEmpty(string2))
				{
					JsonElement property2 = item2.GetProperty("snippet");
					list2.Add(new PlaylistSummary(string2, WebUtility.HtmlDecode(property2.GetProperty("title").GetString() ?? ""), null, Thumbnail(property2)));
				}
			}
			return SearchResults.Empty with
			{
				Playlists = list2
			};
		}
		default:
		{
			using JsonDocument doc2 = await GetAsync("search?part=snippet&type=channel&maxResults=25&q=" + text, ct);
			List<ChannelSummary> list = new List<ChannelSummary>();
			foreach (JsonElement item3 in Items(doc2))
			{
				string @string = item3.GetProperty("id").GetProperty("channelId").GetString();
				if (!string.IsNullOrEmpty(@string))
				{
					JsonElement property = item3.GetProperty("snippet");
					list.Add(new ChannelSummary(@string, WebUtility.HtmlDecode(property.GetProperty("title").GetString() ?? ""), Thumbnail(property)));
				}
			}
			return SearchResults.Empty with
			{
				Channels = list
			};
		}
		}
	}

	private async Task<string> ResolveLikesPlaylistAsync(CancellationToken ct)
	{
		using JsonDocument doc = await GetAsync("channels?part=contentDetails&mine=true", ct);
		foreach (JsonElement item in Items(doc))
		{
			if (item.TryGetProperty("contentDetails", out var value) && value.TryGetProperty("relatedPlaylists", out var value2) && value2.TryGetProperty("likes", out var value3))
			{
				string @string = value3.GetString();
				if (@string != null && @string.Length > 0)
				{
					return @string;
				}
			}
		}
		return "LL";
	}

	private async Task<Dictionary<string, TimeSpan>> GetDurationsAsync(IEnumerable<string> ids, CancellationToken ct)
	{
		Dictionary<string, TimeSpan> result = new Dictionary<string, TimeSpan>();
		List<string> list = ids.Distinct().ToList();
		if (list.Count == 0)
		{
			return result;
		}
		using JsonDocument doc = await GetAsync("videos?part=contentDetails&maxResults=50&id=" + string.Join(",", list), ct);
		foreach (JsonElement item in Items(doc))
		{
			string @string = item.GetProperty("id").GetString();
			string string2 = item.GetProperty("contentDetails").GetProperty("duration").GetString();
			if (@string != null && string2 != null)
			{
				try
				{
					result[@string] = XmlConvert.ToTimeSpan(string2);
				}
				catch (FormatException)
				{
				}
			}
		}
		return result;
	}

	private async Task<JsonDocument> GetAsync(string pathAndQuery, CancellationToken ct)
	{
		string parameter = await _auth.GetAccessTokenAsync(ct);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/" + pathAndQuery);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameter);
		HttpResponseMessage response;
		try
		{
			response = await _http.SendAsync(request, ct);
		}
		catch (HttpRequestException inner)
		{
			throw new ServiceUnavailableException("Sem conexão com o YouTube. Verifique sua internet e tente de novo.", inner);
		}
		catch (TaskCanceledException inner2) when (!ct.IsCancellationRequested)
		{
			throw new ServiceUnavailableException("O YouTube demorou demais para responder.", inner2);
		}
		using (response)
		{
			string text = await response.Content.ReadAsStringAsync(ct);
			if (response.IsSuccessStatusCode)
			{
				return JsonDocument.Parse(text);
			}
			throw MapError(response.StatusCode, text);
		}
	}

	private static ProviderException MapError(HttpStatusCode status, string body)
	{
		string text = ErrorReason(body);
		switch (status)
		{
		case HttpStatusCode.Unauthorized:
			return new SessionExpiredException("Sua sessão expirou. Entre novamente.");
		case HttpStatusCode.Forbidden:
		{
			bool flag;
			switch (text)
			{
			case "quotaExceeded":
			case "dailyLimitExceeded":
			case "rateLimitExceeded":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				return new QuotaExceededException("A cota diária da API do YouTube acabou. Ela renova à meia-noite (horário do Pacífico).");
			}
			switch (text)
			{
			case "insufficientPermissions":
			case "forbidden":
			case "accessNotConfigured":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				return new SessionExpiredException("Permissão insuficiente. Entre novamente e aceite o acesso solicitado.");
			}
			break;
		}
		}
		if (status == HttpStatusCode.NotFound)
		{
			return new ProviderException("Item não encontrado (pode ser privado ou removido).");
		}
		return new ProviderException($"O YouTube retornou um erro ({status}).");
	}

	private static string? ErrorReason(string body)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			return jsonDocument.RootElement.GetProperty("error").GetProperty("errors")[0].GetProperty("reason").GetString();
		}
		catch (Exception ex) when (((ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException || ex is IndexOutOfRangeException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static IEnumerable<JsonElement> Items(JsonDocument doc)
	{
		if (!doc.RootElement.TryGetProperty("items", out var value))
		{
			return Enumerable.Empty<JsonElement>();
		}
		return value.EnumerateArray().ToList();
	}

	private static string? NextToken(JsonDocument doc)
	{
		return StringOrNull(doc.RootElement, "nextPageToken");
	}

	private static string PageParam(string? token)
	{
		if (!string.IsNullOrEmpty(token))
		{
			return "&pageToken=" + Uri.EscapeDataString(token);
		}
		return "";
	}

	private static string? StringOrNull(JsonElement e, string name)
	{
		if (!e.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static string? Thumbnail(JsonElement snippet)
	{
		if (!snippet.TryGetProperty("thumbnails", out var value))
		{
			return null;
		}
		string[] array = new string[3] { "high", "medium", "default" };
		foreach (string propertyName in array)
		{
			if (value.TryGetProperty(propertyName, out var value2))
			{
				string text = StringOrNull(value2, "url");
				if (text != null)
				{
					return text;
				}
			}
		}
		return null;
	}

	private static string CleanArtist(string name)
	{
		if (!name.EndsWith(" - Topic", StringComparison.Ordinal))
		{
			return name;
		}
		return name.Substring(0, name.Length - 8);
	}
}
