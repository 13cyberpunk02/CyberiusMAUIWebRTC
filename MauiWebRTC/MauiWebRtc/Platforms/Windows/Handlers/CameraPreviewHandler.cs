using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;
using Border = Microsoft.UI.Xaml.Controls.Border;

namespace MauiWebRtc.Handlers;

// Windows не имеет отдельного Preview View — MediaCapture управляется через
// ICameraFrameProvider, а превью показываем через обычный Border
public partial class CameraPreviewHandler : ViewHandler<CameraPreviewView, Border>
{
    protected override Border CreatePlatformView() => new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) };

    protected override void ConnectHandler(Border platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.FacingChanged += OnFacingChanged;
    }

    protected override void DisconnectHandler(Border platformView)
    {
        VirtualView.FacingChanged -= OnFacingChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnFacingChanged(CameraFacing facing) => UpdateFacing(facing);

    partial void UpdateFacing(CameraFacing facing) { }
}