namespace Pupu.Behavior;

public enum InteractionIntent
{
    TouchPet,
    StrokePet,
    HoldPet,
    LiftPet,
    MoveWindow,
    Release
}

public enum InteractionRegionKind
{
    Head,
    Body,
    Paw,
    Tail,
    MoveHandle
}

public sealed record InteractionRegionHit(
    string RegionId,
    InteractionRegionKind Kind,
    bool SupportsLift)
{
    public static InteractionRegionHit Default { get; } =
        new("body", InteractionRegionKind.Body, true);
}

public sealed record NormalizedInteractionRegion(
    string RegionId,
    InteractionRegionKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    bool SupportsLift = false);

public sealed class InteractionRegionMap
{
    private static readonly IReadOnlyList<NormalizedInteractionRegion> DefaultRegions =
        new[]
        {
            new NormalizedInteractionRegion("head", InteractionRegionKind.Head, 0.25, 0.12, 0.50, 0.34),
            new NormalizedInteractionRegion("front_paws", InteractionRegionKind.Paw, 0.22, 0.52, 0.46, 0.23),
            new NormalizedInteractionRegion("tail", InteractionRegionKind.Tail, 0.66, 0.34, 0.27, 0.42),
            new NormalizedInteractionRegion("body", InteractionRegionKind.Body, 0.16, 0.34, 0.62, 0.48, true),
            new NormalizedInteractionRegion("move_handle", InteractionRegionKind.MoveHandle, 0.05, 0.78, 0.90, 0.18)
        };

    private readonly Dictionary<string, IReadOnlyList<NormalizedInteractionRegion>> _poseRegions =
        new(StringComparer.OrdinalIgnoreCase);

    public InteractionRegionMap()
    {
        _poseRegions["sleep-curled"] = new[]
        {
            new NormalizedInteractionRegion("head", InteractionRegionKind.Head, 0.28, 0.30, 0.32, 0.26),
            new NormalizedInteractionRegion("sleeping_body", InteractionRegionKind.Body, 0.16, 0.28, 0.68, 0.50, true),
            new NormalizedInteractionRegion("tail", InteractionRegionKind.Tail, 0.55, 0.40, 0.30, 0.34),
            new NormalizedInteractionRegion("move_handle", InteractionRegionKind.MoveHandle, 0.08, 0.80, 0.84, 0.16)
        };
        _poseRegions["sleep-belly-up"] = new[]
        {
            new NormalizedInteractionRegion("head", InteractionRegionKind.Head, 0.34, 0.14, 0.34, 0.27),
            new NormalizedInteractionRegion("belly", InteractionRegionKind.Body, 0.24, 0.36, 0.54, 0.42, true),
            new NormalizedInteractionRegion("paws", InteractionRegionKind.Paw, 0.16, 0.24, 0.68, 0.50),
            new NormalizedInteractionRegion("move_handle", InteractionRegionKind.MoveHandle, 0.08, 0.80, 0.84, 0.16)
        };
    }

    public InteractionRegionHit HitTest(
        string sequenceName,
        int frame,
        string direction,
        double displayWidth,
        double displayHeight,
        double x,
        double y)
    {
        if (displayWidth <= 0 || displayHeight <= 0) return InteractionRegionHit.Default;
        var normalizedX = Math.Clamp(x / displayWidth, 0, 1);
        var normalizedY = Math.Clamp(y / displayHeight, 0, 1);
        if (string.Equals(direction, "left", StringComparison.OrdinalIgnoreCase))
            normalizedX = 1 - normalizedX;

        // The breathing/step offset keeps hit regions attached to the current
        // frame rather than to a static window rectangle.
        var frameYOffset = Math.Sin(Math.Clamp(frame, 0, 15) * Math.PI / 4) * 0.012;
        normalizedY = Math.Clamp(normalizedY - frameYOffset, 0, 1);

        var key = _poseRegions.Keys.FirstOrDefault(sequenceName.Contains);
        var regions = key is null ? DefaultRegions : _poseRegions[key];
        foreach (var region in regions)
        {
            if (normalizedX >= region.X && normalizedX <= region.X + region.Width &&
                normalizedY >= region.Y && normalizedY <= region.Y + region.Height)
                return new InteractionRegionHit(region.RegionId, region.Kind, region.SupportsLift);
        }
        return InteractionRegionHit.Default;
    }
}
