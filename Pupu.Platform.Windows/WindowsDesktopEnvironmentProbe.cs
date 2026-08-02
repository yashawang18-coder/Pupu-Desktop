using Pupu.Application;
using Pupu.Desktop.Services;

namespace Pupu.Platform.Windows;

public sealed class WindowsDesktopEnvironmentProbe : IDesktopEnvironmentProbe
{
    public bool IsForegroundApplicationFullScreen() =>
        EnvironmentContextService.IsForegroundApplicationFullScreen();
}
