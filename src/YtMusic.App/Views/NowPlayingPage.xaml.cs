using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using YtMusic.App.Platform;
using YtMusic.Core.Models;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;
public sealed partial class NowPlayingPage : Page
{
	private bool _updating;

	public PlayerViewModel ViewModel { get; }

	public QueueViewModel Queue { get; }

	public NowPlayingPage()
	{
		ViewModel = App.GetService<PlayerViewModel>();
		Queue = App.GetService<QueueViewModel>();
		InitializeComponent();
		UpdateTabs("next");
		base.Loaded += delegate
		{
			ViewModel.PropertyChanged += OnPlayerChanged;
			RefreshSeek();
		};
		base.Unloaded += delegate
		{
			ViewModel.PropertyChanged -= OnPlayerChanged;
		};
	}

	private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
	{
		string propertyName = e.PropertyName;
		if ((propertyName == "PositionSeconds" || propertyName == "DurationSeconds") ? true : false)
		{
			RefreshSeek();
		}
	}

	private void RefreshSeek()
	{
		_updating = true;
		Seek.Maximum = Math.Max(1.0, ViewModel.DurationSeconds);
		Seek.Value = Math.Min(ViewModel.PositionSeconds, Seek.Maximum);
		_updating = false;
	}

	private void OnSeekChanged(object sender, RangeBaseValueChangedEventArgs e)
	{
		if (!_updating)
		{
			ViewModel.PositionSeconds = e.NewValue;
		}
	}

	private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
	{
		bool flag = e.NewSize.Width < 960.0;
		RightColumn.Width = (flag ? new GridLength(0.0) : new GridLength(1.0, GridUnitType.Star));
		Grid.SetColumn(SidePanel, (!flag) ? 1 : 0);
		Grid.SetRow(SidePanel, flag ? 1 : 0);
	}

	private void OnTabClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string tag })
		{
			UpdateTabs(tag);
		}
	}

	private void UpdateTabs(string tag)
	{
		NextPanel.Visibility = ((tag == "next") ? Visibility.Visible : Visibility.Collapsed);
		LyricsPanel.Visibility = ((tag == "lyrics") ? Visibility.Visible : Visibility.Collapsed);
		foreach (Button item in Tabs.Children.OfType<Button>())
		{
			bool flag = item.Tag as string == tag;
			item.Background = (Brush)Application.Current.Resources[flag ? "Brush.Accent" : "Brush.SurfaceRaised"];
			item.Foreground = (Brush)Application.Current.Resources[flag ? "Brush.AccentText" : "Brush.TextSecondary"];
		}
	}

	private void OnUpNextClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is Track dataContext)
		{
			Queue.PlayIndexCommand.Execute(dataContext);
		}
	}

	private void OnBack(object sender, RoutedEventArgs e)
	{
		GoBack();
	}

	private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		GoBack();
		args.Handled = true;
	}

	private void GoBack()
	{
		if (base.Frame.CanGoBack)
		{
			base.Frame.GoBack();
		}
		else
		{
			((App)Application.Current).NavigateTo("home");
		}
	}

	private void OnMiniPlayer(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).ShowMiniPlayer();
	}

	private void OnArtist(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).NavigateTo("artist");
	}

	private void OnOpenQueue(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).NavigateTo("queue");
	}
}
