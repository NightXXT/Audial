using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.Presentation.ViewModels;

public sealed class SearchViewModel : LoadableViewModel
{
	private readonly ILibraryProvider _library;

	private readonly PlaybackCoordinator _playback;

	private string _query = "";

	private SearchKind _kind;

	private bool _hasSearched;

	private bool _all = true;

	private PlaylistSummary? _best;

	public ObservableCollection<Track> Tracks { get; } = new ObservableCollection<Track>();


	public ObservableCollection<Track> TopTracks { get; } = new ObservableCollection<Track>();


	public PlaylistSummary? Best
	{
		get
		{
			return _best;
		}
		private set
		{
			if (SetProperty(ref _best, value, "Best"))
			{
				OnPropertyChanged("HasBest");
			}
		}
	}

	public bool HasBest => (object)_best != null;

	public bool ShowAll => _all;

	public bool HasTopTracks => TopTracks.Count > 0;

	public bool HasPlaylists => Playlists.Count > 0;

	public bool HasChannels => Channels.Count > 0;


	public ObservableCollection<PlaylistSummary> Playlists { get; } = new ObservableCollection<PlaylistSummary>();


	public ObservableCollection<ChannelSummary> Channels { get; } = new ObservableCollection<ChannelSummary>();


	public IAsyncRelayCommand SearchCommand { get; }

	public IRelayCommand CancelCommand { get; }

	public IAsyncRelayCommand<Track> PlayTrackCommand { get; }

	public IRelayCommand<Track> EnqueueCommand { get; }

	public bool CanSearchAlbums => (_library.Capabilities & LibraryCapabilities.SearchAlbums) != 0;

	public string Query
	{
		get
		{
			return _query;
		}
		set
		{
			SetProperty(ref _query, value, "Query");
		}
	}

	public SearchKind Kind
	{
		get
		{
			return _kind;
		}
		set
		{
			if (SetProperty(ref _kind, value, "Kind"))
			{
				OnPropertyChanged("ShowTracks");
				OnPropertyChanged("ShowPlaylists");
				OnPropertyChanged("ShowChannels");
				if (_hasSearched && !string.IsNullOrWhiteSpace(_query))
				{
					ReloadAsync();
				}
			}
		}
	}

	public bool ShowTracks => !_all && Kind == SearchKind.Tracks;

	public bool ShowPlaylists
	{
		get
		{
			SearchKind kind = Kind;
			if (!_all && (kind == SearchKind.Playlists || kind == SearchKind.Albums))
			{
				return true;
			}
			return false;
		}
	}

	public bool ShowChannels => !_all && Kind == SearchKind.Artists;

	public void SelectAll()
	{
		_all = true;
		ApplySelection();
	}

	public void SelectKind(SearchKind kind)
	{
		_all = false;
		_kind = kind;
		ApplySelection();
	}

	private void ApplySelection()
	{
		OnPropertyChanged("ShowAll");
		OnPropertyChanged("ShowTracks");
		OnPropertyChanged("ShowPlaylists");
		OnPropertyChanged("ShowChannels");
		if (_hasSearched && !string.IsNullOrWhiteSpace(_query))
		{
			ReloadAsync();
		}
	}

	public SearchViewModel(IAuthProvider auth, ILibraryProvider library, PlaybackCoordinator playback)
		: base(auth)
	{
		_library = library;
		_playback = playback;
		SearchCommand = new AsyncRelayCommand(ReloadAsync);
		CancelCommand = new RelayCommand(base.CancelWork);
		PlayTrackCommand = new AsyncRelayCommand<Track>((Track? t) => ((object)t != null) ? _playback.PlayQueueAsync(Tracks.ToList(), Tracks.IndexOf(t)) : Task.CompletedTask);
		EnqueueCommand = new RelayCommand<Track>(delegate(Track? t)
		{
			if ((object)t != null)
			{
				_playback.Enqueue(t);
			}
		});
		base.State = ViewState.Empty;
		base.StateMessage = "Digite algo e pressione Enter para buscar.";
	}

	public override Task ReloadAsync()
	{
		if (string.IsNullOrWhiteSpace(_query))
		{
			base.State = ViewState.Empty;
			base.StateMessage = "Digite algo e pressione Enter para buscar.";
			return Task.CompletedTask;
		}
		_hasSearched = true;
		return RunAsync(async delegate(CancellationToken ct)
		{
			SearchResults[] results = _all
				? await Task.WhenAll(_library.SearchAsync(_query, SearchKind.Tracks, ct), _library.SearchAsync(_query, SearchKind.Playlists, ct), _library.SearchAsync(_query, SearchKind.Artists, ct))
				: new SearchResults[1] { await _library.SearchAsync(_query, _kind, ct) };
			Tracks.Clear();
			TopTracks.Clear();
			Playlists.Clear();
			Channels.Clear();
			foreach (SearchResults searchResults in results)
			{
				foreach (Track track in searchResults.Tracks)
				{
					Tracks.Add(track);
				}
				foreach (PlaylistSummary playlist in searchResults.Playlists)
				{
					Playlists.Add(playlist);
				}
				foreach (ChannelSummary channel in searchResults.Channels)
				{
					Channels.Add(channel);
				}
			}
			foreach (Track top in Tracks.Take(5))
			{
				TopTracks.Add(top);
			}
			Best = Playlists.FirstOrDefault();
			OnPropertyChanged("HasTopTracks");
			OnPropertyChanged("HasPlaylists");
			OnPropertyChanged("HasChannels");
			return Tracks.Count + Playlists.Count + Channels.Count > 0;
		}, "Nada encontrado para \"" + _query + "\".");
	}
}
