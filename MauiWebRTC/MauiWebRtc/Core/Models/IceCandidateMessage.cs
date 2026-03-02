namespace MauiWebRtc.Core.Models;

public sealed record IceCandidateMessage(
    string TargetId,
    string Candidate,
    string SdpMid,
    int SdpMLineIndex
);