namespace MauiWebRtc;

/// <summary>
/// Запрашивает разрешение CAMERA перед стартом Camera2.
/// Вызывать перед StartAsync() в Camera2FrameProvider.
/// </summary>
public static class CameraPermissionHelper
{
    public static async Task<bool> RequestCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

        if (status == PermissionStatus.Granted)
            return true;

        status = await Permissions.RequestAsync<Permissions.Camera>();
        return status == PermissionStatus.Granted;
    }
}