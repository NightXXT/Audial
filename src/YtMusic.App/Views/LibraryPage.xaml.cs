using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT;
using YtMusic.App.Controls;
using YtMusic.App.Platform;
using YtMusic.Core.Models;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;
public sealed partial class LibraryPage : Page, IReloadablePage
{
















	public LibraryViewModel ViewModel { get; }

	public LibraryPage()
	{
		ViewModel = App.GetService<LibraryViewModel>();
		InitializeComponent();
	}

	public Task ReloadAsync()
	{
		return ViewModel.ReloadAsync();
	}

	protected override async void OnNavigatedTo(NavigationEventArgs e)
	{
		LibraryViewModel viewModel = ViewModel;
		viewModel.Collection = (e.Parameter as string) switch
		{
			"albums" => LibraryCollection.Albums, 
			"recommendations" => LibraryCollection.Recommendations, 
			_ => LibraryCollection.Playlists, 
		};
		PageTitle.Text = ((ViewModel.Collection == LibraryCollection.Recommendations) ? "Sugestões do Music" : ViewModel.Title);
		await ViewModel.ReloadAsync();
	}

	private void OnPlaylistClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is PlaylistSummary playlistSummary)
		{
			base.Frame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Playlist, playlistSummary.Id, playlistSummary.Title));
		}
	}






}
