using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YtMusic.Services.Together;

public sealed record RoomSignalMessage(string From, string To, string Kind, string Data);

/// <summary>
/// Exchanges the WebRTC handshake through ntfy.sh, a public relay that needs no account.
/// Everything is encrypted with a key derived from the invite, which the relay never sees.
/// </summary>
public sealed class RoomSignal : IDisposable
{
	private const string RelayUrl = "https://ntfy.sh/";

	private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	private const int NonceSize = 12;

	private const int TagSize = 16;

	private static readonly Regex InvitePattern = new Regex("AUD-?([A-Z0-9]{5})-?([A-Z0-9]{5})", RegexOptions.Compiled);

	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2.0);

	private readonly HttpClient _http = new HttpClient
	{
		Timeout = Timeout.InfiniteTimeSpan
	};

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

	private readonly string _topic;

	private readonly byte[] _key;

	public event Action<RoomSignalMessage>? Received;

	public RoomSignal(string code)
	{
		byte[] secret = Encoding.UTF8.GetBytes(code);
		_topic = "audial-" + Convert.ToHexString(HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 16, null, Encoding.UTF8.GetBytes("audial-room-topic"))).ToLowerInvariant();
		_key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, null, Encoding.UTF8.GetBytes("audial-room-key"));
	}

	public static string NewCode()
	{
		string text = RandomNumberGenerator.GetString(Alphabet, 10);
		return "AUD-" + text.Substring(0, 5) + "-" + text.Substring(5);
	}

	public static string? ParseCode(string? text)
	{
		Match match = InvitePattern.Match((text ?? "").ToUpperInvariant());
		if (!match.Success)
		{
			return null;
		}
		return "AUD-" + match.Groups[1].Value + "-" + match.Groups[2].Value;
	}

	public void Start()
	{
		_ = Task.Run(ListenAsync);
	}

	public async Task SendAsync(RoomSignalMessage message)
	{
		string body = Convert.ToBase64String(Seal(JsonSerializer.SerializeToUtf8Bytes(message)));
		using HttpResponseMessage response = await _http.PostAsync(RelayUrl + _topic, new StringContent(body), _cts.Token);
		response.EnsureSuccessStatusCode();
	}

	private async Task ListenAsync()
	{
		while (!_cts.IsCancellationRequested)
		{
			try
			{
				using HttpResponseMessage response = await _http.GetAsync(RelayUrl + _topic + "/json?since=all", HttpCompletionOption.ResponseHeadersRead, _cts.Token);
				response.EnsureSuccessStatusCode();
				using StreamReader reader = new StreamReader(await response.Content.ReadAsStreamAsync(_cts.Token));
				while (true)
				{
					string? line = await reader.ReadLineAsync(_cts.Token);
					if (line == null)
					{
						break;
					}
					Dispatch(line);
				}
			}
			catch (OperationCanceledException) when (_cts.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
			{
			}
			try
			{
				await Task.Delay(RetryDelay, _cts.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private void Dispatch(string line)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(line);
			JsonElement root = jsonDocument.RootElement;
			if (root.GetProperty("event").GetString() != "message" || !_seen.Add(root.GetProperty("id").GetString() ?? ""))
			{
				return;
			}
			byte[] opened = Open(Convert.FromBase64String(root.GetProperty("message").GetString() ?? ""));
			RoomSignalMessage? message = JsonSerializer.Deserialize<RoomSignalMessage>(opened);
			if ((object?)message != null)
			{
				this.Received?.Invoke(message);
			}
		}
		catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is KeyNotFoundException || ex is InvalidOperationException || ex is CryptographicException || ex is InvalidDataException)
		{
		}
	}

	private byte[] Seal(byte[] plain)
	{
		byte[] compressed = Compress(plain);
		byte[] sealedBytes = new byte[NonceSize + TagSize + compressed.Length];
		Span<byte> nonce = sealedBytes.AsSpan(0, NonceSize);
		RandomNumberGenerator.Fill(nonce);
		using AesGcm aesGcm = new AesGcm(_key, TagSize);
		aesGcm.Encrypt(nonce, compressed, sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize));
		return sealedBytes;
	}

	private byte[] Open(byte[] sealedBytes)
	{
		if (sealedBytes.Length < NonceSize + TagSize)
		{
			throw new CryptographicException();
		}
		byte[] compressed = new byte[sealedBytes.Length - NonceSize - TagSize];
		using AesGcm aesGcm = new AesGcm(_key, TagSize);
		aesGcm.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize), compressed);
		return Decompress(compressed);
	}

	private static byte[] Compress(byte[] data)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionLevel.SmallestSize, leaveOpen: true))
		{
			deflateStream.Write(data);
		}
		return memoryStream.ToArray();
	}

	private static byte[] Decompress(byte[] data)
	{
		using DeflateStream deflateStream = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		deflateStream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	public void Dispose()
	{
		_cts.Cancel();
		_http.Dispose();
	}
}
