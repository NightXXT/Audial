using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.UI;
using YtMusic.App.Platform;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;
using YtMusic.Presentation.ViewModels;
using YtMusic.Services.Settings;

namespace YtMusic.App.Views;

public sealed partial class ArtistPage : Page
{
	private readonly PlayerViewModel _player = App.GetService<PlayerViewModel>();

	private readonly ListeningHistory _history = App.GetService<ListeningHistory>();

	private readonly ShellViewModel _shell = App.GetService<ShellViewModel>();

	private readonly StackPanel _root = new StackPanel
	{
		Spacing = 18.0,
		Padding = new Thickness(24.0, 18.0, 24.0, 40.0)
	};

	private readonly StackPanel _tracks = new StackPanel
	{
		Spacing = 4.0
	};

	private readonly StackPanel _albums = new StackPanel
	{
		Orientation = Orientation.Horizontal,
		Spacing = 12.0
	};

	private readonly TextBlock _artist = Label("Explore artistas", 40.0);

	private readonly TextBlock _trackCount = Label("Seu histórico recente", 12.0, "Brush.TextSecondary");

	private readonly Image _avatar = new Image
	{
		Stretch = Stretch.UniformToFill
	};

	private readonly ILibraryProvider _library = App.GetService<ILibraryProvider>();

	private IReadOnlyList<Track> _artistTracks = Array.Empty<Track>();

	private ChannelSummary? _channel;

	public ArtistPage()
	{
		InitializeComponent();
		base.Content = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			Content = _root
		};
		Build();
		base.Unloaded += delegate
		{
			_player.PropertyChanged -= OnPlayerChanged;
		};
	}

	protected override async void OnNavigatedTo(NavigationEventArgs e)
	{
		base.OnNavigatedTo(e);
		_channel = e.Parameter as ChannelSummary;
		if ((object)_channel == null)
		{
			_player.PropertyChanged += OnPlayerChanged;
		}
		await RefreshAsync();
	}

	private async void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "Artist")
		{
			await RefreshAsync();
		}
	}

	private void Build()
	{
		Border border = new Border
		{
			CornerRadius = new CornerRadius(20.0),
			Padding = new Thickness(30.0, 26.0, 30.0, 26.0),
			MinHeight = 320.0,
			BorderThickness = new Thickness(1.0),
			BorderBrush = Brush("Brush.Border")
		};
		border.Background = new LinearGradientBrush
		{
			StartPoint = new Point(0f, 0f),
			EndPoint = new Point(1f, 1f),
			GradientStops = 
			{
				new GradientStop
				{
					Color = Color.FromArgb(byte.MaxValue, 68, 25, 55),
					Offset = 0.0
				},
				new GradientStop
				{
					Color = Color.FromArgb(byte.MaxValue, 32, 27, 38),
					Offset = 0.55
				},
				new GradientStop
				{
					Color = Color.FromArgb(byte.MaxValue, 24, 27, 34),
					Offset = 1.0
				}
			}
		};
		Grid grid = new Grid
		{
			ColumnSpacing = 28.0,
			VerticalAlignment = VerticalAlignment.Bottom
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(180.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(new Grid
		{
			Width = 180.0,
			Height = 180.0,
			CornerRadius = new CornerRadius(90.0),
			Background = Brush("Brush.SurfaceRaised"),
			VerticalAlignment = VerticalAlignment.Bottom,
			Children =
			{
				(UIElement)new FontIcon
				{
					Glyph = "",
					FontSize = 56.0,
					Foreground = Brush("Brush.Rose")
				},
				(UIElement)_avatar
			}
		});
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 12.0,
			VerticalAlignment = VerticalAlignment.Bottom
		};
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel2.Children.Add(Label("✦  PERFIL DO ARTISTA", 12.0, "Brush.Rose"));
		stackPanel2.Children.Add(Label("•  Catálogo e histórico local", 12.0, "Brush.TextSecondary"));
		stackPanel.Children.Add(stackPanel2);
		stackPanel.Children.Add(_artist);
		stackPanel.Children.Add(Label("Músicas e álbuns disponíveis no catálogo conectado", 14.0, "Brush.TextSecondary"));
		stackPanel.Children.Add(_trackCount);
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 10.0,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		Button button = Button("▶  Reproduzir", "Button.Accent");
		button.Click += async delegate
		{
			await PlayArtistAsync(shuffle: false);
		};
		stackPanel3.Children.Add(button);
		Button button2 = Button("⤨  Embaralhar", "Button.Glass");
		button2.Click += async delegate
		{
			await PlayArtistAsync(shuffle: true);
		};
		stackPanel3.Children.Add(button2);
		Button button3 = Button("Ouvir junto", "Button.Glass");
		button3.Click += delegate
		{
			((App)Application.Current).NavigateTo("together");
		};
		stackPanel3.Children.Add(button3);
		Button button4 = Button("Buscar artista", "Button.Glass");
		button4.Click += delegate
		{
			((App)Application.Current).OpenSearch(_artist.Text);
		};
		stackPanel3.Children.Add(button4);
		stackPanel.Children.Add(stackPanel3);
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		border.Child = grid;
		_root.Children.Add(border);
		Grid grid2 = new Grid
		{
			ColumnSpacing = 18.0
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(2.0, GridUnitType.Star)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		StackPanel stackPanel4 = new StackPanel
		{
			Spacing = 10.0
		};
		Grid grid3 = new Grid();
		grid3.Children.Add(Label("Músicas populares", 22.0));
		grid3.Children.Add(Label("DO SEU CATÁLOGO", 11.0, "Brush.Rose"));
		if (grid3.Children.Count > 1 && grid3.Children[1] is FrameworkElement frameworkElement)
		{
			frameworkElement.HorizontalAlignment = HorizontalAlignment.Right;
		}
		stackPanel4.Children.Add(grid3);
		stackPanel4.Children.Add(_tracks);
		grid2.Children.Add(stackPanel4);
		StackPanel stackPanel5 = new StackPanel
		{
			Spacing = 12.0
		};
		Border border3 = Card();
		StackPanel stackPanel6 = new StackPanel
		{
			Spacing = 12.0
		};
		stackPanel6.Children.Add(Label("●  OUVIR JUNTO", 12.0, "Brush.Rose"));
		stackPanel6.Children.Add(Label("Compartilhe a sessão", 20.0));
		stackPanel6.Children.Add(Label("Convide alguém para acompanhar a música com você.", 13.0, "Brush.TextSecondary"));
		Button button5 = Button("♬  Abrir Ouvir junto", "Button.Accent");
		button5.Click += delegate
		{
			((App)Application.Current).NavigateTo("together");
		};
		stackPanel6.Children.Add(button5);
		border3.Child = stackPanel6;
		stackPanel5.Children.Add(border3);
		Border border4 = Card();
		border4.Child = new StackPanel
		{
			Spacing = 10.0,
			Children = 
			{
				(UIElement)Label("Sobre este artista", 18.0),
				(UIElement)Label("Os dados exibidos vêm das faixas disponíveis no seu catálogo e no histórico deste computador.", 13.0, "Brush.TextSecondary"),
				(UIElement)Label("Para ver mais músicas, use a busca do YouTube Music.", 13.0, "Brush.TextSecondary")
			}
		};
		stackPanel5.Children.Add(border4);
		Grid.SetColumn(stackPanel5, 1);
		grid2.Children.Add(stackPanel5);
		_root.Children.Add(grid2);
		_root.Children.Add(new StackPanel
		{
			Spacing = 12.0,
			Children =
			{
				(UIElement)Label("Discografia", 22.0),
				(UIElement)new ScrollViewer
				{
					HorizontalScrollMode = ScrollMode.Auto,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
					VerticalScrollMode = ScrollMode.Disabled,
					VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
					Content = _albums
				}
			}
		});
	}

	private async Task RefreshAsync()
	{
		if (_shell.IsDemo && (object)_channel == null)
		{
			_artist.Text = "Solaris Drift";
			_avatar.Source = UiHelpers.Image(Art(1));
			_artistTracks = new Track[5]
			{
				new Track("demo-artist-1", "Midnight Horizon", "Solaris Drift", "Lumina", TimeSpan.FromMinutes(3.75), Art(1), IsDemo: true),
				new Track("demo-artist-2", "Echoes of Tomorrow", "Solaris Drift", "Echoes", TimeSpan.FromMinutes(4.2), Art(2), IsDemo: true),
				new Track("demo-artist-3", "Neon Drizzle", "Solaris Drift", "Neon Drizzle EP", TimeSpan.FromMinutes(3.3), Art(3), IsDemo: true),
				new Track("demo-artist-4", "Chroma Dreams", "Solaris Drift", "Chroma Dreams", TimeSpan.FromMinutes(5.0), Art(4), IsDemo: true),
				new Track("demo-artist-5", "Hyperdrive Pulsar", "Solaris Drift", "Hyperdrive", TimeSpan.FromMinutes(3.9), Art(5), IsDemo: true)
			};
			_trackCount.Text = "Músicas populares · demonstração visual";
			_tracks.Children.Clear();
			for (int i = 0; i < _artistTracks.Count; i++)
			{
				AddTrack(_artistTracks[i], i + 1);
			}
			_albums.Children.Clear();
			AddAlbumTile("Chroma Dreams", Art(12), "demo-album-1");
			AddAlbumTile("Atmospheres & Glass", Art(13), "demo-album-2");
			AddAlbumTile("Urban Solitude", Art(14), "demo-album-3");
			AddAlbumTile("Veloce", Art(15), "demo-album-4");
			return;
		}
		string artist = _channel?.Title.Replace(" - Topic", "") ?? _player.Artist;
		if (string.IsNullOrWhiteSpace(artist))
		{
			artist = _history.Items.FirstOrDefault()?.Artist ?? "Explore artistas";
		}
		_artist.Text = artist;
		_avatar.Source = UiHelpers.Image(_channel?.ArtworkUrl ?? _player.ArtworkUrl);
		_tracks.Children.Clear();
		_albums.Children.Clear();
		List<Track> matches = _history.Items.Where((Track t) => string.Equals(t.Artist, artist, StringComparison.OrdinalIgnoreCase)).DistinctBy((Track t) => t.Id).ToList();
		Track current = _player.CurrentTrack;
		if ((object)current != null && string.Equals(current.Artist, artist, StringComparison.OrdinalIgnoreCase) && matches.All((Track t) => t.Id != current.Id))
		{
			matches.Insert(0, current);
		}
		try
		{
			foreach (Track track in (await _library.SearchAsync(artist, SearchKind.Tracks, CancellationToken.None)).Tracks.Where((Track t) => t.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase)))
			{
				if (matches.All((Track existing) => existing.Id != track.Id))
				{
					matches.Add(track);
				}
			}
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
		_artistTracks = matches.Take(20).ToArray();
		_trackCount.Text = ((_artistTracks.Count == 0) ? "Nenhuma faixa encontrada no histórico ou catálogo conectado" : $"{_artistTracks.Count} faixas disponíveis para ouvir");
		if (_artistTracks.Count == 0)
		{
			_tracks.Children.Add(Label("Nenhuma faixa encontrada. Use a busca para explorar este artista.", 13.0, "Brush.TextSecondary"));
		}
		else
		{
			for (int j = 0; j < _artistTracks.Count; j++)
			{
				AddTrack(_artistTracks[j], j + 1);
			}
		}
		try
		{
			foreach (PlaylistSummary item in (await _library.SearchAsync(artist, SearchKind.Albums, CancellationToken.None)).Playlists.Take(5))
			{
				AddAlbumTile(item.Title, item.ArtworkUrl, item.Id);
			}
		}
		catch (Exception ex2) when (!(ex2 is OutOfMemoryException))
		{
		}
		if (_albums.Children.Count == 0)
		{
			_albums.Children.Add(Label("Os álbuns aparecerão quando estiverem disponíveis no catálogo.", 13.0, "Brush.TextSecondary"));
		}
	}

	private void AddAlbumTile(string title, string? artwork, string id)
	{
		string id2 = id;
		string title2 = title;
		Button button = new Button
		{
			Width = 138.0,
			Padding = new Thickness(8.0),
			Background = Brush("Brush.SurfaceRaised"),
			BorderThickness = new Thickness(0.0),
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			Tag = id2
		};
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 6.0
		};
		stackPanel.Children.Add(new Border
		{
			Width = 120.0,
			Height = 120.0,
			CornerRadius = new CornerRadius(8.0),
			Background = Brush("Brush.Surface"),
			Child = new Image
			{
				Source = UiHelpers.Image(artwork),
				Stretch = Stretch.UniformToFill
			}
		});
		stackPanel.Children.Add(Label(title2, 13.0));
		button.Content = stackPanel;
		button.Click += delegate
		{
			if (id2.StartsWith("demo-", StringComparison.Ordinal))
			{
				_shell.StatusMessage = "Este álbum faz parte da demonstração visual e não tem áudio.";
			}
			else
			{
				base.Frame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Playlist, id2, title2));
			}
		};
		_albums.Children.Add(button);
	}

	private static string Art(int number)
	{
		return $"ms-appx:///Assets/Design/ref-{number:00}.png";
	}

	private void AddTrack(Track track, int number)
	{
		Track track2 = track;
		Button button = new Button
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			Padding = new Thickness(10.0, 8.0, 12.0, 8.0),
			Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
			BorderThickness = new Thickness(0.0),
			CornerRadius = new CornerRadius(10.0),
			Tag = track2
		};
		Grid grid = new Grid
		{
			ColumnSpacing = 12.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(30.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(52.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock textBlock = Label(number.ToString(), 13.0, "Brush.TextSecondary");
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(textBlock);
		Image image = new Image
		{
			Width = 48.0,
			Height = 48.0,
			Stretch = Stretch.UniformToFill,
			Source = UiHelpers.Image(track2.ArtworkUrl)
		};
		Grid.SetColumn(image, 1);
		grid.Children.Add(image);
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 3.0
		};
		stackPanel.Children.Add(Label(track2.Title));
		stackPanel.Children.Add(Label(track2.Artist, 12.0, "Brush.TextSecondary"));
		Grid.SetColumn(stackPanel, 2);
		grid.Children.Add(stackPanel);
		FontIcon fontIcon = new FontIcon
		{
			Glyph = "\ue768",
			Foreground = Brush("Brush.Rose"),
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(fontIcon, 3);
		grid.Children.Add(fontIcon);
		button.Content = grid;
		button.Click += async delegate
		{
			if (track2.IsDemo)
			{
				_shell.StatusMessage = "Esta música faz parte da demonstração visual e não contém áudio.";
			}
			else
			{
				await App.GetService<PlaybackCoordinator>().PlayQueueAsync(_artistTracks, Array.IndexOf(_artistTracks.ToArray(), track2));
			}
		};
		_tracks.Children.Add(button);
	}

	private async Task PlayArtistAsync(bool shuffle)
	{
		if (_artistTracks.Count == 0)
		{
			((App)Application.Current).OpenSearch(_artist.Text);
			return;
		}
		if (_artistTracks.Any((Track track) => track.IsDemo))
		{
			_shell.StatusMessage = "Este perfil faz parte da demonstração visual e não contém áudio.";
			return;
		}
		List<Track> list = _artistTracks.ToList();
		if (shuffle)
		{
			list = list.OrderBy((Track _) => Random.Shared.Next()).ToList();
		}
		await App.GetService<PlaybackCoordinator>().PlayQueueAsync(list, 0);
	}

	private static Border Card()
	{
		return new Border
		{
			CornerRadius = new CornerRadius(16.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = Brush("Brush.Border"),
			Background = Brush("Brush.Surface"),
			Padding = new Thickness(18.0)
		};
	}

	internal static Brush Brush(string key)
	{
		return (Brush)Application.Current.Resources[key];
	}

	internal static TextBlock Label(string text, double size = 14.0, string color = "Brush.TextPrimary")
	{
		return new TextBlock
		{
			Text = text,
			FontSize = size,
			Foreground = Brush(color),
			TextWrapping = TextWrapping.Wrap
		};
	}

	internal static Button Button(string text, string style)
	{
		return new Button
		{
			Content = text,
			Style = (Style)Application.Current.Resources[style]
		};
	}
}
