using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using YtMusic.App.Platform;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Playback;
using YtMusic.Services.Together;
using YtMusic.Presentation.ViewModels;
using YtMusic.Services;
using YtMusic.Services.Auth;
using YtMusic.Services.Demo;
using YtMusic.Services.Library;
using YtMusic.Services.Settings;

namespace YtMusic.App;
public partial class App : Application
{
	private static IServiceProvider? _services;

	private Window? _window;







	public App()
	{
		Diagnose("Application constructor");
		base.UnhandledException += delegate(object _, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
		{
			Diagnose("Unhandled: " + e.Exception.GetType().Name);
		};
		InitializeComponent();
		Diagnose("Application XAML initialized");
	}

	internal static void Diagnose(string stage)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("AUDIAL_DIAGNOSTICS");
		if (string.IsNullOrWhiteSpace(environmentVariable))
		{
			return;
		}
		try
		{
			File.AppendAllText(environmentVariable, $"{DateTime.UtcNow:O} {stage}{Environment.NewLine}");
		}
		catch (IOException)
		{
		}
	}

	public static T GetService<T>() where T : notnull
	{
		return (_services ?? throw new InvalidOperationException("Serviços ainda não inicializados.")).GetRequiredService<T>();
	}

	public void NavigateTo(string destination)
	{
		(_window as MainWindow)?.NavigateTo(destination);
	}

	public void OpenSearch(string query)
	{
		(_window as MainWindow)?.OpenSearch(query);
	}

	public void ShowMiniPlayer()
	{
		(_window as MainWindow)?.ShowMiniPlayer();
	}

	public Task<string> CheckForUpdatesAsync()
	{
		return (_window as MainWindow)?.CheckForUpdatesAsync() ?? Task.FromResult("");
	}

	public void ShowMainWindow()
	{
		(_window as MainWindow)?.ActivateMainWindow();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		Diagnose("OnLaunched");
		JsonSettingsStore jsonSettingsStore = new JsonSettingsStore();
		AppSettings appSettings = jsonSettingsStore.Load();
		bool flag = appSettings.DemoMode || Environment.GetCommandLineArgs().Contains("--demo");
		bool flag2 = Environment.GetCommandLineArgs().Contains("--music-public");
		OpenSession(jsonSettingsStore, appSettings, !flag2 && flag, enterDemo: false, flag2);
	}

	public void OpenDemo()
	{
		JsonSettingsStore jsonSettingsStore = new JsonSettingsStore();
		OpenSession(jsonSettingsStore, jsonSettingsStore.Load(), demo: true, enterDemo: true);
	}

	public void OpenPublicMusic()
	{
		JsonSettingsStore jsonSettingsStore = new JsonSettingsStore();
		OpenSession(jsonSettingsStore, jsonSettingsStore.Load(), demo: false, enterDemo: false, publicMusic: true);
	}

	public void OpenMusicAccount()
	{
		JsonSettingsStore jsonSettingsStore = new JsonSettingsStore();
		OpenSession(jsonSettingsStore, jsonSettingsStore.Load(), demo: false);
	}

	private void OpenSession(ISettingsStore store, AppSettings settings, bool demo, bool enterDemo = false, bool publicMusic = false)
	{
		MainWindow mainWindow = _window as MainWindow;
		IDisposable obj = _services as IDisposable;
		_services = BuildServices(store, settings, demo, publicMusic);
		Diagnose("Services ready");
		_window = new MainWindow(enterDemo || demo);
		Diagnose("Window constructed");
		_window.Activate();
		mainWindow?.CloseForRestart();
		obj?.Dispose();
	}

	private static IServiceProvider BuildServices(ISettingsStore settingsStore, AppSettings settings, bool demo, bool publicMusic)
	{
		ServiceCollection services = new ServiceCollection();
		services.AddSingleton(settingsStore);
		services.AddSingleton(settings);
		services.AddSingleton<UpdateService>();
		((IServiceCollection)services).AddSingleton((IUiDispatcher)new DispatcherQueueDispatcher(DispatcherQueue.GetForCurrentThread()));
		if (demo)
		{
			services.AddSingleton<IAuthProvider, DemoAuthProvider>();
			services.AddSingleton<ILibraryProvider, DemoLibraryProvider>();
			services.AddSingleton<IPlayer, DemoPlayer>();
		}
		else
		{
			services.AddSingleton(new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(30.0)
			});
			((IServiceCollection)services).AddSingleton((ISecureTokenStore)new DpapiTokenStore(Path.Combine(AppPaths.DataDirectory, "music-session.bin")));
			services.AddSingleton<IBrowserLauncher, ShellBrowserLauncher>();
			if (publicMusic)
			{
				services.AddSingleton<IAuthProvider, PublicMusicAuthProvider>();
			}
			else
			{
				((IServiceCollection)services).AddSingleton((Func<IServiceProvider, IAuthProvider>)((IServiceProvider sp) => new GoogleOAuthProvider(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISecureTokenStore>(), sp.GetRequiredService<IBrowserLauncher>(), null, "https://www.googleapis.com/auth/youtube")));
			}
			((IServiceCollection)services).AddSingleton((Func<IServiceProvider, ILibraryProvider>)delegate(IServiceProvider sp)
			{
				HttpClient http = sp.GetRequiredService<HttpClient>();
				IAuthProvider auth = sp.GetRequiredService<IAuthProvider>();
				YouTubeMusicLibraryProvider music = new YouTubeMusicLibraryProvider(http, auth, !publicMusic);
				return publicMusic ? music : new HybridLibraryProvider(music, new YouTubeDataApiLibraryProvider(http, auth));
			});
			services.AddSingleton<IPlayer, NativeAudioPlayer>();
		}
		services.AddSingleton<PlaybackQueue>();
		services.AddSingleton<PlaybackCoordinator>();
		services.AddSingleton<ListeningHistory>();
		services.AddSingleton<GoogleMusicReceiver>();
		services.AddSingleton<DiscordPresenceService>();
		services.AddSingleton<ListenRoom>();
		services.AddSingleton<PlayerViewModel>();
		services.AddSingleton((IServiceProvider sp) => new ShellViewModel(sp.GetRequiredService<IAuthProvider>(), sp.GetRequiredService<PlayerViewModel>(), sp.GetRequiredService<IUiDispatcher>(), demo));
		services.AddSingleton((IServiceProvider sp) => new SettingsViewModel(sp.GetRequiredService<AppSettings>(), sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<IAuthProvider>(), sp.GetRequiredService<PlayerViewModel>(), demo));
		services.AddTransient<LibraryViewModel>();
		services.AddTransient<TrackListViewModel>();
		services.AddTransient<SearchViewModel>();
		services.AddSingleton<QueueViewModel>();
		return services.BuildServiceProvider();
	}








}
