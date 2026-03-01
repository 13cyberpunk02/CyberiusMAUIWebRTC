namespace MauiWebRtc.Core;

/// <summary>
/// Конфигурация, которую разработчик передаёт через builder.UseMauiWebRtc(options => ...).
/// </summary>
public sealed class WebRtcOptions
{
    /// <summary>URL SignalR Hub, например: https://yourserver.com/videocall</summary>
    public required string SignalingUrl { get; set; }

    /// <summary>STUN сервер, например: stun:stun.l.google.com:19302</summary>
    public string StunServer { get; set; } = "stun:stun.l.google.com:19302";

    /// <summary>TURN сервер (нужен при симметричном NAT)</summary>
    public string? TurnServer { get; set; }

    public string? TurnUsername { get; set; }
    public string? TurnPassword { get; set; }

    /// <summary>Таймаут ICE gathering в секундах</summary>
    public int IceGatheringTimeoutSeconds { get; set; } = 10;
}