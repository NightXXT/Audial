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
using Microsoft.UI.Text;
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
public sealed partial class TrackListPage : Page, IReloadablePage
{










	private TrackListRequest _request = new TrackListRequest(TrackListKind.Liked, null, "Músicas curtidas");

	private StackPanel? _queueTracks;

	private TextBlock? _queueCurrent;





	public TrackListViewModel ViewModel { get; }

	public TrackListPage()
	{
		ViewModel = App.GetService<TrackListViewModel>();
		InitializeComponent();
		ViewModel.Tracks.CollectionChanged += delegate
		{
			UpdateHero();
		};
	}

	private void UpdateHero()
	{
		int count = ViewModel.Tracks.Count;
		TrackCount.Text = (count == 1) ? "1 faixa" : $"{count} faixas";
		if (count > 0)
		{
			HeroImage.Source = UiHelpers.Image(ViewModel.Tracks[0].ArtworkUrl);
		}
	}

	private void AddQueueDrawer()
	{
		QueueViewModel queue;
		TextBlock count;
		if (base.Content is Grid grid)
		{
			queue = App.GetService<QueueViewModel>();
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(320.0)
			});
			FrameworkElement[] array = grid.Children.OfType<FrameworkElement>().ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				Grid.SetColumn(array[i], 0);
			}
			StackPanel stackPanel = new StackPanel
			{
				Spacing = 12.0
			};
			Grid grid2 = new Grid();
			grid2.Children.Add(new TextBlock
			{
				Text = "Fila de reprodução",
				FontSize = 18.0,
				FontWeight = FontWeights.SemiBold
			});
			count = new TextBlock
			{
				HorizontalAlignment = HorizontalAlignment.Right,
				Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
				FontSize = 11.0
			};
			grid2.Children.Add(count);
			stackPanel.Children.Add(grid2);
			_queueCurrent = new TextBlock
			{
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0,
				Foreground = (Brush)Application.Current.Resources["Brush.Rose"]
			};
			stackPanel.Children.Add(_queueCurrent);
			_queueTracks = new StackPanel
			{
				Spacing = 6.0
			};
			ScrollViewer item = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				MaxHeight = 500.0,
				Content = _queueTracks
			};
			stackPanel.Children.Add(item);
			StackPanel stackPanel2 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0
			};
			Button button = new Button
			{
				Content = "Abrir fila",
				Style = (Style)Application.Current.Resources["Button.Glass"],
				HorizontalAlignment = HorizontalAlignment.Stretch
			};
			button.Click += delegate
			{
				((App)Application.Current).NavigateTo("queue");
			};
			Button button2 = new Button
			{
				Content = "Limpar",
				Style = (Style)Application.Current.Resources["Button.Glass"]
			};
			button2.Click += async delegate
			{
				await queue.ClearCommand.ExecuteAsync(null);
			};
			stackPanel2.Children.Add(button);
			stackPanel2.Children.Add(button2);
			stackPanel.Children.Add(stackPanel2);
			Border border = new Border
			{
				Background = (Brush)Application.Current.Resources["Brush.Surface"],
				BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(14.0),
				Padding = new Thickness(14.0),
				Child = stackPanel
			};
			Grid.SetColumn(border, 1);
			Grid.SetRow(border, 0);
			Grid.SetRowSpan(border, grid.RowDefinitions.Count);
			grid.Children.Add(border);
			queue.Items.CollectionChanged += delegate
			{
				RefreshQueue();
			};
			queue.PropertyChanged += delegate
			{
				RefreshQueue();
			};
			RefreshQueue();
		}
		void RefreshQueue()
		{
			_queueTracks.Children.Clear();
			count.Text = $"{queue.Items.Count} faixas";
			_queueCurrent.Text = (string.IsNullOrWhiteSpace(queue.CurrentTitle) ? "Nenhuma faixa tocando" : ("Tocando agora · " + queue.CurrentTitle));
			foreach (Track track in queue.Items.Take(12))
			{
				Button button3 = new Button
				{
					Background = (Brush)Application.Current.Resources["Brush.Subtle"],
					BorderThickness = new Thickness(0.0),
					Padding = new Thickness(7.0),
					CornerRadius = new CornerRadius(9.0),
					HorizontalContentAlignment = HorizontalAlignment.Stretch
				};
				Grid grid3 = new Grid
				{
					ColumnSpacing = 9.0
				};
				grid3.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(42.0)
				});
				grid3.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				});
				grid3.Children.Add(new Image
				{
					Width = 42.0,
					Height = 42.0,
					Stretch = Stretch.UniformToFill,
					Source = UiHelpers.Image(track.ArtworkUrl)
				});
				StackPanel stackPanel3 = new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Spacing = 3.0
				};
				stackPanel3.Children.Add(new TextBlock
				{
					Text = track.Title,
					FontSize = 12.0,
					TextTrimming = TextTrimming.CharacterEllipsis
				});
				stackPanel3.Children.Add(new TextBlock
				{
					Text = track.Artist,
					FontSize = 10.0,
					Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
					TextTrimming = TextTrimming.CharacterEllipsis
				});
				Grid.SetColumn(stackPanel3, 1);
				grid3.Children.Add(stackPanel3);
				button3.Content = grid3;
				button3.Click += async delegate
				{
					await queue.PlayIndexCommand.ExecuteAsync(track);
				};
				_queueTracks.Children.Add(button3);
			}
		}
	}

	public Task ReloadAsync()
	{
		return ViewModel.LoadAsync(_request);
	}

	protected override async void OnNavigatedTo(NavigationEventArgs e)
	{
		if (e.Parameter is TrackListRequest request)
		{
			_request = request;
		}
		await ViewModel.LoadAsync(_request);
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






}
