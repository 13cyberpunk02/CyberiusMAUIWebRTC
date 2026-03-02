using Android.Views;
using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;
using Microsoft.Maui.Handlers;

namespace MauiWebRtc.Handlers;

public partial class CameraPreviewHandler : ViewHandler<CameraPreviewView, SurfaceView>
{
    protected override SurfaceView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(SurfaceView platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.FacingChanged += OnFacingChanged;
    }

    protected override void DisconnectHandler(SurfaceView platformView)
    {
        VirtualView.FacingChanged -= OnFacingChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnFacingChanged(CameraFacing facing) => UpdateFacing(facing);

    partial void UpdateFacing(CameraFacing facing)
    {
        // ICameraFrameProvider.SwitchCameraAsync() вызывается через IWebRtcClient
        // Handler только отображает превью — Camera2 сам рисует в Surface
    }
}