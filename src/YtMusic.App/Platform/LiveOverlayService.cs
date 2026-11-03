using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.App.Platform;

/// <summary>Serves a now-playing overlay on this computer for live streaming apps to use as a browser source.</summary>
public sealed class LiveOverlayService : IDisposable
{
	public const string Url = "http://localhost:47823/";

	private const string Page = """
		<!doctype html>
		<html lang="pt-BR">
		<head>
		<meta charset="utf-8">
		<title>Audial Overlay</title>
		<style>
		html, body { margin: 0; background: transparent; overflow: hidden; font-family: "Segoe UI", Arial, sans-serif; }
		#card { display: flex; align-items: center; gap: 14px; box-sizing: border-box; width: 420px; padding: 12px; margin: 8px;
			border-radius: 16px; background: rgba(17, 19, 25, .86); border: 1px solid rgba(255, 255, 255, .1); color: #fff;
			opacity: 0; transform: translateY(8px); transition: opacity .35s, transform .35s; }
		#card.on { opacity: 1; transform: none; }
		#art { flex: none; width: 72px; height: 72px; border-radius: 10px; object-fit: cover; background: linear-gradient(45deg, #F43F5E, #EC4899); }
		#info { flex: 1; min-width: 0; }
		#label { font-size: 11px; font-weight: 600; letter-spacing: 1.5px; color: #FB7185; }
		#title, #artist { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
		#title { font-size: 19px; font-weight: 600; margin-top: 2px; }
		#artist { font-size: 14px; color: #D4D4D8; }
		#bar { height: 4px; margin-top: 8px; border-radius: 2px; background: rgba(255, 255, 255, .16); overflow: hidden; }
		#fill { height: 100%; width: 0; border-radius: 2px; background: linear-gradient(90deg, #F43F5E, #EC4899); transition: width 1s linear; }
		</style>
		</head>
		<body>
		<div id="card">
			<img id="art" alt="">
			<div id="info">
				<div id="label">OUVINDO NO AUDIAL</div>
				<div id="title"></div>
				<div id="artist"></div>
				<div id="bar"><div id="fill"></div></div>
			</div>
		</div>
		<script>
		const $ = id => document.getElementById(id);
		let cover = null;
		async function refresh() {
			try {
				const now = await (await fetch("/now", { cache: "no-store" })).json();
				$("card").classList.toggle("on", now.playing);
				if (!now.playing) return;
				$("title").textContent = now.title;
				$("artist").textContent = now.artist;
				if (now.artwork !== cover) {
					cover = now.artwork;
					if (cover) $("art").src = cover; else $("art").removeAttribute("src");
				}
				$("fill").style.width = (now.duration > 0 ? Math.min(100, now.position / now.duration * 100) : 0) + "%";
			} catch {
				$("card").classList.remove("on");
			}
		}
		refresh();
		setInterval(refresh, 1000);
		</script>
		</body>
		</html>
		""";

	private readonly AppSettings _settings;

	private readonly PlaybackCoordinator _playback;

	private HttpListener? _listener;

	public LiveOverlayService(AppSettings settings, PlaybackCoordinator playback)
	{
		_settings = settings;
		_playback = playback;
	}

	public void ApplySettings()
	{
		if (!_settings.LiveOverlayEnabled)
		{
			Stop();
		}
		else if (_listener == null)
		{
			Start();
		}
	}

	private void Start()
	{
		HttpListener listener = new HttpListener();
		listener.Prefixes.Add(Url);
		try
		{
			listener.Start();
		}
		catch (HttpListenerException)
		{
			listener.Close();
			return;
		}
		_listener = listener;
		_ = Task.Run(() => ServeAsync(listener));
	}

	private void Stop()
	{
		HttpListener? listener = _listener;
		_listener = null;
		listener?.Close();
	}

	private async Task ServeAsync(HttpListener listener)
	{
		while (true)
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync();
			}
			catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException || ex is InvalidOperationException)
			{
				break;
			}
			try
			{
				await RespondAsync(context);
			}
			catch (Exception ex2) when (ex2 is HttpListenerException || ex2 is IOException || ex2 is ObjectDisposedException)
			{
			}
		}
	}

	private async Task RespondAsync(HttpListenerContext context)
	{
		HttpListenerResponse response = context.Response;
		string path = context.Request.Url?.AbsolutePath ?? "";
		string body = "";
		if (context.Request.HttpMethod != "GET" || (path != "/" && path != "/now"))
		{
			response.StatusCode = 404;
		}
		else if (path == "/")
		{
			response.ContentType = "text/html; charset=utf-8";
			body = Page;
		}
		else
		{
			response.ContentType = "application/json; charset=utf-8";
			body = NowPlaying();
		}
		response.Headers["Cache-Control"] = "no-store";
		byte[] bytes = Encoding.UTF8.GetBytes(body);
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes);
		response.Close();
	}

	private string NowPlaying()
	{
		Track? track = _playback.CurrentTrack;
		IPlayer player = _playback.Player;
		string? artwork = track?.ArtworkUrl;
		return JsonSerializer.Serialize(new
		{
			playing = (object?)track != null && player.State == PlayerState.Playing,
			title = track?.Title ?? "",
			artist = track?.Artist ?? "",
			artwork = (artwork != null && artwork.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ? artwork : null,
			position = player.Position.TotalSeconds,
			duration = (track?.Duration ?? player.Duration).TotalSeconds
		});
	}

	public void Dispose()
	{
		Stop();
	}
}
