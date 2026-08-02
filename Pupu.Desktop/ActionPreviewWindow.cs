using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pupu.Desktop;

/// <summary>
/// Isolated asset preview. It never changes the live pet behavior, dwell,
/// cooldown, runtime state or memory.
/// </summary>
public sealed class ActionPreviewWindow : Window
{
    private readonly IReadOnlyList<ImageSource> _frames;
    private readonly IReadOnlyList<int> _durations;
    private readonly Image _image;
    private readonly TextBlock _frameLabel;
    private readonly DispatcherTimer _timer;
    private int _position;

    public ActionPreviewWindow(
        string title,
        string description,
        IReadOnlyList<ImageSource> frames,
        IReadOnlyList<int> durations)
    {
        _frames = frames;
        _durations = durations;
        Title = $"素材预览 · {title}";
        Width = 430;
        Height = 500;
        MinWidth = 360;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Topmost = false;
        Background = new SolidColorBrush(Color.FromRgb(244, 248, 248));

        _image = new Image
        {
            Width = 300,
            Height = 300,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Source = _frames.FirstOrDefault()
        };
        _frameLabel = new TextBlock
        {
            Text = FrameText(),
            Foreground = new SolidColorBrush(Color.FromRgb(92, 112, 114)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var close = new Button
        {
            Content = "关闭预览",
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => Close();

        Content = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 231, 229)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontFamily = new FontFamily("Microsoft YaHei UI"),
                        FontSize = 19,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = description,
                        FontFamily = new FontFamily("Microsoft YaHei UI"),
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(92, 112, 114)),
                        Margin = new Thickness(0, 6, 0, 10)
                    },
                    _image,
                    _frameLabel,
                    close
                }
            }
        };

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Advance();
        Closed += (_, _) => _timer.Stop();
        if (_frames.Count > 1)
        {
            SetInterval();
            _timer.Start();
        }
    }

    private void Advance()
    {
        if (_frames.Count == 0) return;
        _position = (_position + 1) % _frames.Count;
        _image.Source = _frames[_position];
        _frameLabel.Text = FrameText();
        SetInterval();
    }

    private void SetInterval()
    {
        var duration = _durations.Count == 0
            ? 600
            : _durations[Math.Min(_position, _durations.Count - 1)];
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(duration, 80, 2500));
    }

    private string FrameText() =>
        _frames.Count == 0
            ? "没有可预览帧"
            : $"帧 {_position + 1} / {_frames.Count}";
}
