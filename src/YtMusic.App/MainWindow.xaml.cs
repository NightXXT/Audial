using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WinRT;
using WinRT.Interop;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using YtMusic.App.Platform;
using YtMusic.App.Views;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;
using YtMusic.Presentation.ViewModels;
using YtMusic.Services.Auth;
using YtMusic.Services.Library;
using YtMusic.Services.Settings;

namespace YtMusic.App;
public sealed partial class MainWindow : Window
{








	private readonly AppSettings _settings;

	private readonly ISettingsStore _settingsStore;

	private bool _exiting;

	private MediaTransportService? _mediaTransport;

	private readonly bool _enterDemo;

	private readonly SettingsViewModel _appearance;

	private MiniPlayerWindow? _miniPlayer;



































	public ShellViewModel Shell { get; }

	public IRelayCommand ShowWindowCommand { get; }

	public IRelayCommand ExitCommand { get; }

	public MainWindow(bool enterDemo = false)
	{
		_enterDemo = enterDemo;
		Shell = App.GetService<ShellViewModel>();
		_settings = App.GetService<AppSettings>();
		_settingsStore = App.GetService<ISettingsStore>();
		_appearance = App.GetService<SettingsViewModel>();
		App.GetService<ListeningHistory>();
		ShowWindowCommand = new RelayCommand(ShowFromTray);
		ExitCommand = new RelayCommand(ExitApp);
		App.Diagnose("MainWindow initialize XAML");
		InitializeComponent();
		App.Diagnose("MainWindow XAML ready");
		ApplyAppearance();
		_appearance.PropertyChanged += OnAppearanceChanged;
		App.GetService<DiscordPresenceService>().ApplySettings();
		Shell.PropertyChanged += OnShellChanged;
		base.ExtendsContentIntoTitleBar = true;
		SetTitleBar(TitleBar);
		base.AppWindow.Resize(new SizeInt32(1440, 960));
		base.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
		ContentFrame.Navigated += OnContentNavigated;
		RegisterShortcuts();
		base.AppWindow.Closing += OnClosing;
		base.Closed += delegate
		{
			PersistAndDispose();
		};
		App.Diagnose("Creating tray");
		Tray.ForceCreate();
		App.Diagnose("Creating media transport");
		_mediaTransport = new MediaTransportService(this, App.GetService<PlaybackCoordinator>(), App.GetService<IUiDispatcher>());
		base.Activated += OnFirstActivated;
		App.Diagnose("Window initialization complete");
	}

	private Button CreateNavButton(string tag, string label, string glyph)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 10.0
		};
		stackPanel.Children.Add(new FontIcon
		{
			Glyph = glyph,
			FontSize = 18.0
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = label
		});
		Button button = new Button();
		button.Tag = tag;
		button.Content = stackPanel;
		button.Style = (Style)Application.Current.Resources["Button.Nav"];
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.Click += OnSidebarClick;
		return button;
	}

	private void RegisterShortcuts()
	{
		AddShortcut(VirtualKey.Space, VirtualKeyModifiers.None, delegate
		{
			Shell.Player.PlayPauseCommand.ExecuteAsync(null);
		});
		AddShortcut(VirtualKey.Right, VirtualKeyModifiers.Control, delegate
		{
			Shell.Player.NextCommand.ExecuteAsync(null);
		});
		AddShortcut(VirtualKey.Left, VirtualKeyModifiers.Control, delegate
		{
			Shell.Player.PreviousCommand.ExecuteAsync(null);
		});
		AddShortcut(VirtualKey.Q, VirtualKeyModifiers.Control, delegate
		{
			NavigateTo("queue");
		});
		AddShortcut(VirtualKey.L, VirtualKeyModifiers.Control, delegate
		{
			NavigateTo("playing");
		});
		AddShortcut(VirtualKey.M, VirtualKeyModifiers.Control, delegate
		{
			ShowMiniPlayer();
		});
	}

	private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
	{
		KeyboardAccelerator keyboardAccelerator = new KeyboardAccelerator
		{
			Key = key,
			Modifiers = modifiers
		};
		keyboardAccelerator.Invoked += handler;
		Root.KeyboardAccelerators.Add(keyboardAccelerator);
	}

	public void ShowMiniPlayer()
	{
		if ((object)_miniPlayer == null)
		{
			_miniPlayer = new MiniPlayerWindow(Shell.Player);
			_miniPlayer.Closed += delegate
			{
				_miniPlayer = null;
			};
		}
		_miniPlayer.Activate();
	}

	private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
	{
		base.Activated -= OnFirstActivated;
		Task minimumDisplay = Task.Delay(1750);
		await Task.Yield();
		try
		{
			await Shell.InitializeAsync();
			if (Shell.IsPublicMusic || (Shell.IsSignedIn && (!Shell.IsDemo || _enterDemo)))
			{
				EnterWorkspace();
				if (ContentFrame.Content is HomePage homePage)
				{
					await Task.WhenAny(homePage.InitialLoadCompleted, Task.Delay(TimeSpan.FromSeconds(5.0)));
				}
			}
			if (Shell.IsPublicMusic && Environment.GetCommandLineArgs().Contains("--audio-test") && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIAL_DIAGNOSTICS")))
			{
				await AudioSmokeTest.RunAsync();
			}
			if (Shell.IsPublicMusic && Environment.GetCommandLineArgs().Contains("--receiver-test") && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIAL_DIAGNOSTICS")))
			{
				await App.GetService<GoogleMusicReceiver>().StartAsync("Audial diagnóstico");
			}
		}
		finally
		{
			await minimumDisplay;
			await HideStartupLoadingAsync();
		}
		await CheckForUpdatesAsync();
	}

	public async Task<string> CheckForUpdatesAsync()
	{
		UpdateService updates = App.GetService<UpdateService>();
		if (!updates.IsInstalled)
		{
			return "A atualização automática só funciona na versão instalada.";
		}
		try
		{
			AppUpdate update = await updates.CheckAsync(CancellationToken.None);
			if ((object)update == null)
			{
				return "Você já está na versão mais recente.";
			}
			string path = await updates.DownloadAsync(update, CancellationToken.None);
			if (await new ContentDialog
			{
				XamlRoot = Root.XamlRoot,
				RequestedTheme = ElementTheme.Dark,
				Title = "Atualização pronta",
				Content = $"O Audial {update.Version} já foi baixado. O app fecha, atualiza e abre de novo sozinho.",
				PrimaryButtonText = "Atualizar agora",
				CloseButtonText = "Depois",
				DefaultButton = ContentDialogButton.Primary
			}.ShowAsync() != ContentDialogResult.Primary)
			{
				return $"O Audial {update.Version} será oferecido de novo na próxima abertura.";
			}
			updates.StartInstaller(path);
			ExitApp();
			return "";
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
			App.Diagnose("Update check failed: " + ex.GetType().Name);
			return "Não foi possível procurar atualizações agora.";
		}
	}

	private async Task HideStartupLoadingAsync()
	{
		if (_settings.ReduceMotion || !new UISettings().AnimationsEnabled)
		{
			StartupLoading.Visibility = Visibility.Collapsed;
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation
		{
			From = 1.0,
			To = 0.0,
			Duration = new Duration(TimeSpan.FromMilliseconds(320.0))
		};
		Storyboard.SetTarget(doubleAnimation, StartupLoading);
		Storyboard.SetTargetProperty(doubleAnimation, "Opacity");
		Storyboard obj = new Storyboard
		{
			Children = { (Timeline)doubleAnimation }
		};
		TaskCompletionSource completion = new TaskCompletionSource();
		obj.Completed += delegate
		{
			completion.TrySetResult();
		};
		obj.Begin();
		await completion.Task;
		StartupLoading.Visibility = Visibility.Collapsed;
		StartupLoading.Opacity = 1.0;
	}

	private void UpdateAlbumsVisibility()
	{
		// The official YouTube API has no saved albums, so the Google session has nothing to show here.
		bool hasAlbums = !(App.GetService<ILibraryProvider>() is HybridLibraryProvider);
		foreach (Button item in Sidebar.Children.OfType<Button>())
		{
			if (item.Tag as string == "albums")
			{
				item.Visibility = (hasAlbums ? Visibility.Visible : Visibility.Collapsed);
			}
		}
	}

	private void EnterWorkspace()
	{
		Welcome.Visibility = Visibility.Collapsed;
		Workspace.Visibility = Visibility.Visible;
		UpdateAlbumsVisibility();
		UpdateSidebar("home");
		LoadSidebarAsync();
		ContentFrame.Navigate(typeof(HomePage));
		if (!_settings.ReduceMotion && new UISettings().AnimationsEnabled)
		{
			DoubleAnimation doubleAnimation = new DoubleAnimation
			{
				From = 0.0,
				To = 1.0,
				Duration = new Duration(TimeSpan.FromMilliseconds(350.0))
			};
			Storyboard.SetTarget(doubleAnimation, Workspace);
			Storyboard.SetTargetProperty(doubleAnimation, "Opacity");
			Storyboard storyboard = new Storyboard();
			storyboard.Children.Add(doubleAnimation);
			storyboard.Begin();
		}
	}

	private async void OnWelcomeSignIn(object sender, RoutedEventArgs e)
	{
		if (Shell.IsDemo)
		{
			Shell.StatusMessage = "A demonstração usa uma conta fictícia. Abra o aplicativo sem --demo para conectar uma conta Google.";
		}
		else if ((object)GoogleOAuthOptions.Load() == null)
		{
			if (await ConfigureGoogleAsync())
			{
				Shell.StatusMessage = "Configuração importada. Clique em Continuar com Google para autorizar sua conta.";
			}
		}
		else
		{
			if (await new ContentDialog
			{
				XamlRoot = Root.XamlRoot,
				RequestedTheme = ElementTheme.Dark,
				Title = "Conectar ao YouTube Music",
				Content = "A busca pública do YouTube Music funciona sem login. A API interna do Music rejeita o OAuth de aplicativo desktop com HTTP 400, então esta conexão Google não libera sua biblioteca pessoal nem é usada para autenticar o áudio. A reprodução nativa continua experimental e não confirma benefícios Premium.",
				PrimaryButtonText = "Continuar no navegador",
				CloseButtonText = "Agora não"
			}.ShowAsync() != ContentDialogResult.Primary)
			{
				return;
			}
			Button loginButton = LoginButton;
			bool isEnabled = (DemoButton.IsEnabled = false);
			loginButton.IsEnabled = isEnabled;
			LoginProgress.Visibility = Visibility.Visible;
			try
			{
				await Shell.SignInCommand.ExecuteAsync(null);
				if (Shell.IsSignedIn)
				{
					EnterWorkspace();
				}
			}
			finally
			{
				LoginProgress.Visibility = Visibility.Collapsed;
				Button loginButton2 = LoginButton;
				isEnabled = (DemoButton.IsEnabled = true);
				loginButton2.IsEnabled = isEnabled;
			}
		}
	}

	private async void OnConfigureGoogle(object sender, RoutedEventArgs e)
	{
		await ConfigureGoogleAsync();
	}

	private async Task<bool> ConfigureGoogleAsync()
	{
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 14.0,
			MaxWidth = 500.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Este protótipo precisa de um cliente OAuth próprio para iniciar o acesso. Ele identifica o aplicativo perante o Google; não é sua senha nem uma chave da assinatura Premium.",
			TextWrapping = TextWrapping.Wrap
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "1. Crie ou selecione um projeto no Google Cloud.\n2. Ative a YouTube Data API v3.\n3. Configure o consentimento OAuth e adicione sua conta como usuário de teste.\n4. Crie um cliente OAuth do tipo App para computador.\n5. Baixe o arquivo JSON e importe aqui.",
			TextWrapping = TextWrapping.Wrap,
			LineHeight = 24.0
		});
		stackPanel.Children.Add(new HyperlinkButton
		{
			Content = "Abrir Google Cloud",
			NavigateUri = new Uri("https://console.cloud.google.com/apis/credentials")
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "O arquivo fica apenas neste computador. O catálogo do Music usa uma integração experimental não oficial; os benefícios Premium ainda não estão validados.",
			TextWrapping = TextWrapping.Wrap,
			FontSize = 12.0
		});
		if (await new ContentDialog
		{
			XamlRoot = Root.XamlRoot,
			RequestedTheme = ElementTheme.Dark,
			Title = "Configurar acesso ao Google",
			Content = stackPanel,
			PrimaryButtonText = "Importar arquivo JSON",
			CloseButtonText = "Agora não",
			DefaultButton = ContentDialogButton.Primary
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return false;
		}
		try
		{
			FileOpenPicker obj = new FileOpenPicker
			{
				FileTypeFilter = { ".json" }
			};
			InitializeWithWindow.Initialize(obj, WindowNative.GetWindowHandle(this));
			StorageFile storageFile = await obj.PickSingleFileAsync();
			if ((object)storageFile == null)
			{
				return false;
			}
			await GoogleOAuthOptions.ImportAsync(storageFile.Path);
			if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YTMUSIC_GOOGLE_CLIENT_ID")))
			{
				Shell.StatusMessage = "Arquivo importado. Uma configuração por variável de ambiente está ativa e tem prioridade.";
			}
			else
			{
				Shell.StatusMessage = "Arquivo importado. Agora você pode continuar com Google.";
			}
			return true;
		}
		catch (Exception ex) when (((ex is JsonException || ex is FormatException) ? 1 : 0) != 0)
		{
			Shell.StatusMessage = ((ex is FormatException) ? ex.Message : "O arquivo não é um JSON válido. Importe o arquivo original baixado do Google Cloud.");
			return false;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is COMException) ? 1 : 0) != 0)
		{
			Shell.StatusMessage = "Não foi possível importar o arquivo. Selecione um JSON salvo neste computador e tente novamente.";
			return false;
		}
	}

	private async void OnExploreDemo(object sender, RoutedEventArgs e)
	{
		if (Shell.IsDemo)
		{
			await Shell.SignInCommand.ExecuteAsync(null);
			EnterWorkspace();
		}
		else
		{
			((App)Application.Current).OpenDemo();
		}
	}

	private void OnCancelLogin(object sender, RoutedEventArgs e)
	{
		Shell.SignInCommand.Cancel();
	}

	private void OnPublicMusic(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).OpenPublicMusic();
	}

	private async void OnConnectAccount(object sender, RoutedEventArgs e)
	{
		if (Shell.IsPublicMusic)
		{
			((App)Application.Current).OpenMusicAccount();
		}
		else
		{
			await Shell.SignInCommand.ExecuteAsync(null);
		}
	}

	private void OnStatusClosed(InfoBar sender, InfoBarClosedEventArgs args)
	{
		Shell.StatusMessage = "";
	}

	private void ApplyAppearance()
	{
		base.SystemBackdrop = (_settings.UseTransparency ? new MicaBackdrop
		{
			Kind = MicaKind.BaseAlt
		} : null);
		Root.Background = (_settings.UseTransparency ? ((Brush)Application.Current.Resources["Brush.Background"]) : new SolidColorBrush(Color.FromArgb(byte.MaxValue, 17, 19, 25)));
	}

	private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "UseTransparency")
		{
			ApplyAppearance();
		}
		string propertyName = e.PropertyName;
		if ((propertyName == "DiscordPresenceEnabled" || propertyName == "DiscordApplicationId") ? true : false)
		{
			App.GetService<DiscordPresenceService>().ApplySettings();
		}
	}

	private async void OnShellChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!(e.PropertyName != "IsSignedIn") && !Shell.IsSignedIn && Workspace.Visibility == Visibility.Visible)
		{
			Workspace.Visibility = Visibility.Collapsed;
			Welcome.Visibility = Visibility.Visible;
			await Shell.Player.Playback.ClearAsync();
		}
	}

	private async void OnSignOut(object sender, RoutedEventArgs e)
	{
		await Shell.Player.Playback.Player.PauseAsync();
		await Shell.SignOutCommand.ExecuteAsync(null);
		if (!Shell.IsSignedIn)
		{
			Workspace.Visibility = Visibility.Collapsed;
			Welcome.Visibility = Visibility.Visible;
		}
	}

	private void OnWelcomeSizeChanged(object sender, SizeChangedEventArgs e)
	{
		bool flag = e.NewSize.Width < 1040.0;
		WelcomeArt.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		ArtColumn.Width = (flag ? new GridLength(0.0) : new GridLength(1.0, GridUnitType.Star));
	}

	private void OnBack(object sender, RoutedEventArgs e)
	{
		if (ContentFrame.CanGoBack)
		{
			ContentFrame.GoBack();
		}
	}

	private void OnSearch(object sender, RoutedEventArgs e)
	{
		NavigateTo("search");
	}

	public void CloseForRestart()
	{
		_exiting = true;
		Close();
	}

	private async void OnSidebarClick(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: string tag }))
		{
			return;
		}
		if (tag == "mini")
		{
			ShowMiniPlayer();
			return;
		}
		try
		{
			NavigateTo(tag);
		}
		catch (Exception ex)
		{
			App.Diagnose($"Navigation to '{tag}' failed: {ex}");
			await new ContentDialog
			{
				XamlRoot = Root.XamlRoot,
				RequestedTheme = ElementTheme.Dark,
				Title = "Não foi possível abrir esta página",
				Content = ex.Message,
				CloseButtonText = "OK"
			}.ShowAsync();
		}
	}

	private void OnNavigateBack(object sender, RoutedEventArgs e)
	{
		if (ContentFrame.CanGoBack)
		{
			ContentFrame.GoBack();
		}
	}

	private void OnNavigateForward(object sender, RoutedEventArgs e)
	{
		if (ContentFrame.CanGoForward)
		{
			ContentFrame.GoForward();
		}
	}

	public void NavigateTo(string tag)
	{
		UpdateSidebar(tag);
		if (tag == null)
		{
			return;
		}
		switch (tag.Length)
		{
		case 7:
			switch (tag[0])
			{
			case 'l':
				if (tag == "library")
				{
					ContentFrame.Navigate(typeof(LibraryPage));
				}
				break;
			case 'p':
				if (tag == "playing")
				{
					ContentFrame.Navigate(typeof(NowPlayingPage));
				}
				break;
			case 'd':
				if (tag == "devices")
				{
					ContentFrame.Navigate(typeof(DevicesPage));
				}
				break;
			}
			break;
		case 6:
			switch (tag[1])
			{
			case 'r':
				if (tag == "artist")
				{
					ContentFrame.Navigate(typeof(ArtistPage));
				}
				break;
			case 'l':
				if (tag == "albums")
				{
					ContentFrame.Navigate(typeof(LibraryPage), "albums");
				}
				break;
			case 'e':
				if (tag == "search")
				{
					ContentFrame.Navigate(typeof(SearchPage), GlobalSearch.Text);
				}
				break;
			}
			break;
		case 8:
			switch (tag[0])
			{
			case 't':
				if (tag == "together")
				{
					ContentFrame.Navigate(typeof(ListenTogetherPage));
				}
				break;
			case 's':
				if (tag == "settings")
				{
					ContentFrame.Navigate(typeof(SettingsPage));
				}
				break;
			}
			break;
		case 5:
			switch (tag[0])
			{
			case 'l':
				if (tag == "liked")
				{
					ContentFrame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Liked, null, "Músicas curtidas"));
				}
				break;
			case 'q':
				if (tag == "queue")
				{
					ContentFrame.Navigate(typeof(QueuePage));
				}
				break;
			}
			break;
		case 4:
			if (tag == "home")
			{
				ContentFrame.Navigate(typeof(HomePage));
			}
			break;
		}
	}

	private void OnContentNavigated(object sender, NavigationEventArgs e)
	{
		// Back, forward and links inside pages also change the page, so the sidebar follows the frame.
		object content = ContentFrame.Content;
		UpdateSidebar((content is HomePage) ? "home" : ((content is SearchPage) ? "search" : ((content is ArtistPage) ? "artist" : ((content is LibraryPage) ? ((e.Parameter as string == "albums") ? "albums" : "library") : ((content is QueuePage) ? "queue" : ((content is DevicesPage) ? "devices" : ((content is NowPlayingPage) ? "playing" : ((content is ListenTogetherPage) ? "together" : ((content is SettingsPage) ? "settings" : ((e.Parameter is TrackListRequest { Kind: TrackListKind.Liked }) ? "liked" : ""))))))))));
	}

	public void OpenSearch(string query)
	{
		GlobalSearch.Text = query;
		NavigateTo("search");
	}

	private void UpdateSidebar(string tag)
	{
		foreach (Button item in Sidebar.Children.OfType<Button>())
		{
			bool flag = item.Tag as string == tag;
			item.Background = flag ? (Brush)Application.Current.Resources["Brush.Accent"] : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
			item.Foreground = (Brush)Application.Current.Resources[flag ? "Brush.AccentText" : "Brush.TextSecondary"];
		}
	}

	private async Task LoadSidebarAsync()
	{
		if (Shell.IsDemo)
		{
			SidebarCollections.ItemsSource = new PlaylistSummary[4]
			{
				new PlaylistSummary("demo-pl-1", "Mix Relax", 6, null, IsDemo: true),
				new PlaylistSummary("demo-pl-2", "Foco Profundo", 6, null, IsDemo: true),
				new PlaylistSummary("demo-pl-3", "Descobertas da Semana", 6, null, IsDemo: true),
				new PlaylistSummary("demo-pl-4", "Synthwave & Lo-Fi", 6, null, IsDemo: true)
			};
		}
		else if (Shell.IsSignedIn)
		{
			try
			{
				ItemsControl sidebarCollections = SidebarCollections;
				sidebarCollections.ItemsSource = (await App.GetService<ILibraryProvider>().GetPlaylistsAsync(null, CancellationToken.None)).Items.Take(5).ToArray();
			}
			catch (Exception ex) when (!(ex is OutOfMemoryException))
			{
			}
		}
	}

	private void OnCollectionClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: PlaylistSummary dataContext })
		{
			ContentFrame.Navigate(typeof(TrackListPage), new TrackListRequest(TrackListKind.Playlist, dataContext.Id, dataContext.Title));
		}
	}

	private void OnGlobalSearchKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Enter)
		{
			NavigateTo("search");
			e.Handled = true;
		}
	}

	private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		GlobalSearch.Focus(FocusState.Keyboard);
		GlobalSearch.SelectAll();
		args.Handled = true;
	}

	private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		if (!_exiting && _settings.MinimizeToTray)
		{
			args.Cancel = true;
			sender.Hide();
		}
	}

	private void ShowFromTray()
	{
		base.AppWindow.Show();
		Activate();
	}

	public void ActivateMainWindow()
	{
		ShowFromTray();
	}

	private void ExitApp()
	{
		_exiting = true;
		Close();
	}

	private void PersistAndDispose()
	{
		App.GetService<DiscordPresenceService>().Dispose();
		App.GetService<GoogleMusicReceiver>().Dispose();
		(Shell.Player.Playback.Player as IDisposable)?.Dispose();
		try
		{
			_settingsStore.Save(_settings);
		}
		catch (IOException)
		{
		}
		Tray.Dispose();
		_mediaTransport?.Dispose();
		_appearance.PropertyChanged -= OnAppearanceChanged;
		Shell.PropertyChanged -= OnShellChanged;
	}






}
