using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace MauiWebRtc.Core;

/// <summary>
/// Реализует полный WebRTC handshake:
///   Caller:  StartLocal → Call → createOffer → setLocal → sendOffer
///                              ← receiveAnswer → setRemote
///   Callee:  StartLocal → (incoming) → Answer → createAnswer → setLocal → sendAnswer
///
/// Зависит от ISignalingService (транспорт) и ICameraFrameProvider (платформа).
/// </summary>
public sealed class WebRtcClient : IWebRtcClient
{
    private readonly ISignalingService _signaling;
    private readonly ICameraFrameProvider _camera;
    private readonly ILogger<WebRtcClient> _logger;
    private readonly WebRtcOptions _options;

    private RTCPeerConnection? _pc;
    private string? _pendingCallerId; // храним id входящего звонка до Answer()
    private int _disposed;

    // ── IWebRtcClient: состояние ──────────────────────────────────
    public CallState State { get; private set; } = CallState.Idle;
    public bool IsMicrophoneMuted { get; private set; }
    public bool IsCameraMuted { get; private set; }

    // ── IWebRtcClient: события ────────────────────────────────────
    public event Action<RawVideoFrame>? OnRemoteFrameReceived;
    public event Action<string>? OnIncomingCall;
    public event Action<CallState>? OnCallStateChanged;

    public WebRtcClient(
        ISignalingService signaling,
        ICameraFrameProvider camera,
        ILogger<WebRtcClient> logger,
        WebRtcOptions options)
    {
        _signaling = signaling;
        _camera = camera;
        _logger = logger;
        _options = options;

        // Подписываемся на входящие сигналы
        _signaling.OnOfferReceived += HandleOfferAsync;
        _signaling.OnAnswerReceived += HandleAnswerAsync;
        _signaling.OnIceCandidateReceived += HandleIceCandidateAsync;
    }

    // ── Управление локальной камерой ──────────────────────────────

    public async Task StartLocalCameraAsync(CancellationToken ct = default)
    {
        _camera.OnFrameAvailable += OnLocalFrameAvailable;
        await _camera.StartAsync(CameraFacing.Front, ct);
        _logger.LogInformation("Local camera started");
    }

    public async Task StopLocalCameraAsync()
    {
        _camera.OnFrameAvailable -= OnLocalFrameAvailable;
        await _camera.StopAsync();
    }

    // ── Caller Flow ───────────────────────────────────────────────

    public async Task CallAsync(string targetUserId, CancellationToken ct = default)
    {
        EnsureState(CallState.Idle);

        _pc = CreatePeerConnection();
        AddAudioVideoTracks(_pc);

        var offer = _pc.createOffer();
        await _pc.setLocalDescription(offer);

        SetState(CallState.Calling);
        _logger.LogInformation("Sending offer to {Target}", targetUserId);

        await _signaling.SendOfferAsync(targetUserId, offer.sdp, ct);
    }

    // ── Callee Flow ───────────────────────────────────────────────

    private async Task HandleOfferAsync(string callerId, string sdp)
    {
        _logger.LogInformation("Incoming call from {CallerId}", callerId);
        _pendingCallerId = callerId;

        // Создаём PC заранее, чтобы можно было накапливать ICE кандидаты
        _pc = CreatePeerConnection();
        AddAudioVideoTracks(_pc);

        var remoteDesc = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdp
        };
        _pc.setRemoteDescription(remoteDesc);

        SetState(CallState.Receiving);
        OnIncomingCall?.Invoke(callerId); // UI показывает Accept/Decline
    }

    public async Task AnswerAsync(CancellationToken ct = default)
    {
        EnsureState(CallState.Receiving);
        if (_pc is null || _pendingCallerId is null)
            throw new InvalidOperationException("Нет входящего звонка для ответа");

        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);

        SetState(CallState.Connecting);
        _logger.LogInformation("Sending answer to {CallerId}", _pendingCallerId);

        await _signaling.SendAnswerAsync(_pendingCallerId, answer.sdp, ct);
        _pendingCallerId = null;
    }

    private async Task HandleAnswerAsync(string callerId, string sdp)
    {
        if (_pc is null) return;

        _logger.LogDebug("Received answer from {CallerId}", callerId);

        var remoteDesc = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = sdp
        };
        _pc.setRemoteDescription(remoteDesc);

        SetState(CallState.Connecting);
        // Дальше ICE negotiation сделает всё сам — см. OnConnectionStateChange
    }

    // ── ICE Candidates ────────────────────────────────────────────

    private async Task HandleIceCandidateAsync(string callerId, string candidate, string sdpMid, int sdpMLineIndex)
    {
        if (_pc is null) return;

        _pc.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = (ushort)sdpMLineIndex
        });
    }

    // ── HangUp ────────────────────────────────────────────────────

    public async Task HangUpAsync()
    {
        await StopLocalCameraAsync();
        DisposePeerConnection();
        SetState(CallState.Ended);
        SetState(CallState.Idle); // сразу сбрасываем в Idle
    }

    // ── Управление медиа ─────────────────────────────────────────

    public void MuteMicrophone(bool mute)
    {
        IsMicrophoneMuted = mute;
        // SIPSorcery: аудио управляется через локальный аудиотрек напрямую
        // Когда мут — просто прекращаем отправлять аудио сэмплы
        // Полная реализация будет в Фазе 5 при подключении нативного микрофона
    }

    public void MuteCamera(bool mute)
    {
        IsCameraMuted = mute;
        // Когда мут — OnLocalFrameAvailable будет пропускать фреймы
    }

    public Task SwitchCameraAsync() => _camera.SwitchCameraAsync();

    // ── Фреймы с камеры → SIPSorcery ─────────────────────────────

    private void OnLocalFrameAvailable(RawVideoFrame frame)
    {
        if (IsCameraMuted || _pc is null) return;

        // SIPSorcery ждёт I420 через ExternalVideoSourceRawSample
        // Duration в мс, здесь приблизительно 30fps → 33ms
        _pc.SendVideo(33, frame.Data);
    }

    // ── Создание PeerConnection ───────────────────────────────────

    private RTCPeerConnection CreatePeerConnection()
    {
        var config = new RTCConfiguration
        {
            iceServers =
            [
                new RTCIceServer { urls = _options.StunServer },
                new RTCIceServer
                {
                    urls = _options.TurnServer,
                    username = _options.TurnUsername,
                    credential = _options.TurnPassword
                }
            ]
        };

        var pc = new RTCPeerConnection(config);

        // ICE candidate → отправляем на другой конец
        pc.onicecandidate += async (candidate) =>
        {
            if (candidate is null) return; // null = gathering complete
            if (_pendingCallerId is null && State is CallState.Calling or CallState.Connecting)
            {
                // targetId нужно хранить — добавим поле _remoteId
                _logger.LogDebug("ICE candidate ready to send");
                // TODO: отправить кандидата через signaling (нужен _remoteId, см. ниже)
            }
        };

        // Отслеживаем состояние ICE соединения
        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogInformation("PeerConnection state: {State}", state);
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    SetState(CallState.Connected);
                    break;
                case RTCPeerConnectionState.failed:
                case RTCPeerConnectionState.disconnected:
                    _ = HangUpAsync();
                    break;
            }
        };

        // Входящее видео с удалённого пира
        pc.OnVideoFrameReceived += (ep, ts, data, format) =>
        {
            var frame = new RawVideoFrame
            {
                Data = data,
                Width = 0,   // SIPSorcery передаёт размер через format
                Height = 0,
                Format = PixelFormat.I420
            };
            OnRemoteFrameReceived?.Invoke(frame);
        };

        return pc;
    }

    private static void AddAudioVideoTracks(RTCPeerConnection pc)
    {
        // VP8 payload type 96 — стандартный dynamic payload для VP8 в WebRTC
        // SDPWellKnownMediaFormatsEnum не содержит видео форматов,
        // видео создаётся через VideoFormat напрямую
        var videoTrack = new MediaStreamTrack(
            new List<VideoFormat>
            {
                new (new VideoFormat(VideoCodecsEnum.VP8, 96))
            });
        pc.addTrack(videoTrack);

        // PCMU (G.711) — есть в SDPWellKnownMediaFormatsEnum, payload type 0
        var audioTrack = new MediaStreamTrack(
            new List<AudioFormat>
            {
                new (SDPWellKnownMediaFormatsEnum.PCMU)
            });
        pc.addTrack(audioTrack);
    }

    // ── Хелперы ───────────────────────────────────────────────────

    private void SetState(CallState state)
    {
        State = state;
        OnCallStateChanged?.Invoke(state);
    }

    private void EnsureState(CallState expected)
    {
        if (State != expected)
            throw new InvalidOperationException($"Ожидалось состояние {expected}, текущее: {State}");
    }

    private void DisposePeerConnection()
    {
        _pc?.close();
        _pc?.Dispose();
        _pc = null;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _signaling.OnOfferReceived -= HandleOfferAsync;
        _signaling.OnAnswerReceived -= HandleAnswerAsync;
        _signaling.OnIceCandidateReceived -= HandleIceCandidateAsync;

        await StopLocalCameraAsync();
        DisposePeerConnection();
    }
}