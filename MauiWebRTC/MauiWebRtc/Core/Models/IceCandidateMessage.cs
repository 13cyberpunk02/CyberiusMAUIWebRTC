namespace MauiWebRtc.Core.Models;

// ── Signaling сообщения ───────────────────────────────────────────────────────

public sealed record IceCandidateMessage(
    string TargetId,
    string Candidate,
    string SdpMid,
    int SdpMLineIndex
);