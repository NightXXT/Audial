using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.Presentation.ViewModels;

public sealed class TrackListViewModel : LoadableViewModel
{
	private readonly ILibraryProvider _library;

	private readonly PlaybackCoordinator _playback;

	private TrackListRequest _request = new TrackListRequest(TrackListKind.Liked, null, "Músicas curtidas");

	private string _title = "";

	private string? _nextPageToken;

	private bool _isLoadingMore;

	public ObservableCollection<Track> Tracks { get; } = new ObservableCollection<Track>();


	public IAsyncRelayCommand PlayAllCommand { get; }

	public IAsyncRelayCommand<Track> PlayTrackCommand { get; }

	public IRelayCommand<Track> EnqueueCommand { get; }

	public IAsyncRelayCommand LoadMoreCommand { get; }

	public bool HasMore => !string.IsNullOrEmpty(_nextPageToken);

	public bool IsLoadingMore
	{
		get
		{
			return _isLoadingMore;
		}
		private set
		{
			SetProperty(ref _isLoadingMore, value, "IsLoadingMore");
		}
	}

	public string Title
	{
		get
		{
			return _title;
		}
		private set
		{
			SetProperty(ref _title, value, "Title");
		}
	}

	public TrackListViewModel(IAuthProvider auth, ILibraryProvider library, PlaybackCoordinator playback)
		: base(auth)
	{
		_library = library;
		_playback = playback;
		PlayAllCommand = new AsyncRelayCommand(() => PlayFromAsync(0));
		PlayTrackCommand = new AsyncRelayCommand<Track>((Track? t) => ((object)t != null) ? PlayFromAsync(Tracks.IndexOf(t)) : Task.CompletedTask);
		EnqueueCommand = new RelayCommand<Track>(delegate(Track? t)
		{
			if ((object)t != null)
			{
				_playback.Enqueue(t);
			}
		});
		LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
	}

	public Task LoadAsync(TrackListRequest request)
	{
		_request = request;
		_nextPageToken = null;
		OnPropertyChanged("HasMore");
		Tracks.Clear();
		Title = request.Title;
		return ReloadAsync();
	}

	public override Task ReloadAsync()
	{
		return RunAsync(async delegate(CancellationToken ct)
		{
			Page<Track> page = await GetPageAsync(null, ct);
			Tracks.Clear();
			foreach (Track item in page.Items)
			{
				Tracks.Add(item);
			}
			_nextPageToken = page.NextPageToken;
			OnPropertyChanged("HasMore");
			return Tracks.Count > 0;
		}, "Esta lista está vazia (ou não tem faixas disponíveis).");
	}

	private async Task LoadMoreAsync()
	{
		if (!HasMore || IsLoadingMore)
		{
			return;
		}
		IsLoadingMore = true;
		try
		{
			Page<Track> page = await GetPageAsync(_nextPageToken, CancellationToken.None);
			foreach (Track track in page.Items)
			{
				if (Tracks.All((Track existing) => existing.Id != track.Id))
				{
					Tracks.Add(track);
				}
			}
			_nextPageToken = page.NextPageToken;
			OnPropertyChanged("HasMore");
		}
		catch (ProviderException ex)
		{
			base.StateMessage = ex.Message;
		}
		finally
		{
			IsLoadingMore = false;
		}
	}

	private Task<Page<Track>> GetPageAsync(string? token, CancellationToken ct)
	{
		if (_request.Kind != TrackListKind.Liked)
		{
			return _library.GetPlaylistTracksAsync(_request.PlaylistId, token, ct);
		}
		return _library.GetLikedTracksAsync(token, ct);
	}

	private Task PlayFromAsync(int index)
	{
		if (Tracks.Count != 0 && index >= 0)
		{
			return _playback.PlayQueueAsync(Tracks.ToList(), index);
		}
		return Task.CompletedTask;
	}
}
