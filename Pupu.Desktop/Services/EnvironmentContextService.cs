using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pupu.Desktop.Services;

public static class EnvironmentContextService
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "waterfox"
    };

    public static DesktopEnvironmentSnapshot Capture(nint petWindow)
    {
        if (!OperatingSystem.IsWindows()) return DesktopEnvironmentSnapshot.Empty;
        try
        {
            var foreground = GetForegroundWindow();
            var petMonitor = MonitorFromWindow(petWindow, MonitorDefaultToNearest);
            var currentWork = GetMonitorWorkAreaDip(petMonitor);
            var candidates = new List<(WindowSurfaceSnapshot Surface, double Score)>();
            EnumWindows((window, _) =>
            {
                if (!IsCandidateWindow(window, petWindow, out var rect, out var processName))
                    return true;
                var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
                if (monitor == IntPtr.Zero) return true;
                var monitorWork = GetMonitorWorkAreaDip(monitor);
                var bounds = ToDip(rect, DpiForWindow(window));
                if (bounds.Top < monitorWork.Top + 72 ||
                    bounds.Top > monitorWork.Bottom - 190 ||
                    bounds.Width < 380 ||
                    bounds.Height < 170)
                    return true;
                var isBrowser = BrowserProcesses.Contains(processName);
                var sameMonitor = monitor == petMonitor;
                var isForeground = window == foreground;
                var surface = new WindowSurfaceSnapshot(
                    window,
                    bounds,
                    monitorWork,
                    processName,
                    isBrowser,
                    isForeground,
                    DateTimeOffset.Now);
                var score = (sameMonitor ? 600 : 0)
                            + (isBrowser ? 360 : 0)
                            + (isForeground ? 280 : 0)
                            + Math.Min(220, bounds.Width / 8)
                            - Math.Abs(bounds.Top - (monitorWork.Top + monitorWork.Height * 0.46)) / 8;
                candidates.Add((surface, score));
                return true;
            }, IntPtr.Zero);

            var preferred = candidates
                .OrderByDescending(x => x.Score)
                .Select(x => x.Surface)
                .FirstOrDefault();
            return new DesktopEnvironmentSnapshot(
                currentWork,
                preferred,
                CountMonitors(),
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return DesktopEnvironmentSnapshot.Empty;
        }
    }

    public static WindowSurfaceSnapshot? RefreshSurface(nint handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero ||
            !IsCandidateWindow(handle, IntPtr.Zero, out var rect, out var processName))
            return null;
        var foreground = GetForegroundWindow();
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return null;
        var bounds = ToDip(rect, DpiForWindow(handle));
        return new WindowSurfaceSnapshot(
            handle,
            bounds,
            GetMonitorWorkAreaDip(monitor),
            processName,
            BrowserProcesses.Contains(processName),
            handle == foreground,
            DateTimeOffset.Now);
    }

    public static DesktopRect GetCurrentMonitorWorkArea(nint window)
    {
        if (!OperatingSystem.IsWindows()) return DesktopEnvironmentSnapshot.Empty.CurrentMonitorWorkArea;
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        return monitor == IntPtr.Zero
            ? DesktopEnvironmentSnapshot.Empty.CurrentMonitorWorkArea
            : GetMonitorWorkAreaDip(monitor);
    }

    public static bool IsForegroundApplicationFullScreen()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            GetWindowThreadProcessId(window, out var processId);
            if (processId == (uint)Environment.ProcessId) return false;
            if (!GetWindowRect(window, out var windowRect)) return false;
            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;
            var info = MonitorInfo.Create();
            if (!GetMonitorInfo(monitor, ref info)) return false;
            const int tolerance = 2;
            return Math.Abs(windowRect.Left - info.Monitor.Left) <= tolerance
                   && Math.Abs(windowRect.Top - info.Monitor.Top) <= tolerance
                   && Math.Abs(windowRect.Right - info.Monitor.Right) <= tolerance
                   && Math.Abs(windowRect.Bottom - info.Monitor.Bottom) <= tolerance;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
    }

    private static bool IsCandidateWindow(
        nint window,
        nint petWindow,
        out NativeRect rectangle,
        out string processName)
    {
        rectangle = default;
        processName = string.Empty;
        if (window == IntPtr.Zero || window == petWindow || !IsWindowVisible(window) || IsIconic(window))
            return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId) return false;
        var exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        if ((exStyle & WsExToolWindow) != 0) return false;
        if (!GetWindowRect(window, out rectangle)) return false;
        if (rectangle.Right - rectangle.Left < 360 || rectangle.Bottom - rectangle.Top < 160)
            return false;
        var titleLength = GetWindowTextLength(window);
        if (titleLength <= 0) return false;
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return false;
        }
        return true;
    }

    private static DesktopRect GetMonitorWorkAreaDip(nint monitor)
    {
        var info = MonitorInfo.Create();
        if (!GetMonitorInfo(monitor, ref info))
            return DesktopEnvironmentSnapshot.Empty.CurrentMonitorWorkArea;
        var dpi = DpiForMonitor(monitor);
        return ToDip(info.Work, dpi);
    }

    private static DesktopRect ToDip(NativeRect rect, uint dpi)
    {
        var scale = Math.Max(0.5, dpi / 96d);
        return new DesktopRect(
            rect.Left / scale,
            rect.Top / scale,
            rect.Right / scale,
            rect.Bottom / scale);
    }

    private static uint DpiForWindow(nint window)
    {
        try
        {
            var dpi = GetDpiForWindow(window);
            return dpi == 0 ? 96u : dpi;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
    }

    private static uint DpiForMonitor(nint monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX
                : 96;
        }
        catch (DllNotFoundException)
        {
            return 96;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
    }

    private static int CountMonitors()
    {
        var count = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (_, _, _, _) =>
        {
            count++;
            return true;
        }, IntPtr.Zero);
        return Math.Max(1, count);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr window, int index);

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : GetWindowLong32(window, index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;

        public static MonitorInfo Create() => new() { Size = Marshal.SizeOf<MonitorInfo>() };
    }
}
