using AVFoundation;
using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;
using Microsoft.Maui.Handlers;
using UIKit;

namespace MauiWebRtc.Handlers;

public partial class CameraPreviewHandler : ViewHandler<CameraPreviewView, UIView>
{
    private AVCaptureVideoPreviewLayer? _previewLayer;

    protected override UIView CreatePlatformView() => new();

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.FacingChanged += OnFacingChanged;
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        VirtualView.FacingChanged -= OnFacingChanged;
        _previewLayer?.RemoveFromSuperLayer();
        _previewLayer = null;
        base.DisconnectHandler(platformView);
    }

    // Вызывается снаружи когда AVCaptureSession готова
    public void AttachSession(AVCaptureSession session)
    {
        _previewLayer?.RemoveFromSuperLayer();
        _previewLayer = new AVCaptureVideoPreviewLayer(session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = PlatformView.Bounds
        };
        PlatformView.Layer.AddSublayer(_previewLayer);
    }

    private void OnFacingChanged(CameraFacing facing) => UpdateFacing(facing);

    partial void UpdateFacing(CameraFacing facing) { }
}