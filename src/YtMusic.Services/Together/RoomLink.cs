using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace YtMusic.Services.Together;

/// <summary>A direct WebRTC data channel to one other person in the room.</summary>
internal sealed class RoomLink : IDisposable
{
	private static readonly string[] StunServers = new string[2] { "stun:stun.cloudflare.com:3478", "stun:stun.l.google.com:19302" };

	private readonly RTCPeerConnection _connection;

	private readonly Func<string, string, Task> _signal;

	private readonly List<RTCIceCandidateInit> _pendingCandidates = new List<RTCIceCandidateInit>();

	private RTCDataChannel? _channel;

	private bool _hasRemoteDescription;

	private bool _closed;

	public event Action? Opened;

	public event Action? Closed;

	public event Action<string>? Received;

	public RoomLink(Func<string, string, Task> signal)
	{
		_signal = signal;
		List<RTCIceServer> servers = new List<RTCIceServer>();
		foreach (string url in StunServers)
		{
			servers.Add(new RTCIceServer
			{
				urls = url
			});
		}
		_connection = new RTCPeerConnection(new RTCConfiguration
		{
			iceServers = servers
		});
		_connection.onicecandidate += OnLocalCandidate;
		_connection.onconnectionstatechange += OnConnectionState;
		_connection.ondatachannel += Attach;
	}

	public async Task OfferAsync()
	{
		Attach(await _connection.createDataChannel("audial"));
		RTCSessionDescriptionInit offer = _connection.createOffer();
		await _connection.setLocalDescription(offer);
		await _signal("offer", offer.sdp);
	}

	public async Task AnswerAsync(string sdp)
	{
		if (!SetRemote(RTCSdpType.offer, sdp))
		{
			return;
		}
		RTCSessionDescriptionInit answer = _connection.createAnswer();
		await _connection.setLocalDescription(answer);
		await _signal("answer", answer.sdp);
	}

	public void AcceptAnswer(string sdp)
	{
		SetRemote(RTCSdpType.answer, sdp);
	}

	public void AddCandidate(string json)
	{
		if (!RTCIceCandidateInit.TryParse(json, out var init))
		{
			return;
		}
		if (_hasRemoteDescription)
		{
			_connection.addIceCandidate(init);
		}
		else
		{
			_pendingCandidates.Add(init);
		}
	}

	public void Send(string text)
	{
		RTCDataChannel? channel = _channel;
		if (channel != null && channel.readyState == RTCDataChannelState.open)
		{
			channel.send(text);
		}
	}

	private bool SetRemote(RTCSdpType type, string sdp)
	{
		if (_connection.setRemoteDescription(new RTCSessionDescriptionInit
		{
			type = type,
			sdp = sdp
		}) != SetDescriptionResultEnum.OK)
		{
			Close();
			return false;
		}
		_hasRemoteDescription = true;
		foreach (RTCIceCandidateInit candidate in _pendingCandidates)
		{
			_connection.addIceCandidate(candidate);
		}
		_pendingCandidates.Clear();
		return true;
	}

	private void Attach(RTCDataChannel channel)
	{
		_channel = channel;
		channel.onopen += delegate
		{
			this.Opened?.Invoke();
		};
		channel.onclose += Close;
		channel.onmessage += delegate(RTCDataChannel _, DataChannelPayloadProtocols _, byte[] data)
		{
			this.Received?.Invoke(Encoding.UTF8.GetString(data));
		};
		if (channel.readyState == RTCDataChannelState.open)
		{
			this.Opened?.Invoke();
		}
	}

	private void OnLocalCandidate(RTCIceCandidate candidate)
	{
		if (candidate != null)
		{
			_ = _signal("ice", candidate.toJSON());
		}
	}

	private void OnConnectionState(RTCPeerConnectionState state)
	{
		if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
		{
			Close();
		}
	}

	private void Close()
	{
		if (!_closed)
		{
			_closed = true;
			this.Closed?.Invoke();
		}
	}

	public void Dispose()
	{
		_closed = true;
		_connection.onicecandidate -= OnLocalCandidate;
		_connection.onconnectionstatechange -= OnConnectionState;
		try
		{
			_connection.close();
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException))
		{
		}
	}
}
