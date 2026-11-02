using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using YtMusic.App.Platform;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;

public sealed partial class ListenTogetherPage : Page
{
	private readonly PlayerViewModel _player = App.GetService<PlayerViewModel>();

	private string? _code;

	private readonly PlaybackCoordinator _playback = App.GetService<PlaybackCoordinator>();

	private readonly IUiDispatcher _ui = App.GetService<IUiDispatcher>();

	private readonly TextBlock _roomName = ArtistPage.Label("Sala de Escuta: ainda não iniciada", 20.0);

	private readonly TextBlock _codeText = ArtistPage.Label("Sem convite ativo", 13.0, "Brush.TextSecondary");

	private readonly TextBlock _status = ArtistPage.Label("Crie uma sala para iniciar uma sessão local.", 13.0, "Brush.TextSecondary");

	private readonly TextBlock _people = ArtistPage.Label("Você • host local", 13.0, "Brush.TextSecondary");

	private readonly StackPanel _queueRows = new StackPanel
	{
		Spacing = 4.0
	};

	private readonly Image _artwork = new Image
	{
		Width = 180.0,
		Height = 180.0,
		Stretch = Stretch.UniformToFill
	};

	private readonly TextBlock _title = ArtistPage.Label("Nada tocando", 23.0);

	private readonly TextBlock _artist = ArtistPage.Label("Escolha uma música para começar", 13.0, "Brush.TextSecondary");

	private readonly TextBlock _reactionStatus = ArtistPage.Label("Reações rápidas ficam nesta sessão local.", 12.0, "Brush.TextSecondary");

	private readonly Button _create;

	private readonly Button _copy;

	private readonly Button _leave;

	public ListenTogetherPage()
	{
		InitializeComponent();
		_create =ArtistPage.Button("Criar sala", "Button.Accent");
		_copy = ArtistPage.Button("Copiar convite", "Button.Glass");
		_leave = ArtistPage.Button("Sair da sala", "Button.Glass");
		_copy.IsEnabled = (_leave.IsEnabled = false);
		_create.Click += OnCreate;
		_copy.Click += OnCopy;
		_leave.Click += OnLeave;
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 16.0,
			Padding = new Thickness(24.0, 16.0, 24.0, 40.0)
		};
		Border border = Card();
		Grid grid = new Grid
		{
			ColumnSpacing = 12.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Spacing = 5.0
		};
		stackPanel2.Children.Add(_roomName);
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 7.0
		};
		stackPanel3.Children.Add(new FontIcon
		{
			Glyph = "\ue701",
			Foreground = (Brush)Application.Current.Resources["Brush.Rose"],
			FontSize = 13.0
		});
		stackPanel3.Children.Add(_status);
		stackPanel2.Children.Add(stackPanel3);
		stackPanel2.Children.Add(_codeText);
		grid.Children.Add(stackPanel2);
		grid.Children.Add(_create);
		Grid.SetColumn(_create, 1);
		StackPanel stackPanel4 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel4.Children.Add(_copy);
		stackPanel4.Children.Add(_leave);
		Grid.SetColumn(stackPanel4, 2);
		grid.Children.Add(stackPanel4);
		border.Child = grid;
		stackPanel.Children.Add(border);
		Grid grid2 = new Grid
		{
			ColumnSpacing = 16.0
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(3.0, GridUnitType.Star)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(2.0, GridUnitType.Star)
		});
		StackPanel stackPanel5 = new StackPanel
		{
			Spacing = 14.0
		};
		Border border2 = Card();
		Grid grid3 = new Grid
		{
			ColumnSpacing = 10.0
		};
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
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
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		Border item = Participant("Você", "Host local", active: true);
		grid3.Children.Add(item);
		TextBlock textBlock = ArtistPage.Label("↔\nLocal", 12.0, "Brush.Rose");
		textBlock.TextAlignment = TextAlignment.Center;
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(textBlock, 1);
		grid3.Children.Add(textBlock);
		Border border3 = Participant("Amigo", "Aguardando convite", active: false);
		Grid.SetColumn(border3, 2);
		grid3.Children.Add(border3);
		Border border4 = Participant("Sincronização", "Disponível quando houver serviço online", active: false);
		Grid.SetColumn(border4, 3);
		grid3.Children.Add(border4);
		border2.Child = new StackPanel
		{
			Spacing = 12.0,
			Children = 
			{
				(UIElement)ArtistPage.Label("Pessoas na sala", 17.0),
				(UIElement)grid3,
				(UIElement)_people
			}
		};
		stackPanel5.Children.Add(border2);
		Border border5 = Card();
		border5.Background = new LinearGradientBrush
		{
			StartPoint = new Point(0f, 0f),
			EndPoint = new Point(1f, 1f),
			GradientStops = 
			{
				new GradientStop
				{
					Color = Color.FromArgb(byte.MaxValue, 58, 31, 50),
					Offset = 0.0
				},
				new GradientStop
				{
					Color = Color.FromArgb(byte.MaxValue, 29, 31, 38),
					Offset = 1.0
				}
			}
		};
		Grid grid4 = new Grid
		{
			ColumnSpacing = 18.0
		};
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(190.0)
		});
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		_artwork.Width = (_artwork.Height = 180.0);
		Border item2 = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Background = ArtistPage.Brush("Brush.SurfaceRaised"),
			Child = _artwork
		};
		grid4.Children.Add(item2);
		StackPanel stackPanel6 = new StackPanel
		{
			Spacing = 10.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel6.Children.Add(ArtistPage.Label("●  OUVINDO NESTE COMPUTADOR", 11.0, "Brush.Rose"));
		stackPanel6.Children.Add(_title);
		stackPanel6.Children.Add(_artist);
		stackPanel6.Children.Add(ArtistPage.Label("O transporte controla a reprodução local.", 12.0, "Brush.TextSecondary"));
		StackPanel stackPanel7 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		Button button = ArtistPage.Button("Anterior", "Button.Glass");
		button.Click += async delegate
		{
			await _player.PreviousCommand.ExecuteAsync(null);
		};
		stackPanel7.Children.Add(button);
		Button button2 = ArtistPage.Button("Reproduzir / pausar", "Button.Accent");
		button2.Click += async delegate
		{
			await _player.PlayPauseCommand.ExecuteAsync(null);
		};
		stackPanel7.Children.Add(button2);
		Button button3 = ArtistPage.Button("Próxima", "Button.Glass");
		button3.Click += async delegate
		{
			await _player.NextCommand.ExecuteAsync(null);
		};
		stackPanel7.Children.Add(button3);
		stackPanel6.Children.Add(stackPanel7);
		Grid.SetColumn(stackPanel6, 1);
		grid4.Children.Add(stackPanel6);
		border5.Child = grid4;
		stackPanel5.Children.Add(border5);
		Border border6 = Card();
		StackPanel stackPanel8 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		string[] array = new string[4] { "\ud83d\udd25", "\ud83d\udc96", "\ud83c\udfa7", "⚡" };
		foreach (string emoji in array)
		{
			Button button4 = ArtistPage.Button(emoji, "Button.Glass");
			button4.Click += delegate
			{
				_reactionStatus.Text = "Você reagiu com " + emoji + ". Reações ainda não são transmitidas a outros participantes.";
			};
			stackPanel8.Children.Add(button4);
		}
		border6.Child = new StackPanel
		{
			Spacing = 10.0,
			Children = 
			{
				(UIElement)ArtistPage.Label("Reações rápidas na sala", 17.0),
				(UIElement)ArtistPage.Label("Envie uma reação local", 12.0, "Brush.TextSecondary"),
				(UIElement)stackPanel8,
				(UIElement)_reactionStatus
			}
		};
		stackPanel5.Children.Add(border6);
		grid2.Children.Add(stackPanel5);
		StackPanel stackPanel9 = new StackPanel
		{
			Spacing = 14.0
		};
		Border border7 = Card();
		border7.Child = new StackPanel
		{
			Spacing = 10.0,
			Children = 
			{
				(UIElement)ArtistPage.Label("FLUXO DE ÁUDIO", 12.0, "Brush.Rose"),
				(UIElement)ArtistPage.Label(_player.PlayerName, 18.0),
				(UIElement)ArtistPage.Label("A reprodução acontece apenas neste computador. O Audial não transmite o áudio para terceiros.", 13.0, "Brush.TextSecondary"),
				(UIElement)ArtistPage.Label("Estado: local", 12.0, "Brush.TextSecondary")
			}
		};
		stackPanel9.Children.Add(border7);
		Border border8 = Card();
		Grid grid5 = new Grid
		{
			Children = { (UIElement)ArtistPage.Label("Fila compartilhada", 18.0) }
		};
		Button button5 = ArtistPage.Button("＋ Adicionar", "Button.Accent");
		button5.HorizontalAlignment = HorizontalAlignment.Right;
		button5.Click += delegate
		{
			((App)Application.Current).OpenSearch("");
		};
		grid5.Children.Add(button5);
		_queueRows.Children.Clear();
		StackPanel child = new StackPanel
		{
			Spacing = 10.0,
			Children = 
			{
				(UIElement)grid5,
				(UIElement)ArtistPage.Label("Fila local do player; os demais participantes ainda não podem alterá-la.", 12.0, "Brush.TextSecondary"),
				(UIElement)_queueRows
			}
		};
		border8.Child = child;
		stackPanel9.Children.Add(border8);
		Border border9 = Card();
		border9.Child = new StackPanel
		{
			Spacing = 8.0,
			Children = 
			{
				(UIElement)ArtistPage.Label("Sessão em desenvolvimento", 16.0),
				(UIElement)ArtistPage.Label("A tela mantém o visual do conceito Stitch. Para convidar alguém e sincronizar músicas pela internet, falta integrar um serviço de salas online.", 12.0, "Brush.TextSecondary")
			}
		};
		stackPanel9.Children.Add(border9);
		Grid.SetColumn(stackPanel9, 1);
		grid2.Children.Add(stackPanel9);
		stackPanel.Children.Add(grid2);
		base.Content = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			Content = stackPanel
		};
		_player.PropertyChanged += delegate
		{
			RefreshNowPlaying();
		};
		_playback.Queue.Changed += delegate
		{
			_ui.Post(RefreshQueue);
		};
		RefreshNowPlaying();
		RefreshQueue();
	}

	private void OnCreate(object? sender, RoutedEventArgs e)
	{
		_code = $"AUD-{Random.Shared.Next(1000, 9999)}";
		_roomName.Text = "Sala de Escuta: sessão local";
		_codeText.Text = "#" + _code + "  •  convite demonstrativo";
		_status.Text = "Sessão aberta neste computador";
		_people.Text = "Você é o host. Nenhum outro participante está conectado.";
		_create.IsEnabled = false;
		Button copy = _copy;
		bool isEnabled = (_leave.IsEnabled = true);
		copy.IsEnabled = isEnabled;
	}

	private async void OnCopy(object? sender, RoutedEventArgs e)
	{
		if (_code != null)
		{
			DataPackage dataPackage = new DataPackage();
			dataPackage.SetText("Audial · sessão local de demonstração: " + _code);
			Clipboard.SetContent(dataPackage);
			await new ContentDialog
			{
				XamlRoot = base.XamlRoot,
				Title = "Convite copiado",
				Content = "Este código representa uma sala local de demonstração. Ainda não permite conectar outro computador ou celular.",
				CloseButtonText = "OK",
				RequestedTheme = ElementTheme.Dark
			}.ShowAsync();
		}
	}

	private void OnLeave(object? sender, RoutedEventArgs e)
	{
		_code = null;
		_roomName.Text = "Sala de Escuta: ainda não iniciada";
		_codeText.Text = "Sem convite ativo";
		_status.Text = "Crie uma sala para iniciar uma sessão local.";
		_people.Text = "Você • host local";
		_create.IsEnabled = true;
		Button copy = _copy;
		bool isEnabled = (_leave.IsEnabled = false);
		copy.IsEnabled = isEnabled;
	}

	private void RefreshNowPlaying()
	{
		_title.Text = _player.Title;
		_artist.Text = _player.Artist;
		_artwork.Source = UiHelpers.Image(_player.ArtworkUrl);
	}

	private void RefreshQueue()
	{
		_queueRows.Children.Clear();
		IReadOnlyList<Track> items = _playback.Queue.Items;
		if (items.Count == 0)
		{
			_queueRows.Children.Add(ArtistPage.Label("A fila está vazia. Adicione faixas pela busca.", 13.0, "Brush.TextSecondary"));
			return;
		}
		for (int i = 0; i < Math.Min(items.Count, 8); i++)
		{
			int songIndex = i;
			Track track = items[i];
			Button button = ArtistPage.Button($"{i + 1}.  {track.Title}   ·   {track.Artist}", "Button.Glass");
			button.HorizontalContentAlignment = HorizontalAlignment.Left;
			button.Click += async delegate
			{
				await _playback.PlayIndexAsync(songIndex);
			};
			_queueRows.Children.Add(button);
		}
	}

	private static Border Participant(string name, string detail, bool active)
	{
		Border item = new Border
		{
			Width = 38.0,
			Height = 38.0,
			CornerRadius = new CornerRadius(19.0),
			Background = (active ? ArtistPage.Brush("Brush.Accent") : ArtistPage.Brush("Brush.SurfaceRaised")),
			Child = new TextBlock
			{
				Text = name.Substring(0, 1),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				FontWeight = FontWeights.SemiBold
			}
		};
		return CardWith(new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 9.0,
			Children = 
			{
				(UIElement)item,
				(UIElement)new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Spacing = 3.0,
					Children = 
					{
						(UIElement)ArtistPage.Label(name, 13.0),
						(UIElement)ArtistPage.Label(detail, 10.0, "Brush.TextSecondary")
					}
				}
			}
		});
	}

	private static Border CardWith(UIElement content)
	{
		Border border = Card();
		border.Padding = new Thickness(12.0);
		border.Child = content;
		return border;
	}

	private static Border Card()
	{
		return new Border
		{
			CornerRadius = new CornerRadius(16.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
			Background = (Brush)Application.Current.Resources["Brush.Surface"],
			Padding = new Thickness(20.0)
		};
	}
}
