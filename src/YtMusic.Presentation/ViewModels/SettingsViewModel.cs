using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;

namespace YtMusic.Presentation.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
	private readonly AppSettings _settings;

	private readonly ISettingsStore _store;

	private readonly IAuthProvider _auth;

	private string _message = "";

	public bool IsDemo { get; }

	public bool UseTransparency
	{
		get
		{
			return _settings.UseTransparency;
		}
		set
		{
			if (_settings.UseTransparency != value)
			{
				_settings.UseTransparency = value;
				Save();
				OnPropertyChanged("UseTransparency");
			}
		}
	}

	public bool ReduceMotion
	{
		get
		{
			return _settings.ReduceMotion;
		}
		set
		{
			if (_settings.ReduceMotion != value)
			{
				_settings.ReduceMotion = value;
				Save();
				OnPropertyChanged("ReduceMotion");
			}
		}
	}

	public bool PreferDataSaver
	{
		get
		{
			return _settings.PreferDataSaver;
		}
		set
		{
			if (_settings.PreferDataSaver != value)
			{
				_settings.PreferDataSaver = value;
				Save();
				OnPropertyChanged("PreferDataSaver");
			}
		}
	}

	public bool DiscordPresenceEnabled
	{
		get
		{
			return _settings.DiscordPresenceEnabled;
		}
		set
		{
			if (_settings.DiscordPresenceEnabled != value)
			{
				_settings.DiscordPresenceEnabled = value;
				Save();
				OnPropertyChanged("DiscordPresenceEnabled");
			}
		}
	}

	public bool LiveOverlayEnabled
	{
		get
		{
			return _settings.LiveOverlayEnabled;
		}
		set
		{
			if (_settings.LiveOverlayEnabled != value)
			{
				_settings.LiveOverlayEnabled = value;
				Save();
				OnPropertyChanged("LiveOverlayEnabled");
			}
		}
	}

	public string DiscordApplicationId
	{
		get
		{
			return _settings.DiscordApplicationId;
		}
		set
		{
			if (value == null)
			{
				value = "";
			}
			if (!(_settings.DiscordApplicationId == value))
			{
				_settings.DiscordApplicationId = value.Trim();
				Save();
				OnPropertyChanged("DiscordApplicationId");
			}
		}
	}

	public string PlayerName { get; }

	public IAsyncRelayCommand ForgetSessionCommand { get; }

	public bool MinimizeToTray
	{
		get
		{
			return _settings.MinimizeToTray;
		}
		set
		{
			if (_settings.MinimizeToTray != value)
			{
				_settings.MinimizeToTray = value;
				Save();
				OnPropertyChanged("MinimizeToTray");
			}
		}
	}

	public bool DemoMode
	{
		get
		{
			return _settings.DemoMode;
		}
		set
		{
			if (_settings.DemoMode != value)
			{
				_settings.DemoMode = value;
				Save();
				OnPropertyChanged("DemoMode");
				Message = "Reinicie o aplicativo para aplicar a mudança de modo.";
			}
		}
	}

	public string Message
	{
		get
		{
			return _message;
		}
		private set
		{
			SetProperty(ref _message, value, "Message");
		}
	}

	public SettingsViewModel(AppSettings settings, ISettingsStore store, IAuthProvider auth, PlayerViewModel player, bool isDemo)
	{
		_settings = settings;
		_store = store;
		_auth = auth;
		IsDemo = isDemo;
		PlayerName = player.PlayerName;
		ForgetSessionCommand = new AsyncRelayCommand(ForgetSessionAsync);
	}

	public void Save()
	{
		_store.Save(_settings);
	}

	private async Task ForgetSessionAsync()
	{
		try
		{
			await _auth.SignOutAsync(CancellationToken.None);
			Message = "Sessão apagada deste computador.";
		}
		catch (ProviderException ex)
		{
			Message = ex.Message;
		}
	}
}
