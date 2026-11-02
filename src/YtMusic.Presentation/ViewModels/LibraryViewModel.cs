using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Presentation.ViewModels;

public sealed class LibraryViewModel : LoadableViewModel
{
	private const int MaxPages = 10;

	private readonly ILibraryProvider _library;

	public ObservableCollection<PlaylistSummary> Playlists { get; } = new ObservableCollection<PlaylistSummary>();


	public LibraryCollection Collection { get; set; }

	public string Title
	{
		get
		{
			if (Collection != LibraryCollection.Albums)
			{
				return "Sua biblioteca";
			}
			return "Seus álbuns";
		}
	}

	public LibraryViewModel(IAuthProvider auth, ILibraryProvider library)
		: base(auth)
	{
		_library = library;
	}

	public override Task ReloadAsync()
	{
		return RunAsync(async delegate(CancellationToken ct)
		{
			List<PlaylistSummary> all = new List<PlaylistSummary>();
			string text = null;
			HashSet<string> seenTokens = new HashSet<string>();
			for (int i = 0; i < 10; i++)
			{
				Page<PlaylistSummary> page = ((!(_library is IMusicLibraryProvider musicLibraryProvider)) ? (await _library.GetPlaylistsAsync(text, ct)) : (Collection switch
				{
					LibraryCollection.Albums => await musicLibraryProvider.GetAlbumsAsync(text, ct), 
					LibraryCollection.Recommendations => await musicLibraryProvider.GetHomeAsync(text, ct), 
					_ => await musicLibraryProvider.GetPlaylistsAsync(text, ct), 
				}));
				Page<PlaylistSummary> page2 = page;
				all.AddRange(page2.Items);
				text = page2.NextPageToken;
				if (text == null || !seenTokens.Add(text) || (Collection == LibraryCollection.Recommendations && i >= 2))
				{
					break;
				}
			}
			Playlists.Clear();
			foreach (PlaylistSummary item in all.DistinctBy((PlaylistSummary x) => x.Id))
			{
				Playlists.Add(item);
			}
			return Playlists.Count > 0;
		}, "Nenhum item encontrado nesta coleção do YouTube Music.");
	}
}
