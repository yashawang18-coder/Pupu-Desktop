using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Pupu.Application;
using Pupu.Behavior;
using Pupu.Desktop.Models;
using Pupu.Desktop.Services;
using Pupu.Desktop.ViewModels;
using Pupu.Platform.Windows;

namespace Pupu.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ControlWindow? _controlWindow;
    private bool _walking;
    private bool _petPointerArmed;
    private bool _petPointerDragged;
    private bool _coinDoubleClickHandled;
    private Point _petPointerDown;
    private DateTimeOffset _petPointerDownAt;
    private readonly DispatcherTimer _environmentTimer;
    private readonly DispatcherTimer _cursorGazeTimer;
    private AnchorPlacementWindow? _anchorPlacementWindow;
    private nint _windowHandle;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new WpfPresentationHost(),
            AssetPackService.Load(),
            new ModelApiService(new PetSpeechComposer()),
            new CodexIterationService(),
            new WindowsDesktopEnvironmentProbe());
        DataContext = _viewModel;
        _viewModel.DesktopMoveRequested += ViewModel_DesktopMoveRequested;
        _viewModel.ControlPanelRequested += ViewModel_ControlPanelRequested;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _environmentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _environmentTimer.Tick += (_, _) => RefreshDesktopEnvironment();
        _cursorGazeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _cursorGazeTimer.Tick += (_, _) => RefreshCursorGaze();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        AnchorToDesktopEdge();
        RefreshDesktopEnvironment();
        _environmentTimer.Start();
        _cursorGazeTimer.Start();
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.TimeChanged += SystemEvents_TimeChanged;
    }

    private void AnchorToDesktopEdge()
    {
        var work = CurrentWorkArea();
        Left = Math.Clamp(work.Right - ActualWidth - 18, work.Left, work.Right - ActualWidth);
        Top = Math.Clamp(work.Bottom - ActualHeight - 6, work.Top, work.Bottom - ActualHeight);
    }

    private void ClampToDesktop()
    {
        var work = CurrentWorkArea();
        Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PetDisplaySize))
        {
            Dispatcher.BeginInvoke(ClampToDesktop, DispatcherPriority.Loaded);
            return;
        }
        if (e.PropertyName == nameof(MainViewModel.IsChatComposerVisible) &&
            _viewModel.IsChatComposerVisible)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ChatInputBox.Focus();
                Keyboard.Focus(ChatInputBox);
            }, DispatcherPriority.Input);
            return;
        }
        if (e.PropertyName == nameof(MainViewModel.MouseInteractionMode))
            UpdateAnchorPlacementWindow();
    }

    private void ViewModel_ControlPanelRequested(object? sender, EventArgs e) => OpenControlWindow();

    private void OpenControlWindow()
    {
        if (_controlWindow is { IsLoaded: true })
        {
            _controlWindow.Show();
            if (_controlWindow.WindowState == WindowState.Minimized)
                _controlWindow.WindowState = WindowState.Normal;
            _controlWindow.Activate();
            _controlWindow.Focus();
            return;
        }

        _controlWindow = new ControlWindow
        {
            DataContext = _viewModel
        };
        _controlWindow.Closed += (_, _) => _controlWindow = null;
        var work = CurrentWorkArea();
        _controlWindow.Left = Math.Clamp(Left + ActualWidth + 8, work.Left, work.Right - _controlWindow.Width);
        _controlWindow.Top = Math.Clamp(Top - 110, work.Top, work.Bottom - _controlWindow.Height);
        _controlWindow.Show();
    }

    private void UpdateAnchorPlacementWindow()
    {
        if (_viewModel.MouseInteractionMode is MouseInteractionMode.Attention)
        {
            _anchorPlacementWindow?.Close();
            _anchorPlacementWindow = null;
            return;
        }
        if (_anchorPlacementWindow is { IsLoaded: true })
            return;

        var work = CurrentWorkArea();
        _anchorPlacementWindow = new AnchorPlacementWindow(
            new Rect(work.Left, work.Top, work.Width, work.Height),
            _viewModel.MouseInteractionMode == MouseInteractionMode.FoodAnchor
                ? "点击桌面位置投掷冻干"
                : "点击桌面位置投放激光点");
        _anchorPlacementWindow.AnchorSelected += point =>
        {
            _anchorPlacementWindow = null;
            _ = _viewModel.PlaceActiveAnchorAsync(new DesktopPoint(point.X, point.Y));
        };
        _anchorPlacementWindow.Cancelled += (_, _) =>
        {
            _anchorPlacementWindow = null;
            if (_viewModel.CancelMouseModeCommand.CanExecute(null))
                _viewModel.CancelMouseModeCommand.Execute(null);
        };
        _anchorPlacementWindow.Closed += (_, _) =>
        {
            _anchorPlacementWindow = null;
            if (_viewModel.CancelMouseModeCommand.CanExecute(null))
                _viewModel.CancelMouseModeCommand.Execute(null);
        };
        _anchorPlacementWindow.Show();
        _anchorPlacementWindow.Activate();
        _anchorPlacementWindow.Focus();
    }

    private async void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_walking)
        {
            _petPointerDown = e.GetPosition(this);
            _petPointerDownAt = DateTimeOffset.Now;
            _petPointerArmed = true;
            _petPointerDragged = false;
            var local = e.GetPosition(sender as IInputElement);
            if (!_viewModel.IsPetrified)
                _viewModel.RegisterPointerDown(local.X, local.Y);
            if (sender is UIElement element) element.CaptureMouse();
            e.Handled = true;
            if (_viewModel.IsPetrified && e.ClickCount >= 2)
            {
                _coinDoubleClickHandled = true;
                await _viewModel.FlipPetrifiedCoinAsync();
            }
        }
    }

    private void ChatBlankArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (DataContext is MainViewModel viewModel && viewModel.ToggleChatComposerCommand.CanExecute(null))
            viewModel.ToggleChatComposerCommand.Execute(null);
        e.Handled = true;
    }

    private void PetImage_MouseMove(object sender, MouseEventArgs e)
    {
        var local = e.GetPosition(sender as IInputElement);
        _viewModel.RegisterMousePresence(local.X, local.Y);
        if (!_petPointerArmed || e.LeftButton != MouseButtonState.Pressed || _walking) return;
        var current = e.GetPosition(this);
        _viewModel.RegisterPointerMove(local.X, local.Y);
        var distance = Math.Sqrt(
            Math.Pow(current.X - _petPointerDown.X, 2) +
            Math.Pow(current.Y - _petPointerDown.Y, 2));
        if (distance < 14) return;
        if (_viewModel.IsMovementLocked) return;
        _petPointerDragged = true;
        // A quick deliberate drag moves the desktop window. Holding first is
        // interpreted as hold/lift intent and never silently becomes window movement.
        if (DateTimeOffset.Now - _petPointerDownAt >= TimeSpan.FromMilliseconds(600)) return;

        _petPointerArmed = false;
        if (!_viewModel.IsPetrified)
            _viewModel.RegisterPointerUp(local.X, local.Y, windowDrag: true);
        if (sender is UIElement element) element.ReleaseMouseCapture();
        try { DragMove(); }
        catch (InvalidOperationException) { }
        e.Handled = true;
    }

    private async void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_petPointerArmed) return;
        _petPointerArmed = false;
        if (sender is UIElement element) element.ReleaseMouseCapture();
        var local = e.GetPosition(sender as IInputElement);
        if (_viewModel.IsPetrified)
        {
            if (_coinDoubleClickHandled)
            {
                _coinDoubleClickHandled = false;
                e.Handled = true;
                return;
            }
            switch (CoinPointerGestureClassifier.Classify(
                        _petPointerDragged,
                        e.ClickCount))
            {
                case CoinPointerAction.Flip:
                    await _viewModel.FlipPetrifiedCoinAsync();
                    break;
                case CoinPointerAction.RefreshColor:
                    _viewModel.RefreshPetrifiedCoinColor();
                    break;
            }
            e.Handled = true;
            return;
        }
        _viewModel.RegisterPointerUp(local.X, local.Y);
        e.Handled = true;
    }

    private void PetImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var command = e.Delta > 0 ? _viewModel.ZoomInCommand : _viewModel.ZoomOutCommand;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    private async void ViewModel_DesktopMoveRequested(object? sender, DesktopMoveRequestEventArgs e)
    {
        if (_walking)
        {
            e.Completion.TrySetResult(false);
            return;
        }
        _walking = true;
        _viewModel.BeginSynchronizedMovement();
        var succeeded = false;
        await Dispatcher.Yield(DispatcherPriority.Loaded);

        try
        {
            if (e.Mode == DesktopMoveMode.AnchorApproach)
            {
                succeeded = await MoveToAnchorAsync(e);
                return;
            }
            if (e.Mode == DesktopMoveMode.Apparate)
            {
                succeeded = await MoveApparateAsync(e);
                return;
            }
            if (e.Mode == DesktopMoveMode.BroomFlight)
            {
                succeeded = await MoveBroomFlightAsync(e);
                return;
            }
            if (e.Mode == DesktopMoveMode.EdgePolish)
            {
                succeeded = await MoveEdgePolishAsync(e);
                return;
            }
            // A fresh unpredictable planner creates every segment at runtime.
            // No waypoint list or pre-authored path is stored anywhere.
            var routePlanner = new DesktopRoutePlanner(
                RandomNumberGenerator.GetInt32(int.MaxValue));
            var until = DateTimeOffset.Now.Add(e.Duration);
            while (DateTimeOffset.Now < until && !e.CancellationToken.IsCancellationRequested)
            {
                var work = CurrentWorkArea();
                var bounds = MovementBounds(work);
                var profile = e.FullWalk
                    ? DesktopRouteProfile.FullWalk
                    : DesktopRouteProfile.AutonomousRoam;
                if (!routePlanner.TryCreateWalkSegment(
                        bounds,
                        new RoutePoint(Left, Top),
                        profile,
                        out var segment))
                    break;

                if (e.Mode == DesktopMoveMode.HarnessedWalk)
                    _viewModel.SetMovementHeading(
                        segment.End.X - segment.Start.X,
                        segment.End.Y - segment.Start.Y,
                        e.Mode);
                else
                    _viewModel.SetMovementDirection(
                        ToPetDirection(segment.Direction),
                        e.Mode);
                await AnimateRouteSegmentAsync(
                    segment,
                    bounds,
                    until,
                    e.CancellationToken);
            }
            succeeded = !e.CancellationToken.IsCancellationRequested;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.ReportRecoverableException(ex, "desktop random movement");
        }
        finally
        {
            Opacity = 1;
            _walking = false;
            _viewModel.EndSynchronizedMovement();
            ClampToDesktop();
            e.Completion.TrySetResult(succeeded && !e.CancellationToken.IsCancellationRequested);
        }
    }

    private async Task<bool> MoveToAnchorAsync(DesktopMoveRequestEventArgs request)
    {
        if (request.Target is not { } target) return false;
        var work = CurrentWorkArea();
        var targetX = Math.Clamp(
            target.X - ActualWidth * 0.5,
            work.Left,
            Math.Max(work.Left, work.Right - ActualWidth));
        var targetY = Math.Clamp(
            target.Y - ActualHeight * 0.72,
            work.Top,
            Math.Max(work.Top, work.Bottom - ActualHeight));
        var dx = targetX - Left;
        var dy = targetY - Top;
        _viewModel.SetMovementHeading(dx, dy, DesktopMoveMode.AnchorApproach);
        var distance = Math.Sqrt(dx * dx + dy * dy);
        await AnimateWindowAsync(
            targetX,
            targetY,
            TimeSpan.FromMilliseconds(Math.Clamp(distance * 5.2, 850, 4200)),
            request.CancellationToken);
        return !request.CancellationToken.IsCancellationRequested;
    }

    private async Task<bool> MoveApparateAsync(DesktopMoveRequestEventArgs request)
    {
        var work = CurrentWorkArea();
        var maxX = Math.Max(work.Left, work.Right - ActualWidth);
        var maxY = Math.Max(work.Top, work.Bottom - ActualHeight);
        var random = new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
        var targetX = Left;
        var targetY = Top;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            targetX = work.Left + random.NextDouble() * Math.Max(1, maxX - work.Left);
            targetY = work.Top + random.NextDouble() * Math.Max(1, maxY - work.Top);
            if (Math.Abs(targetX - Left) + Math.Abs(targetY - Top) >= 180) break;
        }

        await Task.Delay(420, request.CancellationToken);
        await AnimateOpacityAsync(1, 0, TimeSpan.FromMilliseconds(260), request.CancellationToken);
        await Task.Delay(2100, request.CancellationToken);
        Left = Math.Clamp(targetX, work.Left, maxX);
        Top = Math.Clamp(targetY, work.Top, maxY);
        await AnimateOpacityAsync(0, 1, TimeSpan.FromMilliseconds(320), request.CancellationToken);
        return !request.CancellationToken.IsCancellationRequested;
    }

    private async Task<bool> MoveBroomFlightAsync(DesktopMoveRequestEventArgs request)
    {
        var routePlanner = new DesktopRoutePlanner(
            RandomNumberGenerator.GetInt32(int.MaxValue));
        var until = DateTimeOffset.Now.Add(request.Duration);
        while (DateTimeOffset.Now < until && !request.CancellationToken.IsCancellationRequested)
        {
            var work = CurrentWorkArea();
            var bounds = MovementBounds(work);
            if (!routePlanner.TryCreateBroomSegment(
                    bounds,
                    new RoutePoint(Left, Top),
                    out var segment))
                return false;

            _viewModel.SetMovementHeading(
                segment.End.X - segment.Start.X,
                segment.End.Y - segment.Start.Y,
                DesktopMoveMode.BroomFlight);
            await AnimateRouteSegmentAsync(
                segment,
                bounds,
                until,
                request.CancellationToken);
        }
        return !request.CancellationToken.IsCancellationRequested;
    }

    private async Task<bool> MoveEdgePolishAsync(DesktopMoveRequestEventArgs request)
    {
        var until = DateTimeOffset.Now.Add(request.Duration);
        var directionFixed = RandomNumberGenerator.GetInt32(2) == 0;
        while (DateTimeOffset.Now < until && !request.CancellationToken.IsCancellationRequested)
        {
            var surface = request.Surface is null
                ? null
                : EnvironmentContextService.RefreshSurface(request.Surface.Handle);
            var work = surface?.MonitorWorkArea ?? CurrentWorkArea();
            var minX = surface is null ? work.Left : Math.Max(surface.UsableLeft, work.Left);
            var maxX = surface is null
                ? Math.Max(work.Left, work.Right - ActualWidth)
                : Math.Min(surface.UsableRight - ActualWidth, work.Right - ActualWidth);
            var top = surface is null
                ? work.Top
                : Math.Clamp(surface.TopEdge - ActualHeight + 12, work.Top, work.Bottom - ActualHeight);
            var bottom = surface is null
                ? Math.Max(work.Top, work.Bottom - ActualHeight)
                : Math.Clamp(surface.Bounds.Bottom - ActualHeight - 8, work.Top, work.Bottom - ActualHeight);
            if (maxX <= minX) return false;
            var corners = directionFixed
                ? new[] { new Point(minX, top), new Point(maxX, top), new Point(maxX, bottom), new Point(minX, bottom) }
                : new[] { new Point(minX, top), new Point(minX, bottom), new Point(maxX, bottom), new Point(maxX, top) };
            var nearestIndex = Enumerable.Range(0, corners.Length)
                .OrderBy(index => Math.Pow(corners[index].X - Left, 2) + Math.Pow(corners[index].Y - Top, 2))
                .First();
            var route = Enumerable.Range(0, corners.Length)
                .Select(offset => corners[(nearestIndex + offset) % corners.Length])
                .ToArray();
            foreach (var point in route)
            {
                if (DateTimeOffset.Now >= until || request.CancellationToken.IsCancellationRequested) break;
                var distance = Math.Sqrt(Math.Pow(point.X - Left, 2) + Math.Pow(point.Y - Top, 2));
                await AnimateWindowAsync(
                    point.X,
                    point.Y,
                    TimeSpan.FromMilliseconds(Math.Clamp(distance * 1.9, 360, 1250)),
                    request.CancellationToken);
            }
        }
        return !request.CancellationToken.IsCancellationRequested;
    }

    private async Task AnimateRouteSegmentAsync(
        DesktopRouteSegment segment,
        RouteBounds bounds,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var stepMilliseconds = _viewModel.SynchronizedMovementStepMilliseconds;
        var frames = Math.Max(
            2,
            (int)Math.Ceiling(segment.Duration.TotalMilliseconds / stepMilliseconds));
        for (var frame = 1; frame <= frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.Now >= until) break;
            var t = frame / (double)frames;
            var point = segment.Sample(t, bounds);
            Left = point.X;
            Top = point.Y;
            _viewModel.StepSynchronizedMovementFrame();
            await Task.Delay(stepMilliseconds, cancellationToken);
        }
    }

    private async Task AnimateOpacityAsync(
        double from,
        double to,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var frames = Math.Max(8, (int)(duration.TotalMilliseconds / 16));
        for (var frame = 1; frame <= frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = frame / (double)frames;
            var smooth = t * t * (3 - 2 * t);
            Opacity = from + (to - from) * smooth;
            await Task.Delay(16, cancellationToken);
        }
    }

    private RouteBounds MovementBounds(DesktopRect work) => new(
        work.Left,
        work.Top,
        Math.Max(work.Left, work.Right - ActualWidth),
        Math.Max(work.Top, work.Bottom - ActualHeight));

    private static PetDirection ToPetDirection(RouteDirection direction) =>
        direction switch
        {
            RouteDirection.Left => PetDirection.Left,
            RouteDirection.Right => PetDirection.Right,
            RouteDirection.Up => PetDirection.Up,
            RouteDirection.Down => PetDirection.Down,
            RouteDirection.UpLeft => PetDirection.UpLeft,
            RouteDirection.UpRight => PetDirection.UpRight,
            RouteDirection.DownLeft => PetDirection.DownLeft,
            _ => PetDirection.DownRight
        };

    private async Task AnimateWindowAsync(
        double targetX,
        double targetY,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var startX = Left;
        var startY = Top;
        var stepMilliseconds = _viewModel.SynchronizedMovementStepMilliseconds;
        var frames = Math.Max(
            2,
            (int)Math.Ceiling(duration.TotalMilliseconds / stepMilliseconds));
        for (var frame = 1; frame <= frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = frame / (double)frames;
            var smooth = t * t * (3 - 2 * t);
            Left = startX + (targetX - startX) * smooth;
            Top = startY + (targetY - startY) * smooth;
            _viewModel.StepSynchronizedMovementFrame();
            await Task.Delay(stepMilliseconds, cancellationToken);
        }
    }

    private DesktopRect CurrentWorkArea() =>
        _windowHandle == IntPtr.Zero
            ? new DesktopRect(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Right,
                SystemParameters.WorkArea.Bottom)
            : EnvironmentContextService.GetCurrentMonitorWorkArea(_windowHandle);

    private void RefreshDesktopEnvironment()
    {
        if (_windowHandle == IntPtr.Zero) return;
        var snapshot = EnvironmentContextService.Capture(_windowHandle);
        _viewModel.UpdateDesktopEnvironment(snapshot);
    }

    private void RefreshCursorGaze()
    {
        if (_walking ||
            !IsLoaded ||
            !IsVisible ||
            !GetCursorPos(out var cursor))
        {
            _viewModel.UpdateCursorGaze(0, false);
            return;
        }

        var topLeft = PetImage.PointToScreen(new Point(0, 0));
        var bottomRight = PetImage.PointToScreen(new Point(PetImage.ActualWidth, PetImage.ActualHeight));
        var centerX = (topLeft.X + bottomRight.X) / 2;
        var centerY = (topLeft.Y + bottomRight.Y) / 2;
        var dx = cursor.X - centerX;
        var dy = cursor.Y - centerY;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 300 * dpi)
        {
            _viewModel.UpdateCursorGaze(0, false);
            return;
        }

        var deadZone = 28 * dpi;
        var frame = distance <= deadZone
            ? 0
            : Math.Abs(dx) >= Math.Abs(dy) * 1.30
                ? dx < 0 ? 1 : 5
                : dy < 0
                    ? Math.Abs(dx) < Math.Abs(dy) * 0.42
                        ? 3
                        : dx < 0 ? 2 : 4
                    : dx < 0 ? 7 : 6;
        _viewModel.UpdateCursorGaze(frame, true);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.DesktopMoveRequested -= ViewModel_DesktopMoveRequested;
        _viewModel.ControlPanelRequested -= ViewModel_ControlPanelRequested;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _environmentTimer.Stop();
        _cursorGazeTimer.Stop();
        _viewModel.UpdateCursorGaze(0, false);
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.TimeChanged -= SystemEvents_TimeChanged;
        if (_controlWindow is not null)
        {
            _controlWindow.AllowClose = true;
            _controlWindow.Close();
        }
        _anchorPlacementWindow?.Close();
        _anchorPlacementWindow = null;
        _viewModel.Dispose();
    }

    private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            await await Dispatcher.InvokeAsync(_viewModel.NotifySuspendingAsync);
        else if (e.Mode == PowerModes.Resume)
            await await Dispatcher.InvokeAsync(_viewModel.NotifyResumedAsync);
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _viewModel.RegisterSystemPerception("display_changed");
            RefreshDesktopEnvironment();
            ClampToDesktop();
        });

    private void SystemEvents_TimeChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => _viewModel.RegisterSystemPerception("system_time_changed"));

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
