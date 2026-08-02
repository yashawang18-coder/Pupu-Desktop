namespace Pupu.Desktop.Services;

public sealed record DesktopRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);
}

public sealed record WindowSurfaceSnapshot(
    nint Handle,
    DesktopRect Bounds,
    DesktopRect MonitorWorkArea,
    string ProcessName,
    bool IsBrowser,
    bool IsForeground,
    DateTimeOffset CapturedAt)
{
    public double UsableLeft => Bounds.Left + 34;
    public double UsableRight => Bounds.Right - 34;
    public double TopEdge => Bounds.Top;
}

public sealed record DesktopEnvironmentSnapshot(
    DesktopRect CurrentMonitorWorkArea,
    WindowSurfaceSnapshot? PreferredSurface,
    int MonitorCount,
    DateTimeOffset CapturedAt)
{
    public static DesktopEnvironmentSnapshot Empty { get; } = new(
        new DesktopRect(0, 0, 1920, 1080),
        null,
        1,
        DateTimeOffset.MinValue);
}
