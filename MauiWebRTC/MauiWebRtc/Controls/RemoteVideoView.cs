namespace MauiWebRtc.Controls;

/// <summary>
/// Показывает входящее видео удалённого участника.
/// Маппится на нативный View через RemoteVideoHandler.
///
/// Использование в XAML:
///   <webrtc:RemoteVideoView IsMirrored="False" />
/// </summary>
public class RemoteVideoView : View
{
    public static readonly BindableProperty IsMirroredProperty = BindableProperty.Create(
        nameof(IsMirrored),
        typeof(bool),
        typeof(RemoteVideoView),
        false);

    public bool IsMirrored
    {
        get => (bool)GetValue(IsMirroredProperty);
        set => SetValue(IsMirroredProperty, value);
    }

    /// <summary>
    /// Вызывается из WebRtcClient когда приходит удалённый фрейм.
    /// Handler подписывается на это событие и рисует фрейм на нативном View.
    /// </summary>
    internal event Action<Core.Models.RawVideoFrame>? FrameReceived;

    internal void PushFrame(Core.Models.RawVideoFrame frame) => FrameReceived?.Invoke(frame);
}