using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pupu.Desktop;

/// <summary>
/// A short-lived click surface used only while the owner explicitly places a
/// food or toy anchor. It is not a global mouse hook and closes after one click.
/// </summary>
public sealed class AnchorPlacementWindow : Window
{
    public AnchorPlacementWindow(Rect workArea, string instruction)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(28, 49, 134, 138));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
        Cursor = Cursors.Cross;

        Content = new Grid
        {
            Children =
            {
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 28, 0, 0),
                    Padding = new Thickness(16, 9, 16, 9),
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(238, 255, 253, 248)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(49, 134, 138)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = instruction + "（Esc 或右键取消）",
                        FontFamily = new FontFamily("Microsoft YaHei UI"),
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(45, 58, 61))
                    }
                }
            }
        };

        PreviewMouseLeftButtonDown += OnLeftButtonDown;
        PreviewMouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
        };
    }

    public event Action<Point>? AnchorSelected;
    public event EventHandler? Cancelled;

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var local = e.GetPosition(this);
        e.Handled = true;
        AnchorSelected?.Invoke(new Point(Left + local.X, Top + local.Y));
        Close();
    }
}
