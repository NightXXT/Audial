using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT;
using Windows.System;
using YtMusic.App.Controls;
using YtMusic.App.Platform;
using YtMusic.Core.Models;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;
public sealed partial class SearchPage : Page, IReloadablePage
{






















	public SearchViewModel ViewModel { get; }

	public SearchPage()
	{
		ViewModel = App.GetService<SearchViewModel>();
		InitializeComponent();
		UpdatePills("All");
	}

	public Task ReloadAsync()
	{
		return ViewModel.ReloadAsync();
	}

	protected override async void OnNavigatedTo(NavigationEventArgs e)
	{
		base.OnNavigatedTo(e);
		if (e.Parameter is string text && !string.IsNullOrWhiteSpace(text))
		{
			ViewModel.Query = text;
			await ViewModel.SearchCommand.ExecuteAsync(null);
		}
	}

	private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Enter)
		{
			ViewModel.SearchCommand.Execute(null);
		}
	}

	private void OnPillClick(object sender, RoutedEventArgs e)
	{
		if (!(sender is Button { Tag: string tag }))
		{
			return;
		}
		if (tag == "All")
		{
			ViewModel.SelectAll();
		}
		else if (Enum.TryParse<SearchKind>(tag, out var result))
		{
			ViewModel.SelectKind(result);
		}
		UpdatePills(tag);
	}

	private void UpdatePills(string tag)
	{
		foreach (Button item in Pills.Children.OfType<Button>())
		{
			bool flag = item.Tag as string == tag;
			item.Background = (Brush)Application.Current.Resources[flag ? "Brush.Accent" : "Brush.SurfaceRaised"];
			item.Foreground = (Brush)Application.Current.Resources[flag ? "Brush.AccentText" : "Brush.TextSecondary"];
		}
	}

	private void OnBestClick(object sender, RoutedEventArgs e)
	{
		PlaylistSummary best = ViewModel.Best;
		if ((object)best != null)
		{
			OpenPlaylist(best);
		}
	}

	private void OnPlaylistCardClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is PlaylistSummary dataContext)
		{
			OpenPlaylist(dataContext);
		}
	}

	private void OnTopTrackClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is Track dataContext)
		{
			ViewModel.PlayTrackCommand.Execute(dataContext);
		}
	}

	private void OpenPlaylist(PlaylistSummary playlist)
	{
		base.Frame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Playlist, playlist.Id, playlist.Title));
	}

	private void OnChannelCardClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is ChannelSummary dataContext)
		{
			base.Frame.Navigate(typeof(ArtistPage), dataContext);
		}
	}

	private void OnChannelClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is ChannelSummary parameter)
		{
			base.Frame.Navigate(typeof(ArtistPage), parameter);
		}
	}

	private void OnTrackClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is Track parameter)
		{
			ViewModel.PlayTrackCommand.Execute(parameter);
		}
	}

	private void OnEnqueueClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is Track dataContext)
		{
			ViewModel.EnqueueCommand.Execute(dataContext);
		}
	}

	private void OnPlaylistClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is PlaylistSummary playlistSummary)
		{
			base.Frame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Playlist, playlistSummary.Id, playlistSummary.Title));
		}
	}






}
