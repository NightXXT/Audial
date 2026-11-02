using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Demo;

public sealed class DemoLibraryProvider : ILibraryProvider
{
	private static readonly string[] Titles = new string[12]
	{
		"Spectral Horizons", "Velvet Echoes", "Afterglow", "Silent Orbit", "Glasshouse", "Low Tide", "Neon Rain", "Night Drive", "City Lights", "Blue Hour",
		"Slow Motion", "Last Train"
	};

	private static readonly Track[] All = (from i in Enumerable.Range(1, 12)
		select new Track($"demo-{i}", Titles[i - 1], (i <= 6) ? "Aether Collective" : "Midnight Avenue", (i <= 6) ? "Spectral Horizons" : "Neon Rain", TimeSpan.FromSeconds(150 + i * 7), "ms-appx:///Assets/Artwork/" + ((i <= 6) ? "prism" : "night") + ".png", IsDemo: true)).ToArray();

	private static readonly PlaylistSummary[] Playlists = new PlaylistSummary[3]
	{
		new PlaylistSummary("demo-pl-1", "Spectral Horizons", 6, "ms-appx:///Assets/Artwork/prism.png", IsDemo: true),
		new PlaylistSummary("demo-pl-2", "Neon Rain", 6, "ms-appx:///Assets/Artwork/night.png", IsDemo: true),
		new PlaylistSummary("demo-pl-3", "Ruby Frequencies", 6, "ms-appx:///Assets/Artwork/ruby.png", IsDemo: true)
	};

	public LibraryCapabilities Capabilities => LibraryCapabilities.Playlists | LibraryCapabilities.LikedTracks | LibraryCapabilities.SearchTracks | LibraryCapabilities.SearchPlaylists | LibraryCapabilities.SearchArtists;

	public async Task<Page<PlaylistSummary>> GetPlaylistsAsync(string? pageToken, CancellationToken ct)
	{
		await Task.Delay(400, ct);
		return new Page<PlaylistSummary>(Playlists, null);
	}

	public async Task<Page<Track>> GetLikedTracksAsync(string? pageToken, CancellationToken ct)
	{
		await Task.Delay(400, ct);
		return new Page<Track>(All.Take(5).ToList(), null);
	}

	public async Task<Page<Track>> GetPlaylistTracksAsync(string playlistId, string? pageToken, CancellationToken ct)
	{
		await Task.Delay(400, ct);
		int count = ((playlistId == "demo-pl-2") ? 6 : 0);
		return new Page<Track>(All.Skip(count).Take(6).ToList(), null);
	}

	public async Task<SearchResults> SearchAsync(string query, SearchKind kind, CancellationToken ct)
	{
		string query2 = query;
		await Task.Delay(500, ct);
		if (string.IsNullOrWhiteSpace(query2))
		{
			return SearchResults.Empty;
		}
		if (query2.Trim().Equals("vazio", StringComparison.OrdinalIgnoreCase))
		{
			return SearchResults.Empty;
		}
		return kind switch
		{
			SearchKind.Tracks => SearchResults.Empty with
			{
				Tracks = All.Where((Track t) => $"{t.Title} {t.Artist} {t.Album}".Contains(query2.Trim(), StringComparison.OrdinalIgnoreCase)).ToList()
			}, 
			SearchKind.Playlists => SearchResults.Empty with
			{
				Playlists = Playlists.Where((PlaylistSummary p) => p.Title.Contains(query2.Trim(), StringComparison.OrdinalIgnoreCase)).ToList()
			}, 
			_ => SearchResults.Empty with
			{
				Channels = new ChannelSummary[1]
				{
					new ChannelSummary("demo-ch", "[DEMO] Canal de exemplo", null)
				}
			}, 
		};
	}
}
