using System;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Services.Library;

/// <summary>
/// Reads the personal library through the official YouTube Data API, which accepts desktop OAuth,
/// and keeps search and home on the public YouTube Music catalog.
/// </summary>
public sealed class HybridLibraryProvider : IMusicLibraryProvider, ILibraryProvider
{
	private readonly YouTubeMusicLibraryProvider _music;

	private readonly YouTubeDataApiLibraryProvider _data;

	public LibraryCapabilities Capabilities => _music.Capabilities;

	public HybridLibraryProvider(YouTubeMusicLibraryProvider music, YouTubeDataApiLibraryProvider data)
	{
		_music = music;
		_data = data;
	}

	public Task<Page<PlaylistSummary>> GetPlaylistsAsync(string? pageToken, CancellationToken ct)
	{
		return _data.GetPlaylistsAsync(pageToken, ct);
	}

	public async Task<Page<Track>> GetLikedTracksAsync(string? pageToken, CancellationToken ct)
	{
		return await WithSongArtworkAsync(await _data.GetLikedTracksAsync(pageToken, ct), ct);
	}

	public Task<Page<PlaylistSummary>> GetAlbumsAsync(string? pageToken, CancellationToken ct)
	{
		throw new ProviderConfigurationException("A API oficial do YouTube não informa álbuns salvos. Suas playlists e músicas curtidas estão disponíveis na biblioteca.");
	}

	public Task<Page<PlaylistSummary>> GetHomeAsync(string? pageToken, CancellationToken ct)
	{
		return _music.GetHomeAsync(pageToken, ct);
	}

	public async Task<Page<Track>> GetPlaylistTracksAsync(string playlistId, string? pageToken, CancellationToken ct)
	{
		// Album and browse ids only exist in the Music catalog.
		if (playlistId.StartsWith("MP", StringComparison.Ordinal) || playlistId.StartsWith("VL", StringComparison.Ordinal))
		{
			return await _music.GetPlaylistTracksAsync(playlistId, pageToken, ct);
		}
		try
		{
			return await WithSongArtworkAsync(await _data.GetPlaylistTracksAsync(playlistId, pageToken, ct), ct);
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			try
			{
				return await _music.GetPlaylistTracksAsync(playlistId, pageToken, ct);
			}
			catch (Exception ex2) when (!(ex2 is OperationCanceledException))
			{
				throw ex;
			}
		}
	}

	// The Data API only knows video thumbnails; the Music catalog has the album covers.
	private async Task<Page<Track>> WithSongArtworkAsync(Page<Track> page, CancellationToken ct)
	{
		return page with
		{
			Items = await _music.WithSongArtworkAsync(page.Items, ct)
		};
	}

	public Task<SearchResults> SearchAsync(string query, SearchKind kind, CancellationToken ct)
	{
		return _music.SearchAsync(query, kind, ct);
	}
}
