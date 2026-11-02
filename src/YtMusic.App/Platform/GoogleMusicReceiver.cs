using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Presentation.ViewModels;
using YtMusic.Services;
using YtMusic.Services.Auth;

namespace YtMusic.App.Platform;

public sealed class GoogleMusicReceiver : ObservableObject, IDisposable
{
	private readonly PlayerViewModel _vm;

	private readonly IUiDispatcher _ui;

	private readonly DispatcherQueueTimer _timer;

	private readonly SemaphoreSlim _writes = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _storageGate = new SemaphoreSlim(1, 1);

	private readonly DpapiTokenStore _store = new DpapiTokenStore(Path.Combine(AppPaths.DataDirectory, "receiver-session.bin"));

	private Dictionary<string, JsonElement> _saved = new Dictionary<string, JsonElement>();

	private CancellationTokenSource? _lifetime;

	private Process? _process;

	private string _code = "";

	private string _status = "Desativado";

	private bool _running;

	private bool _connected;

	private string? _expectedRemoteTrack;

	public string PairingCode
	{
		get
		{
			return _code;
		}
		private set
		{
			SetProperty(ref _code, value, "PairingCode");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		private set
		{
			SetProperty(ref _status, value, "Status");
		}
	}

	public bool IsRunning
	{
		get
		{
			return _running;
		}
		private set
		{
			SetProperty(ref _running, value, "IsRunning");
		}
	}

	public GoogleMusicReceiver(PlayerViewModel vm, IUiDispatcher ui)
	{
		_vm = vm;
		_ui = ui;
		_vm.Playback.CurrentTrackChanged += OnTrackChanged;
		_timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		_timer.Interval = TimeSpan.FromMilliseconds(750.0);
		_timer.Tick += async delegate
		{
			await SendStateAsync();
		};
	}

	public async Task StartAsync(string name)
	{
		if (IsRunning)
		{
			return;
		}
		string directory = Path.Combine(AppContext.BaseDirectory, "receiver");
		string node = Path.Combine(directory, "node.exe");
		if (!File.Exists(node) || !File.Exists(Path.Combine(directory, "main.mjs")))
		{
			Status = "O componente receptor não está neste pacote. Extraia o ZIP completo.";
			return;
		}
		try
		{
			Directory.CreateDirectory(AppPaths.DataDirectory);
			string text = await _store.LoadAsync(CancellationToken.None);
			_saved = ((text == null) ? new Dictionary<string, JsonElement>() : (JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? new Dictionary<string, JsonElement>()));
			_lifetime = new CancellationTokenSource();
			ProcessStartInfo processStartInfo = new ProcessStartInfo(node)
			{
				WorkingDirectory = directory,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			processStartInfo.ArgumentList.Add("main.mjs");
			if (Environment.GetCommandLineArgs().Contains("--receiver-test") && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIAL_DIAGNOSTICS")))
			{
				processStartInfo.Environment["AUDIAL_RECEIVER_TEST"] = "1";
			}
			processStartInfo.Environment["AUDIAL_RECEIVER_NAME"] = (string.IsNullOrWhiteSpace(name) ? "Audial Windows" : name.Trim().Substring(0, Math.Min(name.Trim().Length, 60)));
			_process = Process.Start(processStartInfo) ?? throw new IOException();
			IsRunning = true;
			Status = "Conectando ao Google…";
			PairingCode = "";
			ReadAsync(_process, _lifetime.Token);
			DrainErrorsAsync(_process, _lifetime.Token);
			_timer.Start();
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			Stop();
			Status = "Não foi possível iniciar o receptor. Verifique a conexão e tente novamente.";
		}
	}

	private static async Task DrainErrorsAsync(Process process, CancellationToken ct)
	{
		try
		{
			while (await process.StandardError.ReadLineAsync(ct) != null)
			{
			}
		}
		catch (Exception ex) when (((ex is OperationCanceledException || ex is IOException || ex is ObjectDisposedException) ? 1 : 0) != 0)
		{
		}
	}

	private async Task ReadAsync(Process process, CancellationToken ct)
	{
		Process process2 = process;
		try
		{
			while (true)
			{
				string text = await process2.StandardOutput.ReadLineAsync(ct);
				if (text == null)
				{
					break;
				}
				if (text.Length > 131072)
				{
					continue;
				}
				JsonElement message;
				try
				{
					message = JsonSerializer.Deserialize<JsonElement>(text);
				}
				catch (JsonException)
				{
					continue;
				}
				_ui.Post(async delegate
				{
					if (!ct.IsCancellationRequested)
					{
						await HandleAsync(message);
					}
				});
			}
		}
		catch (Exception ex2) when (((ex2 is OperationCanceledException || ex2 is IOException || ex2 is ObjectDisposedException) ? 1 : 0) != 0)
		{
		}
		finally
		{
			_ui.Post(delegate
			{
				if (_process == process2 && !ct.IsCancellationRequested)
				{
					Stop();
					Status = "O receptor foi encerrado. Ative novamente para reconectar.";
				}
			});
		}
	}

	private async Task HandleAsync(JsonElement msg)
	{
		if (!msg.TryGetProperty("type", out var value))
		{
			return;
		}
		switch (value.GetString())
		{
		case "ready":
			Status = "Aguardando controle. Vincule pelo código abaixo.";
			break;
		case "diagnostic":
			App.Diagnose("RECEIVER TEST " + msg.GetProperty("stage").GetString());
			break;
		case "pairing":
			PairingCode = msg.GetProperty("code").GetString() ?? "";
			Status = "Código recebido do Google. Aguardando pareamento.";
			break;
		case "error":
			Status = msg.GetProperty("message").GetString() ?? "A conexão do receptor falhou.";
			break;
		case "connected":
		{
			int @int = msg.GetProperty("count").GetInt32();
			_connected = @int > 0;
			Status = ((@int > 0) ? "Controle conectado • áudio neste computador" : "Aguardando controle.");
			_vm.Playback.RemoteNext = ((@int > 0) ? ((Func<Task>)(() => SendControlAsync("next"))) : null);
			_vm.Playback.RemotePrevious = ((@int > 0) ? ((Func<Task>)(() => SendControlAsync("previous"))) : null);
			break;
		}
		case "rpc":
		{
			int id = msg.GetProperty("id").GetInt32();
			try
			{
				await SendAsync(new
				{
					type = "reply",
					id = id,
					ok = true,
					result = await ExecuteAsync(msg.GetProperty("method").GetString() ?? "", msg.GetProperty("args"))
				});
				break;
			}
			catch (Exception ex) when (!(ex is OutOfMemoryException))
			{
				await SendAsync(new
				{
					type = "reply",
					id = id,
					ok = false
				});
				Status = "Não foi possível executar um comando recebido. Tente outra faixa.";
				break;
			}
		}
		}
	}

	private async Task<object?> ExecuteAsync(string method, JsonElement args)
	{
		IPlayer player = _vm.Playback.Player;
		switch (method)
		{
		case "play":
		{
			string videoId = args.GetProperty("videoId").GetString() ?? "";
			if (!Regex.IsMatch(videoId, "^[A-Za-z0-9_-]{11}$"))
			{
				return false;
			}
			Track track = await ReadMusicTrackAsync(videoId);
			_expectedRemoteTrack = videoId;
			await _vm.Playback.PlayQueueAsync(new Track[1] { track }, 0);
			DateTime deadline = DateTime.UtcNow.AddSeconds(40.0);
			while (true)
			{
				PlayerState state = player.State;
				if ((uint)state > 1u)
				{
					break;
				}
				if (DateTime.UtcNow > deadline)
				{
					return false;
				}
				await Task.Delay(200);
			}
			if (player.State != PlayerState.Playing || _vm.CurrentTrack?.Id != videoId)
			{
				return false;
			}
			if (args.TryGetProperty("position", out var value2) && value2.TryGetDouble(out var value3) && double.IsFinite(value3) && value3 > 0.0)
			{
				await _vm.Playback.SeekAsync(TimeSpan.FromSeconds(Math.Min(value3, 86400.0)));
			}
			await SendStateAsync();
			return true;
		}
		case "pause":
			await player.PauseAsync();
			return true;
		case "resume":
			await player.ResumeAsync();
			return true;
		case "stop":
			await _vm.Playback.ClearAsync();
			return true;
		case "seek":
		{
			double double2 = args.GetProperty("position").GetDouble();
			if (!double.IsFinite(double2))
			{
				return false;
			}
			await _vm.Playback.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(double2, 0.0, 86400.0)));
			return true;
		}
		case "volume":
		{
			double @double = args.GetProperty("level").GetDouble();
			if (!double.IsFinite(@double))
			{
				return false;
			}
			_vm.Volume = Math.Clamp(@double, 0.0, 100.0);
			return true;
		}
		case "storeGet":
		{
			JsonElement value;
			return _saved.TryGetValue(args.GetProperty("key").GetString() ?? "", out value) ? ((object)value) : null;
		}
		case "storeSet":
			await _storageGate.WaitAsync();
			try
			{
				_saved[args.GetProperty("key").GetString() ?? ""] = args.GetProperty("value").Clone();
				await _store.SaveAsync(JsonSerializer.Serialize(_saved), CancellationToken.None);
			}
			finally
			{
				_storageGate.Release();
			}
			return true;
		default:
			return false;
		}
	}

	private static async Task<Track> ReadMusicTrackAsync(string id)
	{
		_ = 1;
		try
		{
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(10.0)
			};
			using HttpResponseMessage response = await http.PostAsJsonAsync("https://music.youtube.com/youtubei/v1/music/get_queue?alt=json", new
			{
				context = new
				{
					client = new
					{
						clientName = "WEB_REMIX",
						clientVersion = "1." + DateTime.UtcNow.ToString("yyyyMMdd") + ".01.00",
						hl = "pt",
						gl = "BR"
					}
				},
				videoIds = new string[1] { id }
			});
			using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			JsonElement row2 = FindRow(jsonDocument.RootElement);
			string text = Text(row2, "title");
			string text2 = Text(row2, "longBylineText");
			if (text2.Length == 0)
			{
				text2 = Text(row2, "shortBylineText");
			}
			if (text.Length > 0)
			{
				return new Track(id, text, text2, null, null, "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg");
			}
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is JsonException || ex is InvalidOperationException || ex is TaskCanceledException) ? 1 : 0) != 0)
		{
		}
		return new Track(id, "Faixa recebida do YouTube Music", "Controle remoto", null, null, "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg");
		static string Text(JsonElement row, string key)
		{
			if (!row.TryGetProperty(key, out var value) || !value.TryGetProperty("runs", out var value2))
			{
				return "";
			}
			return string.Concat(from x in value2.EnumerateArray()
				select x.GetProperty("text").GetString());
		}
	}

	private static JsonElement FindRow(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Object)
		{
			if (root.TryGetProperty("playlistPanelVideoRenderer", out var value))
			{
				return value;
			}
			foreach (JsonProperty item in root.EnumerateObject())
			{
				JsonElement result = FindRow(item.Value);
				if (result.ValueKind != 0)
				{
					return result;
				}
			}
		}
		if (root.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item2 in root.EnumerateArray())
			{
				JsonElement result2 = FindRow(item2);
				if (result2.ValueKind != 0)
				{
					return result2;
				}
			}
		}
		return default(JsonElement);
	}

	private Task SendStateAsync()
	{
		return SendAsync(new
		{
			type = "state",
			state = new
			{
				position = _vm.Playback.Player.Position.TotalSeconds,
				duration = _vm.Playback.Player.Duration.TotalSeconds,
				volume = _vm.Volume,
				status = _vm.Playback.Player.State.ToString()
			}
		});
	}

	public Task SendControlAsync(string action)
	{
		return SendAsync(new
		{
			type = "control",
			action = action
		});
	}

	private async Task SendAsync(object message)
	{
		await _writes.WaitAsync();
		try
		{
			Process process = _process;
			if (process != null && !process.HasExited)
			{
				await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
		}
		finally
		{
			_writes.Release();
		}
	}

	public void Stop()
	{
		_timer.Stop();
		_lifetime?.Cancel();
		_lifetime?.Dispose();
		_lifetime = null;
		_vm.Playback.RemoteNext = null;
		_vm.Playback.RemotePrevious = null;
		Process process = _process;
		_process = null;
		try
		{
			if (process != null && !process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (InvalidOperationException)
		{
		}
		finally
		{
			process?.Dispose();
		}
		_connected = false;
		_expectedRemoteTrack = null;
		IsRunning = false;
		PairingCode = "";
		Status = "Desativado";
	}

	private void OnTrackChanged(object? sender, EventArgs e)
	{
		// The receiver stops the current track before every change, which leaves the queue empty for a moment.
		Track current = _vm.CurrentTrack;
		if (_connected && (object)current != null && current.Id != _expectedRemoteTrack)
		{
			Stop();
			Status = "Controle encerrado ao iniciar outra fila neste computador.";
		}
	}

	public void Dispose()
	{
		Stop();
		_vm.Playback.CurrentTrackChanged -= OnTrackChanged;
	}
}
