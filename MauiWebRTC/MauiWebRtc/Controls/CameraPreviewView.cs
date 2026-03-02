using MauiWebRtc.Core.Models;

namespace MauiWebRtc.Controls;

/// <summary>
/// Показывает превью локальной камеры.
/// Маппится на нативный View через CameraPreviewHandler.
///
/// Использование в XAML:
///   <webrtc:CameraPreviewView Facing="Front" />
/// </summary>
public class CameraPreviewView : View
{
    // ── Bindable Properties ───────────────────────────────────────

    public static readonly BindableProperty FacingProperty = BindableProperty.Create(
        nameof(Facing),
        typeof(CameraFacing),
        typeof(CameraPreviewView),
        CameraFacing.Front,
        propertyChanged: (bindable, _, newValue) =>
            ((CameraPreviewView)bindable).FacingChanged?.Invoke((CameraFacing)newValue));

    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning),
        typeof(bool),
        typeof(CameraPreviewView),
        false);

    public CameraFacing Facing
    {
        get => (CameraFacing)GetValue(FacingProperty);
        set => SetValue(FacingProperty, value);
    }

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    // ── Events (для Handler) ──────────────────────────────────────

    /// <summary>Handler подписывается сюда чтобы реагировать на смену камеры</summary>
    internal event Action<CameraFacing>? FacingChanged;

    public async Task StartAsync()
    {
        IsRunning = true;
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        IsRunning = false;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    internal event EventHandler? StartRequested;
    internal event EventHandler? StopRequested;
}