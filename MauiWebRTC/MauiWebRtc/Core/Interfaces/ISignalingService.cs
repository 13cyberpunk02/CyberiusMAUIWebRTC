namespace MauiWebRtc.Core.Interfaces;


/// <summary>
/// Отвечает за обмен SDP и ICE кандидатами через SignalR.
/// Не знает ничего про WebRTC — только транспорт сигналов.
/// </summary>
public interface ISignalingService
{
    // ── Состояние ────────────────────────────────────────────────
    bool IsConnected { get; }
    string? ConnectionId { get; }

    // ── Подключение ───────────────────────────────────────────────
    Task ConnectAsync(string url, CancellationToken ct = default);
    Task DisconnectAsync();

    // ── Исходящие сигналы ─────────────────────────────────────────
    Task SendOfferAsync(string targetId, string sdp, CancellationToken ct = default);
    Task SendAnswerAsync(string targetId, string sdp, CancellationToken ct = default);
    Task SendIceCandidateAsync(string targetId, string candidate, string sdpMid, int sdpMLineIndex, CancellationToken ct = default);
    Task JoinRoomAsync(string roomId, CancellationToken ct = default);
    Task LeaveRoomAsync(string roomId, CancellationToken ct = default);

    // ── Входящие сигналы (события) ────────────────────────────────
    event Func<string, string, Task>? OnOfferReceived;
    event Func<string, string, Task>? OnAnswerReceived;
    event Func<string, string, string, int, Task>? OnIceCandidateReceived;
    event Func<string, Task>? OnPeerJoined;
    event Func<string, Task>? OnPeerLeft;

    // ── Lifecycle ─────────────────────────────────────────────────
    event Action<Exception?>? OnDisconnected;
    event Action? OnReconnected;
}