namespace Pupu.Desktop.Services;

public enum DesktopRouteProfile
{
    FullWalk,
    AutonomousRoam,
    BroomFlight
}

public enum RouteDirection
{
    Left,
    Right,
    Up,
    Down,
    UpLeft,
    UpRight,
    DownLeft,
    DownRight
}

public readonly record struct RoutePoint(double X, double Y)
{
    public double DistanceTo(RoutePoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// The valid range for the top-left corner of the transparent pet window.
/// The caller subtracts the current window size from the monitor work area
/// before constructing these bounds.
/// </summary>
public readonly record struct RouteBounds(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);
    public double Diagonal => Math.Sqrt(Width * Width + Height * Height);
    public bool CanMove => Width >= 2 || Height >= 2;

    public RoutePoint Clamp(RoutePoint point) => new(
        Math.Clamp(point.X, Left, Math.Max(Left, Right)),
        Math.Clamp(point.Y, Top, Math.Max(Top, Bottom)));
}

public sealed record DesktopRouteSegment(
    RoutePoint Start,
    RoutePoint End,
    RouteDirection Direction,
    TimeSpan Duration,
    double Bend,
    double Lift,
    double Flutter,
    bool IsReposition = false)
{
    public double Distance => Start.DistanceTo(End);
    public double Speed => Duration.TotalSeconds <= 0
        ? 0
        : Distance / Duration.TotalSeconds;

    public RoutePoint Sample(double progress, RouteBounds bounds)
    {
        var t = Math.Clamp(progress, 0, 1);
        var smooth = t * t * (3 - 2 * t);
        var deltaX = End.X - Start.X;
        var deltaY = End.Y - Start.Y;
        var length = Math.Max(1, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
        var normalX = -deltaY / length;
        var normalY = deltaX / length;
        var arc = Math.Sin(Math.PI * smooth) * Bend;
        var lift = Math.Sin(Math.PI * t) * Lift;
        var flutter = Math.Sin(Math.PI * 4 * t) * Flutter;
        return bounds.Clamp(new RoutePoint(
            Start.X + deltaX * smooth + normalX * arc,
            Start.Y + deltaY * smooth + normalY * arc - lift - flutter));
    }
}

/// <summary>
/// Produces fresh route segments at runtime. It owns no window state and uses
/// no WPF types, so route invariants can be tested independently of Windows.
/// </summary>
public sealed class DesktopRoutePlanner
{
    private static readonly RouteDirection[] AllDirections =
    {
        RouteDirection.Left,
        RouteDirection.Right,
        RouteDirection.Up,
        RouteDirection.Down,
        RouteDirection.UpLeft,
        RouteDirection.UpRight,
        RouteDirection.DownLeft,
        RouteDirection.DownRight
    };

    private readonly Random _random;
    private readonly Queue<RouteDirection> _broomDirectionBucket = new();
    private RouteDirection? _lastBroomDirection;
    private RouteDirection? _lastWalkDirection;
    private int _walkDirectionStreak;
    private bool _broomInitialized;

    public DesktopRoutePlanner(int seed)
    {
        _random = new Random(seed);
    }

    public bool TryCreateWalkSegment(
        RouteBounds bounds,
        RoutePoint current,
        DesktopRouteProfile profile,
        out DesktopRouteSegment segment)
    {
        if (profile == DesktopRouteProfile.BroomFlight)
            throw new ArgumentOutOfRangeException(nameof(profile));

        current = bounds.Clamp(current);
        if (!bounds.CanMove)
        {
            segment = default!;
            return false;
        }

        var minimum = Math.Min(
            profile == DesktopRouteProfile.FullWalk ? 92 : 58,
            Math.Max(2, bounds.Diagonal * 0.12));
        var maximum = Math.Max(
            minimum + 1,
            bounds.Diagonal *
            (profile == DesktopRouteProfile.FullWalk ? 0.76 : 0.58));

        RoutePoint target = current;
        var found = false;
        if (_lastWalkDirection is { } previous &&
            _walkDirectionStreak < 3 &&
            _random.NextDouble() < 0.72 &&
            TryDirectionalTarget(bounds, current, previous, out target))
        {
            var continuedDistance = current.DistanceTo(target);
            found = continuedDistance >= minimum && continuedDistance <= maximum;
        }
        for (var attempt = 0; attempt < 36; attempt++)
        {
            if (found) break;
            var broad = _random.NextDouble() <
                        (profile == DesktopRouteProfile.FullWalk ? 0.68 : 0.48);
            target = broad
                ? RandomPoint(bounds, 0.025)
                : RandomNearbyPoint(
                    bounds,
                    current,
                    profile == DesktopRouteProfile.FullWalk
                        ? (0.10, 0.42)
                        : (0.06, 0.30));
            var distance = current.DistanceTo(target);
            if (distance >= minimum && distance <= maximum)
            {
                found = true;
                break;
            }
        }

        if (!found &&
            !TryFarthestFallback(bounds, current, minimum, out target))
        {
            segment = default!;
            return false;
        }

        var distanceToTarget = current.DistanceTo(target);
        if (distanceToTarget < 1)
        {
            segment = default!;
            return false;
        }

        var direction = DirectionForVector(
            target.X - current.X,
            target.Y - current.Y);
        if (_lastWalkDirection == direction)
            _walkDirectionStreak++;
        else
        {
            _lastWalkDirection = direction;
            _walkDirectionStreak = 1;
        }
        var speed = profile == DesktopRouteProfile.FullWalk
            ? 190 + _random.NextDouble() * 130
            : 135 + _random.NextDouble() * 85;
        var durationMs = Math.Clamp(
            distanceToTarget / speed * 1000,
            profile == DesktopRouteProfile.FullWalk ? 650 : 600,
            profile == DesktopRouteProfile.FullWalk ? 3600 : 2800);
        var maximumBend = Math.Min(
            profile == DesktopRouteProfile.FullWalk ? 145 : 88,
            distanceToTarget *
            (profile == DesktopRouteProfile.FullWalk ? 0.28 : 0.20));
        var bend = (_random.NextDouble() - 0.5) * 2 * maximumBend;
        var liftChance = profile == DesktopRouteProfile.FullWalk ? 0.32 : 0.14;
        var lift = _random.NextDouble() < liftChance
            ? 4 + _random.NextDouble() *
            (profile == DesktopRouteProfile.FullWalk ? 14 : 7)
            : 0;
        var decorations = FitDecorations(
            bounds,
            current,
            target,
            direction,
            bend,
            lift,
            0);

        segment = new DesktopRouteSegment(
            current,
            target,
            direction,
            TimeSpan.FromMilliseconds(durationMs),
            decorations.Bend,
            decorations.Lift,
            decorations.Flutter);
        return true;
    }

    public bool TryCreateBroomSegment(
        RouteBounds bounds,
        RoutePoint current,
        out DesktopRouteSegment segment)
    {
        current = bounds.Clamp(current);
        if (!bounds.CanMove)
        {
            segment = default!;
            return false;
        }

        if (!_broomInitialized)
        {
            _broomInitialized = true;
            var normalizedX = bounds.Width <= 0
                ? 0.5
                : (current.X - bounds.Left) / bounds.Width;
            var normalizedY = bounds.Height <= 0
                ? 0.5
                : (current.Y - bounds.Top) / bounds.Height;
            if (normalizedX is < 0.22 or > 0.78 ||
                normalizedY is < 0.22 or > 0.78)
            {
                var centerTarget = new RoutePoint(
                    bounds.Left + bounds.Width * (0.38 + _random.NextDouble() * 0.24),
                    bounds.Top + bounds.Height * (0.38 + _random.NextDouble() * 0.24));
                var centerDirection = DirectionForVector(
                    centerTarget.X - current.X,
                    centerTarget.Y - current.Y);
                segment = CreateBroomSegment(
                    bounds,
                    current,
                    centerTarget,
                    centerDirection,
                    isReposition: true);
                return true;
            }
        }

        EnsureBroomDirectionBucket();
        var directionsToTry = _broomDirectionBucket.Count;
        for (var index = 0; index < directionsToTry; index++)
        {
            var desired = _broomDirectionBucket.Dequeue();
            if (!TryDirectionalTarget(bounds, current, desired, out var target))
            {
                _broomDirectionBucket.Enqueue(desired);
                continue;
            }

            _lastBroomDirection = desired;
            segment = CreateBroomSegment(bounds, current, target, desired);
            return true;
        }

        // An exact outward direction can be impossible when the window starts
        // flush against an edge. A far inward reposition keeps the flight
        // moving; the unconsumed bucket is retried from the new position.
        if (!TryFarthestFallback(
                bounds,
                current,
                Math.Min(80, Math.Max(2, bounds.Diagonal * 0.08)),
                out var fallback))
        {
            segment = default!;
            return false;
        }

        var fallbackDirection = DirectionForVector(
            fallback.X - current.X,
            fallback.Y - current.Y);
        segment = CreateBroomSegment(
            bounds,
            current,
            fallback,
            fallbackDirection,
            isReposition: true);
        return true;
    }

    public static RouteDirection DirectionForVector(double deltaX, double deltaY)
    {
        var horizontal = Math.Abs(deltaX);
        var vertical = Math.Abs(deltaY);
        if (horizontal > vertical * 1.75)
            return deltaX < 0 ? RouteDirection.Left : RouteDirection.Right;
        if (vertical > horizontal * 1.75)
            return deltaY < 0 ? RouteDirection.Up : RouteDirection.Down;
        if (deltaY < 0)
            return deltaX < 0 ? RouteDirection.UpLeft : RouteDirection.UpRight;
        return deltaX < 0 ? RouteDirection.DownLeft : RouteDirection.DownRight;
    }

    private DesktopRouteSegment CreateBroomSegment(
        RouteBounds bounds,
        RoutePoint current,
        RoutePoint target,
        RouteDirection direction,
        bool isReposition = false)
    {
        var distance = current.DistanceTo(target);
        var speed = 540 + _random.NextDouble() * 180;
        var durationMs = Math.Clamp(distance / speed * 1000, 500, 900);
        var maximumBend = Math.Min(82, Math.Max(18, distance * 0.16));
        var bend = (_random.NextDouble() < 0.5 ? -1 : 1) *
                   maximumBend *
                   (0.56 + _random.NextDouble() * 0.44);
        var decorations = FitDecorations(
            bounds,
            current,
            target,
            direction,
            bend,
            0,
            2);
        return new DesktopRouteSegment(
            current,
            target,
            direction,
            TimeSpan.FromMilliseconds(durationMs),
            decorations.Bend,
            decorations.Lift,
            decorations.Flutter,
            isReposition);
    }

    private bool TryDirectionalTarget(
        RouteBounds bounds,
        RoutePoint current,
        RouteDirection desired,
        out RoutePoint target)
    {
        var minimum = desired is RouteDirection.Left or RouteDirection.Right
            ? Math.Max(54, bounds.Width * 0.13)
            : desired is RouteDirection.Up or RouteDirection.Down
                ? Math.Max(48, bounds.Height * 0.13)
                : Math.Max(72, bounds.Diagonal * 0.105);
        minimum = Math.Min(minimum, Math.Max(2, bounds.Diagonal * 0.30));

        for (var attempt = 0; attempt < 52; attempt++)
        {
            target = DirectionalPoint(bounds, current, desired, relaxed: false);
            var distance = current.DistanceTo(target);
            if (distance >= minimum &&
                DirectionForVector(
                    target.X - current.X,
                    target.Y - current.Y) == desired)
                return true;
        }

        // Once inside the work area, consume the requested bucket direction
        // with a shorter but still visible segment instead of ever standing
        // still or silently changing to another direction.
        for (var attempt = 0; attempt < 52; attempt++)
        {
            target = DirectionalPoint(bounds, current, desired, relaxed: true);
            if (current.DistanceTo(target) >= 24 &&
                DirectionForVector(
                    target.X - current.X,
                    target.Y - current.Y) == desired)
                return true;
        }

        target = current;
        return false;
    }

    private RoutePoint DirectionalPoint(
        RouteBounds bounds,
        RoutePoint current,
        RouteDirection direction,
        bool relaxed)
    {
        var leftSpace = Math.Max(0, current.X - bounds.Left);
        var rightSpace = Math.Max(0, bounds.Right - current.X);
        var upSpace = Math.Max(0, current.Y - bounds.Top);
        var downSpace = Math.Max(0, bounds.Bottom - current.Y);
        var travel = relaxed
            ? 0.56 + _random.NextDouble() * 0.22
            : 0.78 + _random.NextDouble() * 0.16;
        var insetX = relaxed ? 0 : Math.Min(54, bounds.Width * 0.065);
        var insetY = relaxed ? 0 : Math.Min(46, bounds.Height * 0.065);
        var leftTravel = TravelDistance(leftSpace, insetX, travel);
        var rightTravel = TravelDistance(rightSpace, insetX, travel);
        var upTravel = TravelDistance(upSpace, insetY, travel);
        var downTravel = TravelDistance(downSpace, insetY, travel);
        return direction switch
        {
            RouteDirection.Left => new RoutePoint(
                current.X - leftTravel,
                Math.Clamp(
                    current.Y + (_random.NextDouble() - 0.5) *
                    leftTravel * 0.66,
                    bounds.Top + insetY,
                    bounds.Bottom - insetY)),
            RouteDirection.Right => new RoutePoint(
                current.X + rightTravel,
                Math.Clamp(
                    current.Y + (_random.NextDouble() - 0.5) *
                    rightTravel * 0.66,
                    bounds.Top + insetY,
                    bounds.Bottom - insetY)),
            RouteDirection.Up => new RoutePoint(
                Math.Clamp(
                    current.X + (_random.NextDouble() - 0.5) *
                    upTravel * 0.66,
                    bounds.Left + insetX,
                    bounds.Right - insetX),
                current.Y - upTravel),
            RouteDirection.Down => new RoutePoint(
                Math.Clamp(
                    current.X + (_random.NextDouble() - 0.5) *
                    downTravel * 0.66,
                    bounds.Left + insetX,
                    bounds.Right - insetX),
                current.Y + downTravel),
            RouteDirection.UpLeft => DiagonalPoint(
                current,
                -1,
                -1,
                leftTravel,
                upTravel),
            RouteDirection.UpRight => DiagonalPoint(
                current,
                1,
                -1,
                rightTravel,
                upTravel),
            RouteDirection.DownLeft => DiagonalPoint(
                current,
                -1,
                1,
                leftTravel,
                downTravel),
            _ => DiagonalPoint(
                current,
                1,
                1,
                rightTravel,
                downTravel)
        };
    }

    private static double TravelDistance(
        double available,
        double protectedInset,
        double travel) =>
        Math.Max(0, Math.Min(available * travel, available - protectedInset));

    private RoutePoint DiagonalPoint(
        RoutePoint current,
        int horizontalSign,
        int verticalSign,
        double horizontalTravel,
        double verticalTravel)
    {
        var common = Math.Min(horizontalTravel, verticalTravel);
        var horizontal = common * (0.88 + _random.NextDouble() * 0.12);
        var vertical = common * (0.88 + _random.NextDouble() * 0.12);
        return new RoutePoint(
            current.X + horizontalSign * horizontal,
            current.Y + verticalSign * vertical);
    }

    private static (double Bend, double Lift, double Flutter) FitDecorations(
        RouteBounds bounds,
        RoutePoint start,
        RoutePoint end,
        RouteDirection direction,
        double bend,
        double lift,
        double flutter)
    {
        for (var reduction = 0; reduction < 10; reduction++)
        {
            var probe = new DesktopRouteSegment(
                start,
                end,
                direction,
                TimeSpan.FromMilliseconds(500),
                bend,
                lift,
                flutter);
            var clipped = false;
            for (var sample = 1; sample < 48; sample++)
            {
                var t = sample / 48d;
                var raw = SampleUnclamped(probe, t);
                if (raw.X < bounds.Left ||
                    raw.X > bounds.Right ||
                    raw.Y < bounds.Top ||
                    raw.Y > bounds.Bottom)
                {
                    clipped = true;
                    break;
                }
            }
            if (!clipped) return (bend, lift, flutter);
            bend *= 0.64;
            lift *= 0.64;
            flutter *= 0.64;
        }
        return (0, 0, 0);
    }

    private static RoutePoint SampleUnclamped(
        DesktopRouteSegment segment,
        double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var smooth = t * t * (3 - 2 * t);
        var deltaX = segment.End.X - segment.Start.X;
        var deltaY = segment.End.Y - segment.Start.Y;
        var length = Math.Max(1, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
        var normalX = -deltaY / length;
        var normalY = deltaX / length;
        var arc = Math.Sin(Math.PI * smooth) * segment.Bend;
        var flutter = Math.Sin(Math.PI * 4 * t) * segment.Flutter;
        return new RoutePoint(
            segment.Start.X + deltaX * smooth + normalX * arc,
            segment.Start.Y + deltaY * smooth + normalY * arc - flutter);
    }

    private void EnsureBroomDirectionBucket()
    {
        if (_broomDirectionBucket.Count > 0) return;
        var shuffled = AllDirections.ToArray();
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swap = _random.Next(index + 1);
            (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
        }

        var ordered = new List<RouteDirection>(shuffled.Length);
        if (!TryBuildCompatibleOrder(
                shuffled.ToList(),
                ordered,
                _lastBroomDirection))
        {
            ordered.Clear();
            ordered.AddRange(shuffled);
        }

        foreach (var direction in ordered)
            _broomDirectionBucket.Enqueue(direction);
    }

    private static bool TryBuildCompatibleOrder(
        List<RouteDirection> remaining,
        List<RouteDirection> result,
        RouteDirection? previous)
    {
        if (remaining.Count == 0) return true;
        for (var index = 0; index < remaining.Count; index++)
        {
            var candidate = remaining[index];
            if (previous is { } prior &&
                !AreDirectionallyCompatible(prior, candidate))
                continue;

            remaining.RemoveAt(index);
            result.Add(candidate);
            if (TryBuildCompatibleOrder(remaining, result, candidate))
                return true;
            result.RemoveAt(result.Count - 1);
            remaining.Insert(index, candidate);
        }
        return false;
    }

    private static bool AreDirectionallyCompatible(
        RouteDirection previous,
        RouteDirection next)
    {
        var previousVector = DirectionVector(previous);
        var nextVector = DirectionVector(next);
        var horizontalCompatible =
            previousVector.X == 0 ||
            nextVector.X == 0 ||
            previousVector.X != nextVector.X;
        var verticalCompatible =
            previousVector.Y == 0 ||
            nextVector.Y == 0 ||
            previousVector.Y != nextVector.Y;
        return horizontalCompatible && verticalCompatible;
    }

    private static (int X, int Y) DirectionVector(RouteDirection direction) =>
        direction switch
        {
            RouteDirection.Left => (-1, 0),
            RouteDirection.Right => (1, 0),
            RouteDirection.Up => (0, -1),
            RouteDirection.Down => (0, 1),
            RouteDirection.UpLeft => (-1, -1),
            RouteDirection.UpRight => (1, -1),
            RouteDirection.DownLeft => (-1, 1),
            _ => (1, 1)
        };

    private RoutePoint RandomPoint(RouteBounds bounds, double insetRatio)
    {
        var insetX = bounds.Width * insetRatio;
        var insetY = bounds.Height * insetRatio;
        var usableWidth = Math.Max(0, bounds.Width - insetX * 2);
        var usableHeight = Math.Max(0, bounds.Height - insetY * 2);
        return new RoutePoint(
            bounds.Left + insetX + _random.NextDouble() * usableWidth,
            bounds.Top + insetY + _random.NextDouble() * usableHeight);
    }

    private RoutePoint RandomNearbyPoint(
        RouteBounds bounds,
        RoutePoint current,
        (double Minimum, double Maximum) radiusRange)
    {
        var angle = _random.NextDouble() * Math.PI * 2;
        var radius = bounds.Diagonal *
                     (radiusRange.Minimum +
                      _random.NextDouble() *
                      (radiusRange.Maximum - radiusRange.Minimum));
        return bounds.Clamp(new RoutePoint(
            current.X + Math.Cos(angle) * radius,
            current.Y + Math.Sin(angle) * radius));
    }

    private static bool TryFarthestFallback(
        RouteBounds bounds,
        RoutePoint current,
        double minimum,
        out RoutePoint target)
    {
        var insetX = Math.Min(24, bounds.Width * 0.04);
        var insetY = Math.Min(24, bounds.Height * 0.04);
        var left = bounds.Left + insetX;
        var right = bounds.Right - insetX;
        var top = bounds.Top + insetY;
        var bottom = bounds.Bottom - insetY;
        var candidates = new[]
        {
            new RoutePoint(left, top),
            new RoutePoint(right, top),
            new RoutePoint(left, bottom),
            new RoutePoint(right, bottom),
            new RoutePoint((left + right) / 2, top),
            new RoutePoint((left + right) / 2, bottom),
            new RoutePoint(left, (top + bottom) / 2),
            new RoutePoint(right, (top + bottom) / 2)
        };
        target = candidates
            .OrderByDescending(current.DistanceTo)
            .First();
        return current.DistanceTo(target) >=
               Math.Min(minimum, Math.Max(1, bounds.Diagonal * 0.45));
    }
}
