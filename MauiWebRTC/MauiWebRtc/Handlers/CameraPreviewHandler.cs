using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;

namespace MauiWebRtc.Handlers;

/// <summary>
/// Базовый класс — только маппер свойств.
/// Нативный тип и CreatePlatformView реализованы в partial классах:
///   Platforms/Android/Handlers/CameraPreviewHandler.cs
///   Platforms/iOS/Handlers/CameraPreviewHandler.cs
///   Platforms/Windows/Handlers/CameraPreviewHandler.cs
/// </summary>
public partial class CameraPreviewHandler
{
    public static PropertyMapper<CameraPreviewView, CameraPreviewHandler> Mapper = new(ViewMapper)
    {
        [nameof(CameraPreviewView.Facing)]    = MapFacing,
        [nameof(CameraPreviewView.IsRunning)] = MapIsRunning,
    };

    public CameraPreviewHandler() : base(Mapper) { }

    private static void MapFacing(CameraPreviewHandler handler, CameraPreviewView view)
        => handler.UpdateFacing(view.Facing);

    private static void MapIsRunning(CameraPreviewHandler handler, CameraPreviewView view) { }

    // partial методы — реализуются на каждой платформе
    partial void UpdateFacing(CameraFacing facing);
}