using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT;
using YtMusic.App.Platform;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;
using YtMusic.Presentation.ViewModels;
using YtMusic.Services.Settings;

namespace YtMusic.App.Views;
public sealed partial class HomePage : Page, IReloadablePage
{














	private readonly ShellViewModel _shell;

	private readonly ListeningHistory _history;

	private CancellationTokenSource? _load;

	private readonly TaskCompletionSource _initialLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);











	public Task InitialLoadCompleted => _initialLoad.Task;

	public ObservableCollection<HomeCard> Recent { get; } = new ObservableCollection<HomeCard>();


	public ObservableCollection<HomeCard> Featured { get; } = new ObservableCollection<HomeCard>();


	public ObservableCollection<HomeCard> Quick { get; } = new ObservableCollection<HomeCard>();


	public string Greeting
	{
		get
		{
			int hour = DateTime.Now.Hour;
			if (hour >= 5 && hour < 12)
			{
				return "Bom dia";
			}
			return (hour >= 12 && hour < 18) ? "Boa tarde" : "Boa noite";
		}
	}


	public ObservableCollection<HomeCard> Albums { get; } = new ObservableCollection<HomeCard>();


	public string RecommendationCaption
	{
		get
		{
			if (!_shell.IsDemo)
			{
				if (!_shell.IsPublicMusic)
				{
					return "Da sua página inicial do YouTube Music";
				}
				return "Sugestões públicas do YouTube Music";
			}
			return "Demonstração com dados fictícios";
		}
	}

	public HomePage()
	{
		_shell = App.GetService<ShellViewModel>();
		_history = App.GetService<ListeningHistory>();
		InitializeComponent();
	}

	protected override async void OnNavigatedTo(NavigationEventArgs e)
	{
		try
		{
			await ReloadAsync();
		}
		finally
		{
			_initialLoad.TrySetResult();
		}
	}

	protected override void OnNavigatedFrom(NavigationEventArgs e)
	{
		_load?.Cancel();
	}

	public async Task ReloadAsync()
	{
		_load?.Cancel();
		_load = new CancellationTokenSource();
		CancellationToken ct = _load.Token;
		Recent.Clear();
		Featured.Clear();
		Quick.Clear();
		Albums.Clear();
		LoadMessage.Text = "";
		LoadMessage.Visibility = Visibility.Collapsed;
		if (_shell.IsDemo)
		{
			LoadReferenceDemo();
			return;
		}
		foreach (Track item in _history.Items.Take(6))
		{
			Recent.Add(new HomeCard(item.Id, item.Title, item.Artist, "Ouvido neste computador", item.ArtworkUrl, item));
		}
		EmptyHistory.Visibility = ((Recent.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		Busy.IsActive = true;
		Busy.Visibility = Visibility.Visible;
		try
		{
			ILibraryProvider library = App.GetService<ILibraryProvider>();
			IReadOnlyList<PlaylistSummary> readOnlyList = ((!(library is IMusicLibraryProvider musicLibraryProvider)) ? (await library.GetPlaylistsAsync(null, ct)).Items : (await musicLibraryProvider.GetHomeAsync(null, ct)).Items);
			IReadOnlyList<PlaylistSummary> source = readOnlyList;
			foreach (PlaylistSummary item2 in source.Where((PlaylistSummary x) => !x.Id.StartsWith("MP")).Take(12))
			{
				AddMix(new HomeCard(item2.Id, item2.Title, "YouTube Music", "Mix e playlist", item2.ArtworkUrl));
			}
			PlaylistSummary[] array = source.Where((PlaylistSummary x) => x.Id.StartsWith("MP")).Take(8).ToArray();
			if (array.Length == 0)
			{
				array = (await library.SearchAsync("álbuns populares", SearchKind.Albums, ct)).Playlists.Take(8).ToArray();
			}
			PlaylistSummary[] array2 = array;
			foreach (PlaylistSummary playlistSummary in array2)
			{
				Albums.Add(new HomeCard(playlistSummary.Id, playlistSummary.Title, "YouTube Music", "Álbum", playlistSummary.ArtworkUrl));
			}
			if (Featured.Count == 0 && Albums.Count == 0)
			{
				LoadMessage.Text = "Nenhuma recomendação disponível agora. Explore a busca para escolher uma música.";
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2) when (!(ex2 is OutOfMemoryException))
		{
			if (!ct.IsCancellationRequested)
			{
				LoadMessage.Text = "Não foi possível carregar as recomendações. Tente novamente pela busca.";
			}
		}
		finally
		{
			if (!ct.IsCancellationRequested)
			{
				Busy.IsActive = false;
				Busy.Visibility = Visibility.Collapsed;
				LoadMessage.Visibility = ((LoadMessage.Text.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	private void AddMix(HomeCard card)
	{
		(Featured.Count < 3 ? Featured : Quick).Add(card);
	}

	private static string Art(int n)
	{
		return $"ms-appx:///Assets/Design/ref-{n:00}.png";
	}

	private void LoadReferenceDemo()
	{
		string[] array = new string[6] { "Midnight Horizon", "Lo-Fi Beats for Coding", "Echoes in the Rain", "Deep Focus Alpha Waves", "Synthwave Odyssey", "Acoustic Sunset" };
		string[] array2 = new string[6] { "Solaris Drift", "Cyber Chill", "Aethelgard", "NeuroSound", "Neon Retrowave", "Clara Mendes" };
		for (int i = 0; i < 6; i++)
		{
			Track track = new Track($"demo-ref-{i}", array[i], array2[i], null, TimeSpan.FromSeconds(225.0), Art(i + 1), IsDemo: true);
			Recent.Add(new HomeCard(track.Id, track.Title, track.Artist, "Demonstração", track.ArtworkUrl, track));
		}
		string[] array3 = new string[5] { "Meu Supermix", "Mix de Descobertas", "Mix de Energia", "Mix Acústico", "Nostalgia 2010s" };
		string[] array4 = new string[5] { "Solaris Drift, Cyber Chill, Clara Mendes e mais", "Faixas inéditas para descobrir", "Synthwave, Electro e Basslines intensos", "Sessões desplugadas, folk e timbres orgânicos", "Os maiores sucessos que marcaram uma década" };
		for (int j = 0; j < 5; j++)
		{
			AddMix(new HomeCard($"demo-mix-{j}", array3[j], array4[j], "Demonstração", Art(j + 7)));
		}
		string[] array5 = new string[4] { "Chroma Dreams", "Atmospheres & Glass", "Urban Solitude", "Veloce" };
		string[] array6 = new string[4] { "Kaelen Brooks", "The Ambient Collective", "Sarah Jenkins", "Hyperdrive" };
		for (int k = 0; k < 4; k++)
		{
			Albums.Add(new HomeCard($"demo-album-{k}", array5[k], array6[k], "Álbum de demonstração", Art(k + 12)));
		}
	}

	private async void OnMixPlayClick(object sender, RoutedEventArgs e)
	{
		if (!(UiHelpers.ItemOf(sender) is HomeCard tag))
		{
			return;
		}
		if (_shell.IsDemo)
		{
			_shell.StatusMessage = "Este mix faz parte da demonstração visual e não contém áudio.";
			return;
		}
		try
		{
			IReadOnlyList<Track> items = (await App.GetService<ILibraryProvider>().GetPlaylistTracksAsync(tag.Id, null, CancellationToken.None)).Items;
			if (items.Count == 0)
			{
				_shell.StatusMessage = "Este mix não tem faixas disponíveis.";
				return;
			}
			await App.GetService<PlaybackCoordinator>().PlayQueueAsync(items, 0);
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
			_shell.StatusMessage = "Não foi possível tocar este mix. Abra o mix e escolha uma faixa.";
		}
	}

	private async void OnCardClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is HomeCard dataContext)
		{
			if ((object)dataContext.Track != null)
			{
				await PlayRecentAsync(dataContext);
			}
			else if (_shell.IsDemo)
			{
				base.Frame.Navigate(typeof(LibraryPage));
			}
			else
			{
				base.Frame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Playlist, dataContext.Id, dataContext.Title));
			}
		}
	}

	private async void OnPlayRecentClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is HomeCard dataContext)
		{
			await PlayRecentAsync(dataContext);
		}
	}

	private void OnRecentMoreClick(object sender, RoutedEventArgs e)
	{
		FrameworkElement button = sender as FrameworkElement;
		if ((object)button == null || !(UiHelpers.ItemOf(button) is HomeCard homeCard))
		{
			return;
		}
		Track track = homeCard.Track;
		if ((object)track != null)
		{
			MenuFlyout menuFlyout = new MenuFlyout();
			MenuFlyoutItem menuFlyoutItem = new MenuFlyoutItem
			{
				Text = "Tocar agora",
				Icon = new FontIcon
				{
					Glyph = "\ue768"
				}
			};
			menuFlyoutItem.Click += async delegate
			{
				await PlayRecentAsync(homeCard);
			};
			menuFlyout.Items.Add(menuFlyoutItem);
			MenuFlyoutItem menuFlyoutItem2 = new MenuFlyoutItem
			{
				Text = "Adicionar à fila",
				Icon = new FontIcon
				{
					Glyph = "\ue710"
				},
				IsEnabled = !track.IsDemo
			};
			menuFlyoutItem2.Click += delegate
			{
				App.GetService<PlaybackCoordinator>().Enqueue(track);
			};
			menuFlyout.Items.Add(menuFlyoutItem2);
			MenuFlyoutItem menuFlyoutItem3 = new MenuFlyoutItem
			{
				Text = "Abrir artista",
				Icon = new FontIcon
				{
					Glyph = "\ue8d4"
				}
			};
			menuFlyoutItem3.Click += delegate
			{
				((App)Application.Current).OpenSearch(track.Artist);
			};
			menuFlyout.Items.Add(menuFlyoutItem3);
			menuFlyout.ShowAt(button);
		}
	}

	private async Task PlayRecentAsync(HomeCard card)
	{
		Track track = card.Track;
		if ((object)track == null)
		{
			return;
		}
		if (track.IsDemo)
		{
			_shell.StatusMessage = "Esta é uma faixa de demonstração visual e não contém áudio. Conecte o catálogo para reproduzir músicas.";
			return;
		}
		Track[] array = (from item in Recent.Where(delegate(HomeCard item)
			{
				Track track2 = item.Track;
				return (object)track2 != null && !track2.IsDemo;
			})
			select item.Track).ToArray();
		int num = Array.IndexOf(array, track);
		if (num < 0)
		{
			array = new Track[1] { track };
			num = 0;
		}
		try
		{
			await App.GetService<PlaybackCoordinator>().PlayQueueAsync(array, num);
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
			_shell.StatusMessage = "Não foi possível iniciar esta faixa. Tente novamente ou escolha outra música.";
		}
	}

	private void OnMoodClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string { Length: >0 } tag })
		{
			base.Frame.Navigate(typeof(SearchPage), tag);
		}
	}

	private void OnAllRecent(object sender, RoutedEventArgs e)
	{
		if (_shell.IsDemo)
		{
			return;
		}
		Recent.Clear();
		foreach (Track item in _history.Items)
		{
			Recent.Add(new HomeCard(item.Id, item.Title, item.Artist, "Ouvido neste computador", item.ArtworkUrl, item));
		}
	}

	private void OnAllMixes(object sender, RoutedEventArgs e)
	{
		base.Frame.Navigate(typeof(LibraryPage), "recommendations");
	}






}
