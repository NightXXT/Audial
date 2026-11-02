using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;

namespace YtMusic.Presentation.ViewModels;

public abstract class LoadableViewModel : ObservableObject
{
	private readonly IAuthProvider _auth;

	private CancellationTokenSource? _cts;

	private ViewState _state;

	private string _stateMessage = "";

	public IAsyncRelayCommand ReloadCommand { get; }

	public IAsyncRelayCommand SignInCommand { get; }

	public ViewState State
	{
		get
		{
			return _state;
		}
		protected set
		{
			if (SetProperty(ref _state, value, "State"))
			{
				OnPropertyChanged("IsContent");
				OnPropertyChanged("IsLoading");
			}
		}
	}

	public string StateMessage
	{
		get
		{
			return _stateMessage;
		}
		protected set
		{
			SetProperty(ref _stateMessage, value, "StateMessage");
		}
	}

	public bool IsContent => State == ViewState.Content;

	public bool IsLoading => State == ViewState.Loading;

	protected LoadableViewModel(IAuthProvider auth)
	{
		_auth = auth;
		ReloadCommand = new AsyncRelayCommand(ReloadAsync);
		SignInCommand = new AsyncRelayCommand(SignInAndReloadAsync);
	}

	public abstract Task ReloadAsync();

	protected async Task RunAsync(Func<CancellationToken, Task<bool>> work, string emptyMessage = "Nada por aqui ainda.")
	{
		_cts?.Cancel();
		CancellationTokenSource cts = (_cts = new CancellationTokenSource());
		if (_auth.State == AuthState.SignedOut)
		{
			StateMessage = "Entre na sua conta para ver este conteúdo.";
			State = ViewState.SignedOut;
			return;
		}
		State = ViewState.Loading;
		try
		{
			bool flag = await work(cts.Token);
			if (!cts.IsCancellationRequested)
			{
				if (flag)
				{
					State = ViewState.Content;
					return;
				}
				StateMessage = emptyMessage;
				State = ViewState.Empty;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (SessionExpiredException ex2)
		{
			StateMessage = ex2.Message;
			State = ViewState.SessionExpired;
		}
		catch (ProviderException ex3)
		{
			StateMessage = ex3.Message;
			State = ViewState.Error;
		}
		catch (Exception ex4) when (((ex4 is HttpRequestException || ex4 is IOException || ex4 is InvalidOperationException || ex4 is JsonException) ? 1 : 0) != 0)
		{
			StateMessage = "Algo deu errado ao carregar. Tente novamente.";
			State = ViewState.Error;
		}
	}

	protected void CancelWork()
	{
		_cts?.Cancel();
	}

	private async Task SignInAndReloadAsync()
	{
		try
		{
			await _auth.SignInAsync(CancellationToken.None);
		}
		catch (ProviderException ex)
		{
			StateMessage = ex.Message;
			State = ViewState.Error;
			return;
		}
		await ReloadAsync();
	}
}
