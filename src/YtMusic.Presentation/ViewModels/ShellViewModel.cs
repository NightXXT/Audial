using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Presentation.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
	private readonly IAuthProvider _auth;

	private readonly IUiDispatcher _ui;

	private string _statusMessage = "";

	public PlayerViewModel Player { get; }

	public bool IsDemo { get; }

	public IAsyncRelayCommand SignInCommand { get; }

	public IAsyncRelayCommand SignOutCommand { get; }

	public AuthState AuthState => _auth.State;

	public bool IsSignedIn => _auth.State == AuthState.SignedIn;

	public bool IsPublicMusic => _auth.State == AuthState.PublicAccess;

	public string AccountText => _auth.State switch
	{
		AuthState.SignedIn => IsDemo ? "Conta de demonstração" : "Google conectado · catálogo público", 
		AuthState.SigningIn => "Entrando… conclua no navegador", 
		AuthState.SessionExpired => "Sessão expirada", 
		AuthState.PublicAccess => "Catálogo público • YouTube Music", 
		_ => "Não conectado", 
	};

	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		set
		{
			if (SetProperty(ref _statusMessage, value, "StatusMessage"))
			{
				OnPropertyChanged("HasStatusMessage");
			}
		}
	}

	public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

	public ShellViewModel(IAuthProvider auth, PlayerViewModel player, IUiDispatcher ui, bool isDemo)
	{
		_auth = auth;
		_ui = ui;
		Player = player;
		IsDemo = isDemo;
		_auth.StateChanged += delegate
		{
			_ui.Post(delegate
			{
				OnPropertyChanged("AuthState");
				OnPropertyChanged("IsSignedIn");
				OnPropertyChanged("AccountText");
			});
		};
		Player.ErrorRaised += delegate(object? _, string msg)
		{
			StatusMessage = msg;
		};
		Player.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "IsPlaying" && Player.IsPlaying)
			{
				StatusMessage = "";
			}
		};
		SignInCommand = new AsyncRelayCommand(SignInAsync);
		SignOutCommand = new AsyncRelayCommand(SignOutAsync);
	}

	public async Task InitializeAsync()
	{
		try
		{
			await _auth.TryRestoreSessionAsync(CancellationToken.None);
		}
		catch (ProviderException ex)
		{
			StatusMessage = ex.Message;
		}
	}

	private async Task SignInAsync(CancellationToken ct)
	{
		try
		{
			await _auth.SignInAsync(ct);
			StatusMessage = "";
		}
		catch (ProviderException ex)
		{
			StatusMessage = ex.Message;
		}
		catch (TimeoutException)
		{
			StatusMessage = "Tempo esgotado esperando o login.";
		}
		catch (OperationCanceledException)
		{
			StatusMessage = "Login cancelado.";
		}
		catch (Exception ex4) when (((ex4 is HttpRequestException || ex4 is IOException || ex4 is HttpListenerException || ex4 is CryptographicException) ? 1 : 0) != 0)
		{
			StatusMessage = "Não foi possível concluir o acesso. Confira sua conexão e tente novamente.";
		}
	}

	private async Task SignOutAsync()
	{
		try
		{
			await _auth.SignOutAsync(CancellationToken.None);
			StatusMessage = "";
		}
		catch (ProviderException ex)
		{
			StatusMessage = ex.Message;
		}
	}
}
