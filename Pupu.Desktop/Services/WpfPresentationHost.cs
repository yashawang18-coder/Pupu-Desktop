using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pupu.Application;

namespace Pupu.Desktop.Services;

public sealed class WpfPresentationHost : IDesktopPresentationHost
{
    private ActionPreviewWindow? _previewWindow;

    public IUiTimer CreateTimer(TimeSpan interval) => new WpfUiTimer(interval);

    public object? CropImage(object? source, int x, int y, int width, int height)
    {
        if (source is not BitmapSource bitmap ||
            x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x + width > bitmap.PixelWidth || y + height > bitmap.PixelHeight)
            return null;
        var frame = new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
        frame.Freeze();
        return frame;
    }

    public object? LoadImage(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.DecodePixelWidth = Math.Max(1, decodePixelWidth);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void ShowActionPreview(
        string title,
        IReadOnlyList<object> frames,
        IReadOnlyList<int> frameDurations,
        bool loop)
    {
        _previewWindow?.Close();
        var imageFrames = frames.OfType<ImageSource>().ToList();
        _previewWindow = new ActionPreviewWindow(
            title,
            loop ? "独立循环预览；不会改变当前行为或冷却。" : "独立单次预览；不会改变当前行为或冷却。",
            imageFrames,
            frameDurations);
        var control = Application.Current.Windows
            .OfType<ControlWindow>()
            .FirstOrDefault(window => window.IsVisible);
        if (control is not null) _previewWindow.Owner = control;
        _previewWindow.Closed += (_, _) => _previewWindow = null;
        _previewWindow.Show();
        _previewWindow.Activate();
    }

    public void ReportRecoverableException(Exception exception, string context) =>
        App.ReportRecoverableException(exception, context);

    public void Shutdown() => Application.Current.Shutdown();

    private sealed class WpfUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfUiTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public event EventHandler? Tick;

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() => _timer.Stop();
    }
}
