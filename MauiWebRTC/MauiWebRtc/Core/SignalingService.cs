using MauiWebRtc.Core.Interfaces;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace MauiWebRtc.Core;

/// <summary>
/// SignalR-реализация сигналинга.
/// Отвечает ТОЛЬКО за транспорт — пересылку SDP и ICE между пирами.
/// Логика WebRTC — в WebRtcClient.
/// </summary>
public sealed class SignalingService(ILogger<SignalingService> logger) : ISignalingService
{
    private HubConnection? _hub;

    // ── ISignalingService: состояние ──────────────────────────────
    public bool IsConnected => _hub?.State == HubConnectionState.Connected;
    public string? ConnectionId => _hub?.ConnectionId;

    // ── ISignalingService: события ────────────────────────────────
    public event Func<string, string, Task>? OnOfferReceived;
    public event Func<string, string, Task>? OnAnswerReceived;
    public event Func<string, string, string, int, Task>? OnIceCandidateReceived;
    public event Func<string, Task>? OnPeerJoined;
    public event Func<string, Task>? OnPeerLeft;
    public event Action<Exception?>? OnDisconnected;
    public event Action? OnReconnected;

    // ── Подключение ───────────────────────────────────────────────

    public async Task ConnectAsync(string url, CancellationToken ct = default)
    {
        if (_hub != null)
            await DisconnectAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect(new RetryPolicy())
            .ConfigureLogging(logging => logging.AddDebug())
            .Build();

        RegisterHandlers();
        SubscribeToLifecycle();

        await _hub.StartAsync(ct);
        logger.LogInformation("SignalR connected. ConnectionId={Id}", _hub.ConnectionId);
    }

    public async Task DisconnectAsync()
    {
        if (_hub is null) return;
        await _hub.StopAsync();
        await _hub.DisposeAsync();
        _hub = null;
    }

    // ── Исходящие сигналы ─────────────────────────────────────────

    public Task SendOfferAsync(string targetId, string sdp, CancellationToken ct = default)
        => InvokeAsync("SendOffer", ct, targetId, sdp);

    public Task SendAnswerAsync(string targetId, string sdp, CancellationToken ct = default)
        => InvokeAsync("SendAnswer", ct, targetId, sdp);

    public Task SendIceCandidateAsync(string targetId, string candidate, string sdpMid, int sdpMLineIndex, CancellationToken ct = default)
        => InvokeAsync("SendIceCandidate", ct, targetId, candidate, sdpMid, sdpMLineIndex);

    public Task JoinRoomAsync(string roomId, CancellationToken ct = default)
        => InvokeAsync("JoinRoom", ct, roomId);

    public Task LeaveRoomAsync(string roomId, CancellationToken ct = default)
        => InvokeAsync("LeaveRoom", ct, roomId);

    // ── Приватные хелперы ─────────────────────────────────────────

    private Task InvokeAsync(string method, CancellationToken ct, params object[] args)
    {
        EnsureConnected();
        return _hub!.InvokeCoreAsync(method, args, ct);
    }

    private void EnsureConnected()
    {
        if (_hub is null || !IsConnected)
            throw new InvalidOperationException("SignalR не подключён. Вызовите ConnectAsync() первым.");
    }

    private void RegisterHandlers()
    {
        if(_hub is null) return;
        // Hub вызывает эти методы на клиенте
        _hub.On<string, string>("ReceiveOffer", async (callerId, sdp) =>
        {
            logger.LogDebug("ReceiveOffer from {CallerId}", callerId);
            if (OnOfferReceived is not null)
                await OnOfferReceived(callerId, sdp);
        });

        _hub.On<string, string>("ReceiveAnswer", async (callerId, sdp) =>
        {
            logger.LogDebug("ReceiveAnswer from {CallerId}", callerId);
            if (OnAnswerReceived is not null)
                await OnAnswerReceived(callerId, sdp);
        });

        _hub.On<string, string, string, int>("ReceiveIceCandidate", async (callerId, candidate, sdpMid, sdpMLineIndex) =>
        {
            logger.LogDebug("ReceiveIceCandidate from {CallerId}", callerId);
            if (OnIceCandidateReceived is not null)
                await OnIceCandidateReceived(callerId, candidate, sdpMid, sdpMLineIndex);
        });

        _hub.On<string>("PeerJoined", async (peerId) =>
        {
            logger.LogInformation("Peer joined: {PeerId}", peerId);
            if (OnPeerJoined is not null)
                await OnPeerJoined(peerId);
        });

        _hub.On<string>("PeerLeft", async (peerId) =>
        {
            logger.LogInformation("Peer left: {PeerId}", peerId);
            if (OnPeerLeft is not null)
                await OnPeerLeft(peerId);
        });
    }

    private void SubscribeToLifecycle()
    {
        _hub!.Closed += ex =>
        {
            logger.LogWarning(ex, "SignalR disconnected");
            OnDisconnected?.Invoke(ex);
            return Task.CompletedTask;
        };

        _hub.Reconnected += _ =>
        {
            logger.LogInformation("SignalR reconnected. New ConnectionId={Id}", _hub.ConnectionId);
            OnReconnected?.Invoke();
            return Task.CompletedTask;
        };
    }

    // ── Retry policy: 0s, 2s, 5s, 10s, 30s, потом каждые 30s ────

    private sealed class RetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
             TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

        public TimeSpan? NextRetryDelay(RetryContext ctx) =>
            ctx.PreviousRetryCount < Delays.Length
                ? Delays[ctx.PreviousRetryCount]
                : TimeSpan.FromSeconds(30);
    }
}