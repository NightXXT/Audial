using System;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using YtMusic.App.Platform;
using YtMusic.App.Views;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App;

public sealed class MiniPlayerWindow : Window
{
	private static readonly Color WindowColor = Color.FromArgb(byte.MaxValue, 17, 19, 25);

	private readonly Slider _seek = new Slider
	{
		Minimum = 0.0,
		IsThumbToolTipEnabled = false,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly Image _image = new Image
	{
		Stretch = Stretch.UniformToFill
	};

	private readonly FontIcon _playIcon = new FontIcon
	{
		FontSize = 18.0
	};

	private readonly TextBlock _title = ArtistPage.Label("", 15.0);

	private readonly TextBlock _artist = ArtistPage.Label("", 12.0, "Brush.TextSecondary");

	private readonly TextBlock _elapsed = ArtistPage.Label("", 11.0, "Brush.TextSecondary");

	private readonly TextBlock _duration = ArtistPage.Label("", 11.0, "Brush.TextSecondary");

	private bool _updating;

	public PlayerViewModel ViewModel { get; }

	public MiniPlayerWindow(PlayerViewModel viewModel)
	{
		ViewModel = viewModel;
		base.Title = "Audial Mini";
		_title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
		_title.TextWrapping = TextWrapping.NoWrap;
		_title.TextTrimming = TextTrimming.CharacterEllipsis;
		_artist.TextWrapping = TextWrapping.NoWrap;
		_artist.TextTrimming = TextTrimming.CharacterEllipsis;
		_elapsed.VerticalAlignment = VerticalAlignment.Center;
		_duration.VerticalAlignment = VerticalAlignment.Center;
		Grid grid = new Grid
		{
			ColumnSpacing = 14.0,
			RowSpacing = 6.0,
			Padding = new Thickness(14.0, 12.0, 14.0, 12.0),
			Background = new SolidColorBrush(WindowColor)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(96.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		Grid grid2 = new Grid
		{
			Width = 96.0,
			Height = 96.0,
			CornerRadius = new CornerRadius(10.0),
			Background = (Brush)Application.Current.Resources["Brush.AccentGradient"],
			Children =
			{
				(UIElement)new FontIcon
				{
					Glyph = "",
					FontSize = 28.0,
					Foreground = (Brush)Application.Current.Resources["Brush.AccentText"]
				},
				(UIElement)_image
			}
		};
		grid.Children.Add(grid2);
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 4.0
		};
		stackPanel.Children.Add(_title);
		stackPanel.Children.Add(_artist);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 10.0,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		Button button = IconButton("", "Anterior");
		button.Click += async delegate
		{
			await ViewModel.PreviousCommand.ExecuteAsync(null);
		};
		stackPanel2.Children.Add(button);
		Button button2 = new Button
		{
			Style = (Style)Application.Current.Resources["Button.Play"],
			Background = (Brush)Application.Current.Resources["Brush.PlayGradient"],
			Width = 40.0,
			Height = 40.0,
			CornerRadius = new CornerRadius(20.0),
			Content = _playIcon
		};
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button2, "Reproduzir ou pausar");
		button2.Click += async delegate
		{
			await ViewModel.PlayPauseCommand.ExecuteAsync(null);
		};
		stackPanel2.Children.Add(button2);
		Button button3 = IconButton("", "Próxima");
		button3.Click += async delegate
		{
			await ViewModel.NextCommand.ExecuteAsync(null);
		};
		stackPanel2.Children.Add(button3);
		Button button4 = IconButton("", "Expandir");
		button4.Click += delegate
		{
			((App)Application.Current).ShowMainWindow();
		};
		stackPanel2.Children.Add(button4);
		stackPanel.Children.Add(stackPanel2);
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		Grid grid3 = new Grid
		{
			ColumnSpacing = 8.0
		};
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid3.Children.Add(_elapsed);
		Grid.SetColumn(_seek, 1);
		grid3.Children.Add(_seek);
		Grid.SetColumn(_duration, 2);
		grid3.Children.Add(_duration);
		Grid.SetRow(grid3, 1);
		Grid.SetColumnSpan(grid3, 2);
		grid.Children.Add(grid3);
		_seek.ValueChanged += delegate(object _, RangeBaseValueChangedEventArgs e)
		{
			if (!_updating)
			{
				ViewModel.PositionSeconds = e.NewValue;
			}
		};
		grid.PointerWheelChanged += OnWheel;
		grid.DoubleTapped += delegate
		{
			((App)Application.Current).ShowMainWindow();
		};
		KeyboardAccelerator keyboardAccelerator = new KeyboardAccelerator
		{
			Key = VirtualKey.M,
			Modifiers = VirtualKeyModifiers.Control
		};
		keyboardAccelerator.Invoked += delegate(KeyboardAccelerator _, KeyboardAcceleratorInvokedEventArgs e)
		{
			e.Handled = true;
			Close();
		};
		grid.KeyboardAccelerators.Add(keyboardAccelerator);
		base.Content = grid;
		RefreshTrack();
		RefreshPosition();
		_playIcon.Glyph = UiHelpers.PlayGlyph(ViewModel.IsPlaying);
		ViewModel.PropertyChanged += OnPlayerChanged;
		base.Closed += delegate
		{
			ViewModel.PropertyChanged -= OnPlayerChanged;
		};
		base.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
		AppWindowTitleBar titleBar = base.AppWindow.TitleBar;
		titleBar.BackgroundColor = WindowColor;
		titleBar.InactiveBackgroundColor = WindowColor;
		titleBar.ButtonBackgroundColor = WindowColor;
		titleBar.ButtonInactiveBackgroundColor = WindowColor;
		titleBar.ForegroundColor = Colors.White;
		titleBar.ButtonForegroundColor = Colors.White;
		base.AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
		base.AppWindow.Resize(new SizeInt32(400, 240));
	}

	private static Button IconButton(string glyph, string name)
	{
		Button button = new Button
		{
			Style = (Style)Application.Current.Resources["Button.Icon"],
			Content = new FontIcon
			{
				Glyph = glyph,
				FontSize = 16.0
			}
		};
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
		ToolTipService.SetToolTip(button, name);
		return button;
	}

	private void OnWheel(object sender, PointerRoutedEventArgs e)
	{
		PointerPoint currentPoint = e.GetCurrentPoint(null);
		ViewModel.Volume += ((currentPoint.Properties.MouseWheelDelta > 0) ? 5.0 : (-5.0));
		e.Handled = true;
	}

	private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
		case "PositionSeconds":
		case "DurationSeconds":
			RefreshPosition();
			break;
		case "ArtworkUrl":
		case "Title":
		case "Artist":
		case "HasTrack":
			RefreshTrack();
			break;
		case "IsPlaying":
			_playIcon.Glyph = UiHelpers.PlayGlyph(ViewModel.IsPlaying);
			break;
		}
	}

	private void RefreshTrack()
	{
		_image.Source = UiHelpers.Image(ViewModel.ArtworkUrl);
		_title.Text = ViewModel.Title;
		_artist.Text = ViewModel.Artist;
		_seek.IsEnabled = ViewModel.HasTrack;
	}

	private void RefreshPosition()
	{
		_updating = true;
		_seek.Maximum = Math.Max(1.0, ViewModel.DurationSeconds);
		_seek.Value = Math.Min(ViewModel.PositionSeconds, _seek.Maximum);
		_updating = false;
		_elapsed.Text = ViewModel.ElapsedText;
		_duration.Text = ViewModel.DurationText;
	}
}
