using MauiWebRtc.Core.Models;

namespace MauiWebRtc.Core.Interfaces;

/// <summary>
/// Главный фасад для работы с WebRTC.
/// ViewModel взаимодействует только с этим интерфейсом.
/// </summary>
public interface IWebRtcClient : IAsyncDisposable
{
    // ── Состояние ─────────────────────────────────────────────────
    CallState State { get; }
    bool IsMicrophoneMuted { get; }
    bool IsCameraMuted { get; }

    // ── Управление локальной камерой ──────────────────────────────
    Task StartLocalCameraAsync(CancellationToken ct = default);
    Task StopLocalCameraAsync();

    // ── Управление звонком ────────────────────────────────────────
    Task CallAsync(string targetUserId, CancellationToken ct = default);
    Task AnswerAsync(CancellationToken ct = default);
    Task HangUpAsync();

    // ── Управление медиа во время звонка ─────────────────────────
    void MuteMicrophone(bool mute);
    void MuteCamera(bool mute);
    Task SwitchCameraAsync();

    // ── События ───────────────────────────────────────────────────

    /// <summary>Приходит удалённый видеофрейм — нужно отрисовать</summary>
    event Action<RawVideoFrame>? OnRemoteFrameReceived;

    /// <summary>Входящий звонок — показать UI с Accept/Decline</summary>
    event Action<string>? OnIncomingCall;

    /// <summary>Изменение состояния звонка</summary>
    event Action<CallState>? OnCallStateChanged;
}