using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pupu.Application;
using Pupu.Behavior;
using Pupu.Desktop.Models;
using Pupu.Desktop.Services;

namespace Pupu.Desktop.ViewModels;

public enum PetDirection
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
public enum DesktopMoveMode
{
    HarnessedWalk,
    FreeRoam,
    AttentionRoam,
    AngryEscape,
    BroomFlight,
    Apparate,
    EdgePolish,
    AnchorApproach
}

public sealed class DesktopMoveRequestEventArgs : EventArgs
{
    public DesktopMoveRequestEventArgs(
        DesktopMoveMode mode,
        TimeSpan duration,
        CancellationToken cancellationToken,
        WindowSurfaceSnapshot? surface = null,
        DesktopPoint? target = null)
    {
        Mode = mode;
        Duration = duration;
        CancellationToken = cancellationToken;
        Surface = surface;
        Target = target;
    }

    public DesktopMoveMode Mode { get; }
    public bool FullWalk => Mode is DesktopMoveMode.HarnessedWalk or DesktopMoveMode.FreeRoam;
    public TimeSpan Duration { get; }
    public CancellationToken CancellationToken { get; }
    public WindowSurfaceSnapshot? Surface { get; }
    public DesktopPoint? Target { get; }
    public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class ActionGalleryItem
{
    public required string BehaviorId { get; init; }
    public required string AnimationSource { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string FrameLabel { get; init; }
    public string AvailabilityLabel { get; init; } = string.Empty;
    public required object Thumbnail { get; init; }
    public required ICommand PreviewCommand { get; init; }
}

public sealed class SeasonalGalleryItem
{
    public required string BehaviorId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string AvailabilityLabel { get; init; }
}

public sealed class ActionGalleryGroupItem
{
    public required string Name { get; init; }
    public ObservableCollection<ActionGalleryItem> Items { get; } = new();
}

public sealed class InformationCardItem
{
    public required string Title { get; init; }
    public required string Body { get; init; }
}

public sealed class AssetActionGroupViewItem
{
    public required AssetActionGroupStatus Status { get; init; }
    public required string GroupId { get; init; }
    public required string BehaviorId { get; init; }
    public required string Source { get; init; }
    public required string Timing { get; init; }
    public required string LoopMode { get; init; }
    public required string Fallback { get; init; }
    public required string Validation { get; init; }
    public required string Trigger { get; init; }
    public ObservableCollection<object> PreviewFrames { get; } = new();
}

public sealed class CoinStateViewItem
{
    public required string StateKey { get; init; }
    public required string Name { get; init; }
    public required string Coordinate { get; init; }
    public required string Duration { get; init; }
    public required object Preview { get; init; }
}

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private enum SpriteAtlas
    {
        Core,
        Life,
        Directions,
        Touch,
        Routines,
        WalkModes,
        Activity,
        LifeEquipment,
        Motion,
        GazeCoin,
        Litter,
        Specials,
        Seasonal
    }
    private enum FoodKind { Kibble, FreezeDried, Canned }

    private sealed record AnimationSequence(
        string Name,
        SpriteAtlas Atlas,
        int Row,
        int[] Frames,
        int[] FrameDurations)
    {
        public bool Loop { get; init; } = true;
        public object? ExternalSheet { get; init; }
        public int FrameWidth { get; init; } = 256;
        public int FrameHeight { get; init; } = 256;
        public bool VerticalStrip { get; init; }
        public bool AtlasRowSource { get; init; }
        public string ResolvedSource { get; init; } = string.Empty;
        public int DurationAt(int position) =>
            FrameDurations[Math.Min(position, FrameDurations.Length - 1)];
    }

    private sealed record DesktopBehaviorPresentation(
        AnimationSequence Sequence,
        string Label,
        string Bubble = "");

    private static AnimationSequence Sequence(
        string name,
        SpriteAtlas atlas,
        int row,
        int[] durations,
        params int[] frames) => new(name, atlas, row, frames, durations);

    private static AnimationSequence OneShot(AnimationSequence sequence) =>
        sequence with { Loop = false };

    private static readonly AnimationSequence IdleSequence = Sequence(
        "idle-breathe", SpriteAtlas.Core, 0,
        new[] { 720, 520, 680, 460, 760, 520, 420, 820 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence SideLieIdleSequence = Sequence(
        "side-lie-idle", SpriteAtlas.Motion, 9,
        new[] { 1750, 1450, 1900, 1550, 1800, 1500, 1650, 2100 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence ProneIdleSequence = Sequence(
        "prone-idle", SpriteAtlas.Routines, 1,
        new[] { 1500, 1200, 1650, 1400, 1750, 1300, 1450, 1900 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence PawNibbleSequence = Sequence(
        "paw-nibble", SpriteAtlas.Routines, 2,
        new[] { 900, 760, 820, 720, 880, 760, 840, 1150 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence RearIdleSequence = Sequence(
        "rear-idle", SpriteAtlas.Routines, 6,
        new[] { 1600, 1350, 1450, 1200, 1500, 1300, 1450, 1800 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence SideRearTransitionSequence = OneShot(Sequence(
        "side-rear-transition", SpriteAtlas.Routines, 7,
        new[] { 760, 560, 520, 460, 520, 620, 760, 1050 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence RollSequence = OneShot(Sequence(
        "roll", SpriteAtlas.Core, 1,
        new[] { 680, 420, 340, 360, 420, 520, 680, 880 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence SpinSequence = OneShot(Sequence(
        "spin", SpriteAtlas.Core, 2,
        new[] { 360, 280, 240, 240, 260, 280, 320, 520 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence WandSequence = Sequence(
        "wand", SpriteAtlas.Core, 3,
        new[] { 500, 260, 190, 180, 150, 140, 260, 460 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence WandIntroSequence = OneShot(Sequence(
        "wand-intro", SpriteAtlas.Core, 3,
        new[] { 760, 840 },
        0, 1));

    private static readonly AnimationSequence WandLoopSequence = Sequence(
        "wand-loop", SpriteAtlas.Core, 3,
        new[] { 460, 390, 340, 320, 430, 360, 340, 390 },
        2, 3, 4, 5, 6, 5, 4, 3);

    private static readonly AnimationSequence ExpressionSequence = OneShot(Sequence(
        "expressions", SpriteAtlas.Core, 4,
        new[] { 780, 620, 720, 420, 230, 480, 740, 880 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence RunLeftSequence = Sequence(
        "run-left", SpriteAtlas.Directions, 0,
        new[] { 150, 145, 140, 145, 150, 155, 160, 175 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence RunRightSequence = Sequence(
        "run-right", SpriteAtlas.Directions, 1,
        new[] { 150, 145, 140, 145, 150, 155, 160, 175 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence RunUpSequence = Sequence(
        "run-up", SpriteAtlas.Directions, 2,
        new[] { 170, 160, 155, 160, 170, 175, 185, 195 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence RunDownSequence = Sequence(
        "run-down", SpriteAtlas.Directions, 3,
        new[] { 170, 160, 155, 160, 170, 175, 185, 195 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence FeedSequence = OneShot(Sequence(
        "feed", SpriteAtlas.Life, 0,
        new[] { 520, 260, 210, 185, 180, 210, 360, 620 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence FeedIntroSequence = OneShot(Sequence(
        "feed-intro", SpriteAtlas.Life, 0,
        new[] { 760, 900 },
        0, 1));

    private static readonly AnimationSequence EatingLoopSequence = Sequence(
        "feed-loop", SpriteAtlas.Life, 0,
        new[] { 760, 690, 820, 720, 820, 690 },
        2, 3, 4, 5, 4, 3);

    private static readonly AnimationSequence KibbleEatingSequence = Sequence(
        "kibble-slow", SpriteAtlas.Routines, 3,
        new[] { 1150, 1450, 1900, 1250, 1750, 2100, 1350, 1650 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence FreezeDriedEatingSequence = OneShot(Sequence(
        "freeze-dried-pounce", SpriteAtlas.Routines, 4,
        new[] { 420, 260, 190, 180, 210, 230, 270, 360 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence FreezeDriedEatingLoopSequence = Sequence(
        "freeze-dried-eating-loop", SpriteAtlas.Routines, 4,
        new[] { 360, 290, 330, 420, 330, 290 },
        4, 5, 6, 7, 6, 5);

    private static readonly AnimationSequence CannedEatingSequence = OneShot(Sequence(
        "canned-pounce", SpriteAtlas.Routines, 5,
        new[] { 460, 240, 175, 165, 180, 210, 260, 420 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence CannedEatingLoopSequence = Sequence(
        "canned-eating-loop", SpriteAtlas.Routines, 5,
        new[] { 340, 270, 310, 390, 310, 270 },
        4, 5, 6, 7, 6, 5);

    private static readonly AnimationSequence HarnessWalkLeftSequence = WalkSequence("harness-left", 0);
    private static readonly AnimationSequence HarnessWalkRightSequence = WalkSequence("harness-right", 1);
    private static readonly AnimationSequence HarnessWalkUpSequence = WalkSequence("harness-back", 2, vertical: true);
    private static readonly AnimationSequence HarnessWalkDownSequence = WalkSequence("harness-front", 3, vertical: true);
    private static readonly AnimationSequence FreeWalkLeftSequence = WalkSequence("free-left", 4);
    private static readonly AnimationSequence FreeWalkRightSequence = WalkSequence("free-right", 5);
    private static readonly AnimationSequence FreeWalkUpSequence = WalkSequence("free-back", 6, vertical: true);
    private static readonly AnimationSequence FreeWalkDownSequence = WalkSequence("free-front", 7, vertical: true);
    private static readonly AnimationSequence HarnessWalkDownLeftSequence = MotionWalkSequence("harness-front-left", 0);
    private static readonly AnimationSequence HarnessWalkDownRightSequence = MotionWalkSequence("harness-front-right", 1);
    private static readonly AnimationSequence HarnessWalkUpLeftSequence = MotionWalkSequence("harness-rear-left", 2);
    private static readonly AnimationSequence HarnessWalkUpRightSequence = MotionWalkSequence("harness-rear-right", 3);
    private static readonly AnimationSequence FreeWalkDownLeftSequence = MotionWalkSequence("free-front-left", 4);
    private static readonly AnimationSequence FreeWalkDownRightSequence = MotionWalkSequence("free-front-right", 5);
    private static readonly AnimationSequence FreeWalkUpLeftSequence = MotionWalkSequence("free-rear-left", 6);
    private static readonly AnimationSequence FreeWalkUpRightSequence = MotionWalkSequence("free-rear-right", 7);

    private static AnimationSequence WalkSequence(string name, int row, bool vertical = false) => Sequence(
        name, SpriteAtlas.WalkModes, row,
        vertical
            ? new[] { 185, 170, 165, 170, 180, 175, 185, 195 }
            : new[] { 165, 150, 145, 150, 160, 155, 165, 180 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static AnimationSequence MotionWalkSequence(string name, int row) => Sequence(
        name, SpriteAtlas.Motion, row,
        new[] { 160, 145, 140, 145, 155, 150, 160, 175 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence CleanSequence = OneShot(Sequence(
        "clean", SpriteAtlas.Life, 1,
        new[] { 540, 280, 240, 220, 260, 320, 420, 680 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence CleanIntroSequence = OneShot(Sequence(
        "clean-intro", SpriteAtlas.Life, 1,
        new[] { 850, 920 },
        0, 1));

    private static readonly AnimationSequence CleaningLoopSequence = Sequence(
        "clean-loop", SpriteAtlas.Life, 1,
        new[] { 820, 720, 780, 900, 760, 940, 760, 900, 780, 720 },
        1, 2, 3, 4, 5, 6, 5, 4, 3, 2);

    private static readonly AnimationSequence ToiletEnterSequence = OneShot(Sequence(
        "toilet-enter", SpriteAtlas.Litter, 0,
        new[] { 620, 520, 500, 620 },
        0, 1, 2, 3));

    private static readonly AnimationSequence ToiletRelieveSequence = Sequence(
        "toilet-relieve", SpriteAtlas.Litter, 1,
        new[] { 1150, 1250, 1180, 1320, 1180, 1250 },
        2, 3, 4, 5, 4, 3);

    private static readonly AnimationSequence ToiletLookUpSequence = OneShot(Sequence(
        "toilet-look-up", SpriteAtlas.Litter, 2,
        new[] { 620, 520, 480, 620, 720, 560, 520, 720 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence ToiletBurySequence = OneShot(Sequence(
        "toilet-bury", SpriteAtlas.Litter, 3,
        new[] { 280, 230, 210, 200, 210, 240, 300 },
        0, 1, 2, 3, 4, 5, 6));

    private static readonly AnimationSequence ToiletExitSequence = OneShot(Sequence(
        "toilet-exit", SpriteAtlas.Litter, 3,
        new[] { 420, 560, 760 },
        5, 6, 7));

    private static readonly AnimationSequence FurGroomSequence = Sequence(
        "fur-groom-daily", SpriteAtlas.LifeEquipment, 0,
        new[] { 980, 760, 820, 720, 900, 760, 860, 1120 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence BlueBedSleepSequence = Sequence(
        "blue-bed-sleep", SpriteAtlas.LifeEquipment, 1,
        new[] { 2200, 1800, 2400, 1950, 2300, 1850 },
        4, 5, 6, 7, 6, 5);

    private static readonly AnimationSequence HappyPetSequence = OneShot(Sequence(
        "happy-petting", SpriteAtlas.Touch, 0,
        new[] { 560, 420, 720 },
        0, 1, 2));

    private static readonly AnimationSequence OverPetSequence = OneShot(Sequence(
        "over-petting", SpriteAtlas.Touch, 3,
        new[] { 420, 560, 230, 190, 170, 680 },
        2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence GroomSequence = OneShot(Sequence(
        "groom", SpriteAtlas.Life, 3,
        new[] { 520, 360, 300, 320, 400, 520, 720 },
        1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence GroomIntroSequence = OneShot(Sequence(
        "groom-intro", SpriteAtlas.Life, 3,
        new[] { 780, 940 },
        1, 2));

    private static readonly AnimationSequence GroomingLoopSequence = Sequence(
        "groom-loop", SpriteAtlas.Life, 3,
        new[] { 820, 740, 880, 760, 880, 740 },
        2, 3, 4, 5, 4, 3);

    private static readonly AnimationSequence AttentionSequence = OneShot(Sequence(
        "attention", SpriteAtlas.Life, 4,
        new[] { 980, 760, 680, 620, 240, 320, 480, 920 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence MischiefSequence = OneShot(Sequence(
        "mischief", SpriteAtlas.Life, 5,
        new[] { 760, 420, 240, 200, 280, 560, 1080 },
        0, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence FocusSequence = OneShot(Sequence(
        "focus", SpriteAtlas.Life, 6,
        new[] { 1150, 900, 820, 1050, 720, 920, 1500 },
        0, 1, 2, 3, 4, 5, 6));

    private static readonly AnimationSequence SleepSequence = Sequence(
        "sleep-snore", SpriteAtlas.Life, 6,
        new[] { 2100, 1700, 2400, 1800 },
        4, 5, 6, 5);

    private static readonly AnimationSequence LaserAnchorChaseSequence = Sequence(
        "laser-chase-8", SpriteAtlas.Directions, 0,
        new[] { 165, 165, 165, 165 },
        0, 1, 2, 3);

    private static readonly AnimationSequence SnackAnchorChaseSequence = Sequence(
        "snack-chase-8", SpriteAtlas.Directions, 0,
        new[] { 165, 165, 165, 165 },
        0, 1, 2, 3);

    private static readonly AnimationSequence LaserPounceSequence = Sequence(
        "laser-pounce", SpriteAtlas.Activity, 0,
        new[] { 360, 280, 240, 260, 320, 260, 240, 300 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence SleepCurledSequence = Sequence(
        "sleep-curled", SpriteAtlas.Activity, 2,
        new[] { 2300, 1900, 2500, 2100, 2400, 1850, 2550, 2050, 2550, 1850, 2400, 2100, 2500, 1900 },
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 1);

    private static readonly AnimationSequence SleepBellyUpSequence = Sequence(
        "sleep-belly-up", SpriteAtlas.Activity, 3,
        new[] { 2200, 1800, 2450, 2000, 2350, 1750, 2500, 2100, 2500, 1750, 2350, 2000, 2450, 1800 },
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 1);

    private static readonly AnimationSequence SleepSideSequence = Sequence(
        "sleep-side", SpriteAtlas.Activity, 4,
        new[] { 2350, 1850, 2550, 2050, 2450, 1800, 2600, 2150, 2600, 1800, 2450, 2050, 2550, 1850 },
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 1);

    private static readonly AnimationSequence SleepTransitionSequence = OneShot(Sequence(
        "sleep-transition", SpriteAtlas.Activity, 5,
        new[] { 680, 560, 520, 500, 560, 620, 720, 920 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence SplootSequence = Sequence(
        "sploot", SpriteAtlas.Activity, 6,
        new[] { 1500, 1250, 1650, 1350, 1550, 1300, 1450, 1750, 1450, 1300, 1550, 1350, 1650, 1250 },
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 1);

    private static readonly AnimationSequence CageRestSequence = Sequence(
        "cage-rest-12", SpriteAtlas.Routines, 1,
        new[] { 1350, 1200, 1450, 1250, 1500, 1200, 1400, 1650 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence AccioBroomIntroSequence = OneShot(Sequence(
        "magic-accio-broom-intro", SpriteAtlas.Specials, 1,
        new[] { 620, 520, 500, 480, 520, 620 },
        0, 1, 2, 3, 4, 5));

    private static readonly AnimationSequence BroomFlightSequence = Sequence(
        "magic-accio-broom-flight-right", SpriteAtlas.Motion, 8,
        new[] { 180 },
        4);

    private static readonly AnimationSequence BroomFlightLeftSequence = BroomDirectionSequence("left", 0);
    private static readonly AnimationSequence BroomFlightDownLeftSequence = BroomDirectionSequence("front-left", 1);
    private static readonly AnimationSequence BroomFlightDownSequence = BroomDirectionSequence("front", 2);
    private static readonly AnimationSequence BroomFlightDownRightSequence = BroomDirectionSequence("front-right", 3);
    private static readonly AnimationSequence BroomFlightRightSequence = BroomDirectionSequence("right", 4);
    private static readonly AnimationSequence BroomFlightUpRightSequence = BroomDirectionSequence("rear-right", 5);
    private static readonly AnimationSequence BroomFlightUpSequence = BroomDirectionSequence("rear", 6);
    private static readonly AnimationSequence BroomFlightUpLeftSequence = BroomDirectionSequence("rear-left", 7);

    private static AnimationSequence BroomDirectionSequence(string direction, int frame) => Sequence(
        $"magic-accio-broom-flight-{direction}", SpriteAtlas.Motion, 8,
        new[] { 180 },
        frame);

    private static readonly AnimationSequence ApparateSequence = OneShot(Sequence(
        "magic-apparate", SpriteAtlas.Specials, 2,
        new[] { 520, 420, 360, 340, 520, 520, 420, 620 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence ApparateReappearSequence = OneShot(Sequence(
        "magic-apparate-reappear", SpriteAtlas.Specials, 2,
        new[] { 460, 520, 620 },
        5, 6, 7));

    private static readonly AnimationSequence PetrifySequence = OneShot(Sequence(
        "magic-petrificus-totalus", SpriteAtlas.Specials, 3,
        new[] { 620, 520, 520, 560, 620, 720 },
        0, 1, 2, 3, 4, 5));

    private static readonly AnimationSequence SilverCoinSequence = Sequence(
        "magic-petrificus-coin-front", SpriteAtlas.GazeCoin, 2,
        new[] { 920, 1180 },
        0, 1);

    private static readonly AnimationSequence SilverCoinBackSequence = Sequence(
        "magic-petrificus-coin-back", SpriteAtlas.GazeCoin, 2,
        new[] { 980, 1220 },
        4, 5);

    private static readonly AnimationSequence PetrificationReleaseStretchSequence = OneShot(Sequence(
        "magic-petrificus-release-stretch", SpriteAtlas.Core, 5,
        new[] { 480, 420, 380, 420, 520, 620, 720, 900 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence ScourgifySequence = Sequence(
        "magic-scourgify", SpriteAtlas.Specials, 4,
        new[] { 420, 320, 260, 240, 280, 320, 360, 460 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence ChristmasSequence = Sequence(
        "seasonal-christmas", SpriteAtlas.Seasonal, 0,
        new[] { 1200, 980, 1080, 940, 1120, 820, 960, 1280 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence HalloweenSequence = Sequence(
        "seasonal-halloween", SpriteAtlas.Seasonal, 1,
        new[] { 1050, 920, 980, 880, 1060, 760, 920, 1160 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence SpringFestivalSequence = Sequence(
        "seasonal-spring-festival", SpriteAtlas.Seasonal, 2,
        new[] { 1180, 960, 1040, 920, 1100, 780, 940, 1240 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence BirthdaySequence = Sequence(
        "seasonal-owner-birthday", SpriteAtlas.Seasonal, 3,
        new[] { 760, 620, 540, 520, 560, 620, 740, 980 },
        0, 1, 2, 3, 4, 5, 6, 7);

    private static readonly AnimationSequence AskWalkSequence = OneShot(Sequence(
        "ask-walk", SpriteAtlas.LifeEquipment, 2,
        new[] { 560, 340, 260, 300, 420, 520, 680, 820 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence GentleTouchSequence = OneShot(Sequence(
        "gentle-touch", SpriteAtlas.Touch, 0,
        new[] { 620, 520, 650, 720, 620, 560, 680, 820 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence PurrSequence = Sequence(
        "purr", SpriteAtlas.Touch, 1,
        new[] { 820, 760, 700, 760, 860, 920, 860, 780, 720, 780 },
        0, 1, 2, 3, 4, 5, 6, 7, 6, 5);

    private static readonly AnimationSequence CuriousTouchSequence = OneShot(Sequence(
        "curious-touch", SpriteAtlas.Touch, 2,
        new[] { 620, 650, 720, 720, 560, 740, 680, 820 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence AnnoyedTouchSequence = OneShot(Sequence(
        "annoyed-touch", SpriteAtlas.Touch, 3,
        new[] { 620, 520, 560, 620, 520, 640, 760, 980 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence AngryTouchSequence = OneShot(Sequence(
        "angry-touch", SpriteAtlas.Touch, 4,
        new[] { 520, 420, 440, 360, 520, 420, 360, 700 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private static readonly AnimationSequence TrustTouchSequence = OneShot(Sequence(
        "trust-touch", SpriteAtlas.Touch, 5,
        new[] { 680, 620, 720, 720, 820, 860, 980, 1200 },
        0, 1, 2, 3, 4, 5, 6, 7));

    private readonly LocalPetStore _store = new();
    private readonly MemoryEngine _memory;
    private readonly PetSpeechComposer _speech = new();
    private readonly IModelApiService _modelApi;
    private readonly ICodexIterationService _codexIteration;
    private readonly IDesktopPresentationHost _presentationHost;
    private readonly IDesktopEnvironmentProbe _desktopEnvironmentProbe;
    private readonly IUiTimer _animationTimer;
    private readonly IUiTimer _needsTimer;
    private readonly IUiTimer _autonomyTimer;
    private readonly IAssetPackService _assetPack;
    private readonly Random _random = new();
    private readonly IClock _clock;
    private readonly GestureInterpreter _gestureInterpreter;
    private readonly GestureStateUpdater _gestureStateUpdater = new();
    private readonly ContextualInteractionEvaluator _interactionEvaluator = new();
    private readonly OwnerInteractionParticipationEvaluator _participationEvaluator = new();
    private readonly DailyToiletPlanner _dailyToiletPlanner = new();
    private readonly IRandomSource _dailyToiletRandom = new SystemRandomSource();
    private readonly BehaviorDecisionLogger _decisionLogger;
    private readonly PetBehaviorRuntime _behaviorRuntime;
    private BehaviorArbitrator _behaviorArbitrator => _behaviorRuntime.Arbitrator;
    private PetAgentKernel _agentKernel => _behaviorRuntime.Kernel;
    private readonly IBehaviorPresentationResolver<DesktopBehaviorPresentation>
        _presentationResolver;
    private BehaviorProposalQueue _behaviorProposalQueue => _behaviorRuntime.ProposalQueue;
    private BehaviorProposalExecutor _behaviorProposalExecutor => _behaviorRuntime.ProposalExecutor;
    private PersonaDefinition _persona = PersonaDefinition.CreateDefaultPupu();
    private readonly LocalInteractionCommandParser _localCommandParser = new();
    private readonly ActionScheduler _actionScheduler = new();
    private readonly InteractionLifecycle _interactionLifecycle;
    private readonly InteractionSessionManager _interactionSessions;
    private readonly PerceptionEventProcessor _perception = new();
    private readonly InteractionRegionMap _interactionRegions = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _bubbleCancellation;
    private CancellationTokenSource? _touchReactionCancellation;
    private AnimationSequence _currentSequence = SideLieIdleSequence;
    private int _framePosition;
    private bool _synchronizedMovement;
    private bool _activeAnchorIsFood;
    private DateTimeOffset _nextAutonomousActionAt = DateTimeOffset.Now.AddSeconds(20);
    private DateTimeOffset _lastAutonomousMessageAt = DateTimeOffset.MinValue;
    private bool _busyAction;
    private bool _isTouchEscaping;
    private bool _isCursorGazeActive;
    private int _cursorGazeFrame = -1;
    private int _pendingCursorGazeFrame = -1;
    private int _pendingCursorGazeSamples;
    private DateTimeOffset _cursorGazeFrameChangedAt = DateTimeOffset.MinValue;
    private int _cursorGazeTailPhase;
    private DateTimeOffset _nextCursorGazeTailPhaseAt = DateTimeOffset.MinValue;
    private bool _isPetrified;
    private bool _isCoinBackVisible;
    private bool _isCoinFlipRunning;
    private double _coinFlipScaleX = 1;
    private double _interactionScale = 1;
    private InteractionSession? _petrificationSession;
    private bool _calendarSpecialRunning;
    private DesktopMoveMode? _activeMoveMode;
    private bool _disposed;
    private ScheduledAction? _scheduledAction;
    private InteractionSession? _activeInteraction;
    private DateTimeOffset _currentBehaviorStartedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastActiveStateTickAt = DateTimeOffset.Now;
    private DateTimeOffset _initiativeCooldownUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextCursorAttentionArbitrationAt = DateTimeOffset.MinValue;
    private bool _lastInitiativeWasIgnored;
    private PetDirection _currentDirection = PetDirection.Right;
    private string _currentInteractionType = "autonomous";
    private string _currentBehaviorContext = "general";
    private string _currentAnimationSource = "motion:side-lie-idle";
    private MouseInteractionMode _mouseInteractionMode = MouseInteractionMode.Attention;
    private string _cursorAttentionStatus = "普通注意力：等待鼠标靠近";
    private string _lastArbitrationResult = "尚无行为请求";
    private string _currentIntent = "idle";
    private string _lastProposalResult = "尚无行为提案";
    private string _travelDestinationInput = string.Empty;
    private double _travelDurationHours = 1;
    private DesktopEnvironmentSnapshot _desktopEnvironment = DesktopEnvironmentSnapshot.Empty;
    private object? _petFrame;
    private string _bubbleText = "朴朴正在醒来…";
    private bool _isBubbleVisible = true;
    private bool _areQuickActionsVisible;
    private bool _isReady;
    private bool _isChatBusy;
    private bool _isChatComposerVisible;
    private string _chatInput = string.Empty;
    private DateTimeOffset _coinColorRefreshedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextAutomaticCoinRefreshAt = DateTimeOffset.MaxValue;
    private ModelApiSettings _modelApiSettings = new();
    private string _modelApiKey = string.Empty;
    private string _modelApiStatus = "模型对话尚未配置。未启用时，朴朴仍会使用本地性格台词。";
    private string _correctionNote = string.Empty;
    private string _currentBehaviorKey = "idle";
    private string _currentBehaviorLabel = "原地趴着，缓慢呼吸和眨眼";
    private string _memoryStatus = "正在加载…";
    private string _naturalRuleInput = string.Empty;
    private string _naturalRuleStatus = "可以直接描述你希望pupu如何表现，规则与记忆只保存在本机。";
    private string _editableMemoryText = string.Empty;
    private string _editableMemoryStatus = "Markdown 长期记忆正在加载…";
    private string _codexIterationRequest = string.Empty;
    private string _codexProjectPath = string.Empty;
    private string _codexIterationStatus = "写下新动作或设定后，可生成一份带当前性格与记忆上下文的 Codex 任务。";
    private string _assetPackStatus = "正在读取动作素材包…";
    private AssetActionGroupViewItem? _selectedAssetActionGroup;
    private PersonalityTraits _editableTraits = new();
    private PetProfile _editableProfile = new();

    public MainViewModel(
        IDesktopPresentationHost presentationHost,
        IAssetPackService assetPack,
        IModelApiService modelApi,
        ICodexIterationService codexIteration,
        IDesktopEnvironmentProbe desktopEnvironmentProbe,
        IClock? clock = null,
        IRandomSource? randomSource = null)
    {
        _presentationHost = presentationHost ?? throw new ArgumentNullException(nameof(presentationHost));
        _assetPack = assetPack ?? throw new ArgumentNullException(nameof(assetPack));
        _modelApi = modelApi ?? throw new ArgumentNullException(nameof(modelApi));
        _codexIteration = codexIteration ?? throw new ArgumentNullException(nameof(codexIteration));
        _desktopEnvironmentProbe = desktopEnvironmentProbe ?? throw new ArgumentNullException(nameof(desktopEnvironmentProbe));
        _decisionLogger = new BehaviorDecisionLogger(_presentationHost.ReportRecoverableException);
        _clock = clock ?? new SystemClock();
        var behaviorRandom = randomSource ?? new SystemRandomSource();
        _gestureInterpreter = new GestureInterpreter(_clock);
        _interactionSessions = new InteractionSessionManager(_clock);
        _memory = new MemoryEngine(_store, _clock);
        _behaviorRuntime = new PetBehaviorRuntime(
            _memory,
            _memory,
            _persona,
            behaviorRandom);
        _presentationResolver = BuildPresentationResolver();
        _interactionLifecycle = new InteractionLifecycle(_clock, _memory.RecordInteractionAsync);
        _assetPackStatus = _assetPack.DisplayStatus;

        _animationTimer = _presentationHost.CreateTimer(TimeSpan.FromMilliseconds(600));
        _animationTimer.Tick += (_, _) => RenderNextFrame();
        _needsTimer = _presentationHost.CreateTimer(TimeSpan.FromMinutes(1));
        _needsTimer.Tick += async (_, _) => await UpdateNeedsAsync();
        _autonomyTimer = _presentationHost.CreateTimer(TimeSpan.FromSeconds(6));
        _autonomyTimer.Tick += async (_, _) => await RunAutonomyAsync();

        FeedCommand = FeedKibbleCommand = ActionCommand(() => FeedAsync(FoodKind.Kibble));
        FeedFreezeDriedCommand = ActionCommand(() => FeedAsync(FoodKind.FreezeDried));
        FeedCannedCommand = ActionCommand(() => FeedAsync(FoodKind.Canned));
        WalkCommand = HarnessWalkCommand = WalkActionCommand(() => WalkAsync(DesktopMoveMode.HarnessedWalk));
        FreeRoamCommand = WalkActionCommand(() => WalkAsync(DesktopMoveMode.FreeRoam));
        // Retained as a disabled compatibility command for old bindings. Litter
        // use is autonomous from this release onward and never creates a care
        // debt for the owner.
        CleanCommand = AsyncCommand(
            () => Task.CompletedTask,
            () => false);
        PetCommand = new RelayCommand(RegisterPetClick, () => IsReady && !_busyAction && !_isTouchEscaping);
        GroomCommand = ActionCommand(GroomAsync);
        PlayWandCommand = ActionCommand(PlayWandAsync);
        PlayLaserCommand = ActionCommand(PlayLaserAsync);
        LieDownCommand = ActionCommand(LieDownAsync);
        RollCommand = ActionCommand(RollAsync);
        SpinCommand = ActionCommand(SpinAsync);
        AccioBroomCommand = ActionCommand(() => AccioBroomAsync(false));
        ApparateCommand = ActionCommand(() => ApparateAsync(false));
        PetrificusTotalusCommand = ActionCommand(() => PetrificusTotalusAsync(false));
        ScourgifyCommand = ActionCommand(() => ScourgifyAsync(false));
        ReleasePetrificationCommand = AsyncCommand(
            ReleasePetrificationAsync,
            () => IsReady && IsPetrified);
        StartFoodAnchorCommand = new RelayCommand(
            () => ActivateAnchorMode(MouseInteractionMode.FoodAnchor),
            () => IsReady && !IsCaged && !IsTraveling && !_busyAction);
        StartToyAnchorCommand = new RelayCommand(
            () => ActivateAnchorMode(MouseInteractionMode.ToyAnchor),
            () => IsReady && !IsCaged && !IsTraveling && !_busyAction);
        CancelMouseModeCommand = new RelayCommand(
            CancelAnchorMode,
            () => MouseInteractionMode is not MouseInteractionMode.Attention);
        CageCommand = AsyncCommand(
            CageAsync,
            () => IsReady && !IsCaged && !IsTraveling);
        ReleaseCageCommand = AsyncCommand(
            ReleaseCageAsync,
            () => IsReady && IsCaged);
        StartTravelCommand = AsyncCommand(
            () => StartTravelAsync(TravelDestinationInput, TimeSpan.FromHours(TravelDurationHours)),
            () => IsReady && !IsTraveling && !IsCaged);
        RecallTravelCommand = AsyncCommand(
            () => ReturnFromTravelAsync(recalled: true),
            () => IsReady && IsTraveling);
        StopCurrentActionCommand = new RelayCommand(StopCurrentAction, () => IsReady);
        ToggleQuickActionsCommand = new RelayCommand(() => AreQuickActionsVisible = !AreQuickActionsVisible);
        ToggleChatComposerCommand = new RelayCommand(() => IsChatComposerVisible = !IsChatComposerVisible);
        SendChatCommand = AsyncCommand(SendChatAsync, () => IsReady && !IsChatBusy && !string.IsNullOrWhiteSpace(ChatInput));
        SaveModelApiCommand = AsyncCommand(SaveModelApiAsync, () => IsReady && !IsChatBusy);
        TestModelApiCommand = AsyncCommand(TestModelApiAsync, () => IsReady && !IsChatBusy);
        DeleteModelApiKeyCommand = AsyncCommand(DeleteModelApiKeyAsync, () => IsReady && !IsChatBusy && HasStoredModelApiKey);
        ApplyNaturalRuleCommand = AsyncCommand(ApplyNaturalRuleAsync, () => IsReady && !string.IsNullOrWhiteSpace(NaturalRuleInput));
        SaveEditableMemoryCommand = AsyncCommand(SaveEditableMemoryAsync, () => IsReady && !string.IsNullOrWhiteSpace(EditableMemoryText));
        ReloadEditableMemoryCommand = AsyncCommand(ReloadEditableMemoryAsync, () => IsReady);
        OpenEditableMemoryCommand = new RelayCommand(OpenEditableMemoryFile, () => IsReady);
        CreateCodexIterationCommand = AsyncCommand(CreateCodexIterationAsync, () => IsReady && !string.IsNullOrWhiteSpace(CodexIterationRequest));
        LikeBehaviorCommand = AsyncCommand(() => CorrectBehaviorAsync(1), () => IsReady);
        DislikeBehaviorCommand = AsyncCommand(() => CorrectBehaviorAsync(-1), () => IsReady);
        UndoCorrectionCommand = AsyncCommand(UndoCorrectionAsync, () => IsReady);
        SavePersonalityCommand = AsyncCommand(SavePersonalityAsync, () => IsReady);
        SavePetProfileCommand = AsyncCommand(SavePetProfileAsync, () => IsReady);
        ResetLearningCommand = AsyncCommand(ResetLearningAsync, () => IsReady);
        ZoomInCommand = AsyncCommand(() => ChangeScaleAsync(0.1), () => IsReady);
        ZoomOutCommand = AsyncCommand(() => ChangeScaleAsync(-0.1), () => IsReady);
        ResetZoomCommand = AsyncCommand(ResetScaleAsync, () => IsReady);
        OpenControlPanelCommand = new RelayCommand(() => ControlPanelRequested?.Invoke(this, EventArgs.Empty));
        OpenMemoryFolderCommand = new RelayCommand(OpenMemoryFolder);
        OpenAssetFolderCommand = new RelayCommand(OpenAssetFolder);
        ExitCommand = new RelayCommand(_presentationHost.Shutdown);

        BuildActionGallery();
        BuildAssetActionGroups();
        BuildCoinUpdateStates();
        BuildInformationCards();
        PlaySequence(SideLieIdleSequence);
        _ = InitializeAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DesktopMoveRequestEventArgs>? DesktopMoveRequested;
    public event EventHandler? ControlPanelRequested;

    public ICommand FeedCommand { get; }
    public ICommand FeedKibbleCommand { get; }
    public ICommand FeedFreezeDriedCommand { get; }
    public ICommand FeedCannedCommand { get; }
    public ICommand WalkCommand { get; }
    public ICommand HarnessWalkCommand { get; }
    public ICommand FreeRoamCommand { get; }
    public ICommand CleanCommand { get; }
    public bool IsManualLitterCleaningAvailable => false;
    public ICommand PetCommand { get; }
    public ICommand GroomCommand { get; }
    public ICommand PlayWandCommand { get; }
    public ICommand PlayLaserCommand { get; }
    public ICommand LieDownCommand { get; }
    public ICommand RollCommand { get; }
    public ICommand SpinCommand { get; }
    public ICommand AccioBroomCommand { get; }
    public ICommand ApparateCommand { get; }
    public ICommand PetrificusTotalusCommand { get; }
    public ICommand ScourgifyCommand { get; }
    public ICommand ReleasePetrificationCommand { get; }
    public ICommand StartFoodAnchorCommand { get; }
    public ICommand StartToyAnchorCommand { get; }
    public ICommand CancelMouseModeCommand { get; }
    public ICommand CageCommand { get; }
    public ICommand ReleaseCageCommand { get; }
    public ICommand StartTravelCommand { get; }
    public ICommand RecallTravelCommand { get; }
    public ICommand StopCurrentActionCommand { get; }
    public ICommand ToggleQuickActionsCommand { get; }
    public ICommand ToggleChatComposerCommand { get; }
    public ICommand SendChatCommand { get; }
    public ICommand SaveModelApiCommand { get; }
    public ICommand TestModelApiCommand { get; }
    public ICommand DeleteModelApiKeyCommand { get; }
    public ICommand ApplyNaturalRuleCommand { get; }
    public ICommand SaveEditableMemoryCommand { get; }
    public ICommand ReloadEditableMemoryCommand { get; }
    public ICommand OpenEditableMemoryCommand { get; }
    public ICommand CreateCodexIterationCommand { get; }
    public ICommand LikeBehaviorCommand { get; }
    public ICommand DislikeBehaviorCommand { get; }
    public ICommand UndoCorrectionCommand { get; }
    public ICommand SavePersonalityCommand { get; }
    public ICommand SavePetProfileCommand { get; }
    public ICommand ResetLearningCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ResetZoomCommand { get; }
    public ICommand OpenControlPanelCommand { get; }
    public ICommand OpenMemoryFolderCommand { get; }
    public ICommand OpenAssetFolderCommand { get; }
    public ICommand ExitCommand { get; }

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();
    public ObservableCollection<string> NaturalRules { get; } = new();
    public ObservableCollection<string> HiddenActionRules { get; } = new();
    public ObservableCollection<string> LearnedPreferenceItems { get; } = new();
    public ObservableCollection<string> BehaviorScoreItems { get; } = new();
    public ObservableCollection<string> ArbitrationItems { get; } = new();
    public ObservableCollection<string> BehaviorProposalItems { get; } = new();
    public ObservableCollection<ActionGalleryItem> ActionGallery { get; } = new();
    public ObservableCollection<ActionGalleryGroupItem> RegularActionGalleryGroups { get; } = new();
    public ObservableCollection<ActionGalleryItem> AutonomousActionGallery { get; } = new();
    public ObservableCollection<ActionGalleryItem> InteractiveActionGallery { get; } = new();
    public ObservableCollection<ActionGalleryItem> MagicActionGallery { get; } = new();
    public ObservableCollection<SeasonalGalleryItem> SeasonalActionGallery { get; } = new();
    public ObservableCollection<InformationCardItem> ProductDesignCards { get; } = new();
    public ObservableCollection<InformationCardItem> CodeImplementationCards { get; } = new();
    public ObservableCollection<AssetActionGroupViewItem> AssetActionGroups { get; } = new();
    public ObservableCollection<CoinStateViewItem> CoinUpdateStates { get; } = new();
    public IReadOnlyList<ModelProvider> ModelProviderOptions { get; } =
        Enum.GetValues<ModelProvider>();
    public IReadOnlyList<ModelApiFormat> ModelApiFormatOptions { get; } =
        Enum.GetValues<ModelApiFormat>();
    public AssetActionGroupViewItem? SelectedAssetActionGroup
    {
        get => _selectedAssetActionGroup;
        set => SetField(ref _selectedAssetActionGroup, value);
    }

    public object? PetFrame
    {
        get => _petFrame;
        private set => SetField(ref _petFrame, value);
    }
    public string BubbleText
    {
        get => _bubbleText;
        private set => SetField(ref _bubbleText, value);
    }

    public bool IsBubbleVisible
    {
        get => _isBubbleVisible;
        private set => SetField(ref _isBubbleVisible, value);
    }

    public bool AreQuickActionsVisible
    {
        get => _areQuickActionsVisible;
        set => SetField(ref _areQuickActionsVisible, value);
    }

    public bool IsChatComposerVisible
    {
        get => _isChatComposerVisible;
        set => SetField(ref _isChatComposerVisible, value);
    }

    public bool IsLongActionRunning => _busyAction;
    public bool IsPetrified => _isPetrified;
    public bool IsCaged => IsReady && _memory.State.IsCaged;
    public bool IsTraveling => IsReady && _memory.State.Travel.IsTraveling;
    public bool IsPetOnDesktop => !IsTraveling;
    public bool IsMovementLocked => IsCaged || IsTraveling;
    public string ConfinementStatus => IsCaged
        ? "关笼子／锁定中：不会移动或切换普通大姿态，需主人释放。"
        : "未关笼子";
    public string TravelStatus
    {
        get
        {
            if (!IsTraveling)
                return string.IsNullOrWhiteSpace(_memory.State.Travel.LastStory)
                    ? "朴朴目前在桌面上。"
                    : $"已回到桌面。上次经历：{_memory.State.Travel.LastStory}";
            var remaining = _memory.State.Travel.ReturnsAt is { } returnsAt
                ? returnsAt - _clock.Now
                : TimeSpan.Zero;
            return $"外出中 · {_memory.State.Travel.Destination} · " +
                   $"预计 {Math.Max(0, remaining.TotalMinutes):0} 分钟后回来";
        }
    }
    public string AwayDesktopStatus => IsTraveling
        ? $"朴朴正在{_memory.State.Travel.Destination}旅行 · 打开面板可召回"
        : string.Empty;
    public MouseInteractionMode MouseInteractionMode
    {
        get => _mouseInteractionMode;
        private set
        {
            if (!SetField(ref _mouseInteractionMode, value)) return;
            OnPropertyChanged(nameof(MouseInteractionModeLabel));
            RaiseCommands();
        }
    }
    public string MouseInteractionModeLabel => MouseInteractionMode switch
    {
        MouseInteractionMode.FoodAnchor => "食物锚点：点击桌面位置投放",
        MouseInteractionMode.ToyAnchor => "玩具锚点：点击桌面位置引导",
        _ => "普通注意力：只记录方向，不抢动作"
    };
    public string CursorAttentionStatus
    {
        get => _cursorAttentionStatus;
        private set => SetField(ref _cursorAttentionStatus, value);
    }
    public string LastArbitrationResult
    {
        get => _lastArbitrationResult;
        private set => SetField(ref _lastArbitrationResult, value);
    }
    public string CurrentIntent
    {
        get => _currentIntent;
        private set => SetField(ref _currentIntent, value);
    }
    public string LastProposalResult
    {
        get => _lastProposalResult;
        private set => SetField(ref _lastProposalResult, value);
    }
    public string CurrentPersonaSummary =>
        $"{_persona.DisplayName} · {_persona.Id} · {_persona.Identity} · {_persona.SpeakingStyle}";
    public string CurrentPromptPreview =>
        $"{_persona.PromptSummary()} 只注入相关长期记忆摘要与最多三条相册经历摘要；不包含本地绝对路径。";
    public int CurrentPromptTokenEstimate =>
        Math.Max(1, (int)Math.Ceiling(CurrentPromptPreview.Length / 3.2));
    public string LlmFallbackReason => _modelApiSettings.Enabled
        ? "模型已启用；失败时回退本地规则和 Persona。"
        : "模型未启用：当前使用本地规则 PetAgent。";
    public string AssetCompatibilityStatus => _assetPack.CompatibilityStatus;
    public string AssetGenerationRequirements =>
        "V18 素材契约：每格 256×256 RGBA，四边至少 20px 透明安全区；同一动作行共享身体尺度、重心和脚底线，完整保留耳朵、四肢与大尾巴。循环动作使用语义帧率与往返序列，八方向追逐每个方向至少四个独立步态相位。正式文件只由 pupu-assets.json 引用，旧底图退出运行目录但保留在 Git 历史中；替换时必须同时校验行为 ID、动作回退、预览分类和运行引用。";
    public string TravelDestinationInput
    {
        get => _travelDestinationInput;
        set => SetField(ref _travelDestinationInput, value);
    }
    public double TravelDurationHours
    {
        get => _travelDurationHours;
        set => SetField(ref _travelDurationHours, Math.Clamp(value, 0.25, 24));
    }
    public double CoinFlipScaleX
    {
        get => _coinFlipScaleX;
        private set => SetField(ref _coinFlipScaleX, value);
    }
    public double InteractionScale
    {
        get => _interactionScale;
        private set => SetField(ref _interactionScale, value);
    }
    public bool IsHarnessedWalkActive => _activeMoveMode == DesktopMoveMode.HarnessedWalk;
    public bool IsFreeRoamActive => _activeMoveMode == DesktopMoveMode.FreeRoam;

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (!SetField(ref _isReady, value)) return;
            RaiseCommands();
        }
    }

    public bool IsChatBusy
    {
        get => _isChatBusy;
        private set
        {
            if (!SetField(ref _isChatBusy, value)) return;
            RaiseCommands();
        }
    }

    public string ChatInput
    {
        get => _chatInput;
        set
        {
            if (!SetField(ref _chatInput, value)) return;
            RaiseCommands();
        }
    }

    public bool ModelApiEnabled
    {
        get => _modelApiSettings.Enabled;
        set
        {
            if (_modelApiSettings.Enabled == value) return;
            _modelApiSettings.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LlmFallbackReason));
            RaiseCommands();
        }
    }

    public ModelProvider ModelApiProvider
    {
        get => _modelApiSettings.Provider;
        set
        {
            if (_modelApiSettings.Provider == value) return;
            var previousProvider = _modelApiSettings.Provider;
            var previousPreset = ModelProtocolAdapter.GetPreset(previousProvider);
            var endpointWasPreset =
                string.IsNullOrWhiteSpace(_modelApiSettings.Endpoint) ||
                string.Equals(
                    _modelApiSettings.Endpoint.Trim(),
                    previousPreset.DefaultEndpoint,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    _modelApiSettings.Endpoint.Trim(),
                    ResponseEndpointFor(previousProvider),
                    StringComparison.OrdinalIgnoreCase);
            var modelWasPreset =
                string.IsNullOrWhiteSpace(_modelApiSettings.Model) ||
                string.Equals(
                    _modelApiSettings.Model.Trim(),
                    previousPreset.DefaultModel,
                    StringComparison.OrdinalIgnoreCase);
            _modelApiSettings.Provider = value;
            OnPropertyChanged();

            // Custom always preserves everything the owner typed. Built-in
            // presets only replace a blank/known preset address, never a
            // hand-edited compatible endpoint.
            if (value != ModelProvider.Custom)
            {
                var preset = ModelProtocolAdapter.GetPreset(value);
                if (endpointWasPreset)
                {
                    _modelApiSettings.Endpoint = preset.DefaultEndpoint;
                    _modelApiSettings.ApiFormat = preset.DefaultApiFormat;
                    OnPropertyChanged(nameof(ModelApiEndpoint));
                    OnPropertyChanged(nameof(ModelApiRequestFormat));
                }
                else if (!preset.SupportsResponses &&
                         _modelApiSettings.ApiFormat == ModelApiFormat.OpenAiResponses)
                {
                    _modelApiSettings.ApiFormat = ModelApiFormat.OpenAiChat;
                    OnPropertyChanged(nameof(ModelApiRequestFormat));
                }
                if (modelWasPreset)
                {
                    _modelApiSettings.Model = preset.DefaultModel;
                    OnPropertyChanged(nameof(ModelApiModel));
                }
            }

            OnPropertyChanged(nameof(ModelApiProviderCapability));
            OnPropertyChanged(nameof(HasStoredModelApiKey));
        }
    }

    public ModelApiFormat ModelApiRequestFormat
    {
        get => _modelApiSettings.ApiFormat;
        set
        {
            var preset = ModelProtocolAdapter.GetPreset(_modelApiSettings.Provider);
            if (value == ModelApiFormat.OpenAiResponses && !preset.SupportsResponses)
            {
                ModelApiStatus = $"{preset.DisplayName} 预设不提供 Responses；已保留 Chat Completions。";
                OnPropertyChanged();
                return;
            }
            if (_modelApiSettings.ApiFormat == value) return;
            _modelApiSettings.ApiFormat = value;
            OnPropertyChanged();

            // Keep known official endpoints consistent while leaving custom or
            // hand-edited compatible addresses untouched.
            var responseEndpoint = ResponseEndpointFor(_modelApiSettings.Provider);
            if (value == ModelApiFormat.OpenAiResponses &&
                responseEndpoint.Length > 0 &&
                string.Equals(
                    _modelApiSettings.Endpoint.Trim(),
                    preset.DefaultEndpoint,
                    StringComparison.OrdinalIgnoreCase))
            {
                _modelApiSettings.Endpoint = responseEndpoint;
                OnPropertyChanged(nameof(ModelApiEndpoint));
            }
            else if (value == ModelApiFormat.OpenAiChat &&
                     responseEndpoint.Length > 0 &&
                     string.Equals(
                         _modelApiSettings.Endpoint.Trim(),
                         responseEndpoint,
                         StringComparison.OrdinalIgnoreCase))
            {
                _modelApiSettings.Endpoint = preset.DefaultEndpoint;
                OnPropertyChanged(nameof(ModelApiEndpoint));
            }
        }
    }

    private static string ResponseEndpointFor(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenAI => "https://api.openai.com/v1/responses",
        ModelProvider.Qwen => "https://dashscope.aliyuncs.com/compatible-mode/v1/responses",
        _ => string.Empty
    };

    public string ModelApiProviderCapability
    {
        get
        {
            var preset = ModelProtocolAdapter.GetPreset(_modelApiSettings.Provider);
            var vision = preset.SupportsVision ? "支持视觉输入" : "预设未声明视觉输入";
            var responses = preset.SupportsResponses ? "支持 Chat / Responses" : "仅支持 Chat";
            return $"{preset.DisplayName} · {responses} · {vision}";
        }
    }

    public string ModelApiEndpoint
    {
        get => _modelApiSettings.Endpoint;
        set
        {
            if (_modelApiSettings.Endpoint == value) return;
            _modelApiSettings.Endpoint = value;
            OnPropertyChanged();
        }
    }

    public string ModelApiModel
    {
        get => _modelApiSettings.Model;
        set
        {
            if (_modelApiSettings.Model == value) return;
            _modelApiSettings.Model = value;
            OnPropertyChanged();
        }
    }

    public bool ModelApiVisionEnabled
    {
        get => _modelApiSettings.VisionEnabled;
        set
        {
            if (_modelApiSettings.VisionEnabled == value) return;
            _modelApiSettings.VisionEnabled = value;
            OnPropertyChanged();
        }
    }

    public string ModelApiVisionModel
    {
        get => _modelApiSettings.VisionModel;
        set
        {
            if (_modelApiSettings.VisionModel == value) return;
            _modelApiSettings.VisionModel = value;
            OnPropertyChanged();
        }
    }

    public bool ModelApiSendAlbumImages
    {
        get => _modelApiSettings.SendAlbumImages;
        set
        {
            if (_modelApiSettings.SendAlbumImages == value) return;
            _modelApiSettings.SendAlbumImages = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<int> ModelConversationTurnOptions { get; } =
        Enumerable.Range(
                ConversationSessionStore.MinimumTurns,
                ConversationSessionStore.MaximumTurns -
                ConversationSessionStore.MinimumTurns + 1)
            .ToArray();

    public int ModelApiConversationTurns
    {
        get => _modelApiSettings.ConversationTurns;
        set
        {
            var normalized = Math.Clamp(
                value,
                ConversationSessionStore.MinimumTurns,
                ConversationSessionStore.MaximumTurns);
            if (_modelApiSettings.ConversationTurns == normalized) return;
            _modelApiSettings.ConversationTurns = normalized;
            OnPropertyChanged();
        }
    }

    public bool ModelApiOmitTemperature
    {
        get => _modelApiSettings.OmitTemperature;
        set
        {
            if (_modelApiSettings.OmitTemperature == value) return;
            _modelApiSettings.OmitTemperature = value;
            OnPropertyChanged();
        }
    }

    public string ModelApiKey
    {
        get => _modelApiKey;
        set => SetField(ref _modelApiKey, value);
    }

    public double ModelApiTemperature
    {
        get => _modelApiSettings.Temperature;
        set
        {
            if (Math.Abs(_modelApiSettings.Temperature - value) < 0.0001) return;
            _modelApiSettings.Temperature = value;
            OnPropertyChanged();
        }
    }

    public string ModelApiStatus
    {
        get => _modelApiStatus;
        private set
        {
            if (!SetField(ref _modelApiStatus, value)) return;
            OnPropertyChanged(nameof(LlmFallbackReason));
        }
    }

    public bool HasStoredModelApiKey => _modelApi.HasStoredApiKey(_modelApiSettings);

    public string NaturalRuleInput
    {
        get => _naturalRuleInput;
        set
        {
            if (!SetField(ref _naturalRuleInput, value)) return;
            RaiseCommands();
        }
    }

    public string NaturalRuleStatus
    {
        get => _naturalRuleStatus;
        private set => SetField(ref _naturalRuleStatus, value);
    }

    public string EditableMemoryText
    {
        get => _editableMemoryText;
        set
        {
            if (!SetField(ref _editableMemoryText, value)) return;
            RaiseCommands();
        }
    }

    public string EditableMemoryStatus
    {
        get => _editableMemoryStatus;
        private set => SetField(ref _editableMemoryStatus, value);
    }

    public string CodexIterationRequest
    {
        get => _codexIterationRequest;
        set
        {
            if (!SetField(ref _codexIterationRequest, value)) return;
            RaiseCommands();
        }
    }

    public string CodexProjectPath
    {
        get => _codexProjectPath;
        set => SetField(ref _codexProjectPath, value);
    }

    public string CodexIterationStatus
    {
        get => _codexIterationStatus;
        private set => SetField(ref _codexIterationStatus, value);
    }

    public string CorrectionNote
    {
        get => _correctionNote;
        set => SetField(ref _correctionNote, value);
    }

    public string CurrentBehaviorLabel
    {
        get => _currentBehaviorLabel;
        // Keep a public setter as a defensive WPF compatibility boundary. Some
        // older BAML/control templates request TwoWay for Run.Text even when the
        // current XAML explicitly says OneWay; accepting the no-op source update
        // prevents those cached layouts from aborting window creation.
        set => SetField(ref _currentBehaviorLabel, value);
    }

    public string MemoryStatus
    {
        get => _memoryStatus;
        private set => SetField(ref _memoryStatus, value);
    }

    public double Fullness => _memory.State.Fullness;
    public double Happiness => _memory.State.Happiness;
    public double Cleanliness => _memory.State.Cleanliness;
    public double Energy => _memory.State.Energy;
    public double Trust => _memory.Personality.Relationship.Trust * 100;
    public double LitterLevel => _memory.State.LitterLevel;
    public double PetDisplaySize => 236 * _memory.State.PetScale;
    public string PetScaleLabel => $"大小 {_memory.State.PetScale:P0}";
    public string EffectivePersonality => IsReady ? BuildPersonalityLabel(_memory.Personality.Temperament) : "读取中…";
    public string PersonalityMemoryMatchSummary => IsReady ? _memory.GetPersonalityMemoryMatchSummary() : "正在匹配性格与记忆…";
    public string RuntimeStateSummary => IsReady
        ? $"唤醒 {_memory.Personality.Runtime.Arousal:P0} · 压力 {_memory.Personality.Runtime.Stress:P0} · 社交 {_memory.Personality.Runtime.SocialDesire:P0} · 玩耍 {_memory.Personality.Runtime.PlayDesire:P0} · 好奇 {_memory.Personality.Runtime.Curiosity:P0} · 疲劳 {_memory.Personality.Runtime.Fatigue:P0} · 安全 {_memory.Personality.Runtime.Safety:P0}"
        : "读取中…";
    public string RelationshipStateSummary => IsReady
        ? $"信任 {_memory.Personality.Relationship.Trust:P0} · 熟悉 {_memory.Personality.Relationship.Familiarity:P0} · 触摸接受 {_memory.Personality.Relationship.TouchAcceptance:P0} · 主动接受 {_memory.Personality.Relationship.InitiativeAcceptance:P0}"
        : "读取中…";
    public string LocalMemoryPath => StoragePaths.MemoryDirectory;
    public string AssetPackStatus
    {
        get => _assetPackStatus;
        private set => SetField(ref _assetPackStatus, value);
    }
    public string LocalAssetPath => StoragePaths.AssetDirectory;
    public string NaturalPolicySummary => IsReady
        ? $"所有自主行为统一评分 · 主人未互动不产生惩罚或欠账 · rapid_tap 容忍范围 {_memory.GetTouchReactionProfile().AnnoyedAt}–{_memory.GetTouchReactionProfile().AngryAt} · 背带/自由遛猫 {_memory.BehaviorPolicy.WalkDurationMinutes} 分钟 · 三类投喂 {_memory.BehaviorPolicy.FeedingSeconds} 秒 · 铲砂 {_memory.BehaviorPolicy.CleaningSeconds} 秒 · 睡觉 {_memory.BehaviorPolicy.SleepMinutes} 分钟"
        : "正在读取规则…";

    public string PetChineseName
    {
        get => _editableProfile.ChineseName;
        set { _editableProfile.ChineseName = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public string PetEnglishName
    {
        get => _editableProfile.EnglishName;
        set { _editableProfile.EnglishName = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public string OwnerPersonalityPrompt
    {
        get => _editableProfile.SystemPrompt;
        set
        {
            if (string.Equals(_editableProfile.SystemPrompt, value, StringComparison.Ordinal))
                return;
            _editableProfile.SystemPrompt = value;
            OnPropertyChanged();
            RaiseCommands();
        }
    }

    public string PetBreed
    {
        get => _editableProfile.Breed;
        set { _editableProfile.Breed = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public string PetSex
    {
        get => _editableProfile.Sex;
        set { _editableProfile.Sex = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public DateTime? PetBirthday
    {
        get => _editableProfile.Birthday;
        set { _editableProfile.Birthday = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public string OwnerNickname
    {
        get => _editableProfile.OwnerNickname;
        set { _editableProfile.OwnerNickname = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public string RelationshipToOwner
    {
        get => _editableProfile.RelationshipToOwner;
        set { _editableProfile.RelationshipToOwner = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public DateTime? OwnerBirthday
    {
        get => _editableProfile.OwnerBirthday;
        set { _editableProfile.OwnerBirthday = value; OnPropertyChanged(); OnPropertyChanged(nameof(PetProfileSummary)); }
    }

    public string PetProfileSummary
    {
        get
        {
            var petBirthday = _editableProfile.Birthday?.ToString("yyyy年M月d日") ?? "未填写";
            var ownerBirthday = _editableProfile.OwnerBirthday?.ToString("yyyy年M月d日") ?? "未填写";
            var address = string.IsNullOrWhiteSpace(_editableProfile.OwnerNickname)
                ? "主人"
                : _editableProfile.OwnerNickname;
            return $"{_editableProfile.ChineseName} / {_editableProfile.EnglishName} · {_editableProfile.Breed} · " +
                   $"{_editableProfile.Sex} · 生日 {petBirthday} · 是主人的{_editableProfile.RelationshipToOwner} · " +
                   $"称呼主人为 {address} · 主人生日 {ownerBirthday}";
        }
    }

    public string PetProfileTitle
    {
        get
        {
            var english = string.IsNullOrWhiteSpace(_editableProfile.EnglishName)
                ? "PUPU"
                : _editableProfile.EnglishName.Trim().ToUpperInvariant();
            var chinese = string.IsNullOrWhiteSpace(_editableProfile.ChineseName)
                ? "朴朴"
                : _editableProfile.ChineseName.Trim();
            return $"{english} / {chinese}";
        }
    }

    public double Playfulness
    {
        get => _editableTraits.Playfulness;
        set { _editableTraits.Playfulness = value; OnPropertyChanged(); }
    }

    public double Clinginess
    {
        get => _editableTraits.Clinginess;
        set { _editableTraits.Clinginess = value; OnPropertyChanged(); }
    }

    public double Sensitivity
    {
        get => _editableTraits.Sensitivity;
        set { _editableTraits.Sensitivity = value; OnPropertyChanged(); }
    }

    public double Independence
    {
        get => _editableTraits.Independence;
        set { _editableTraits.Independence = value; OnPropertyChanged(); }
    }

    public double Mischief
    {
        get => _editableTraits.Mischief;
        set { _editableTraits.Mischief = value; OnPropertyChanged(); }
    }

    private AsyncRelayCommand ActionCommand(Func<Task> action) =>
        AsyncCommand(action, () => IsReady && !_busyAction && !IsCaged && !IsTraveling);

    private AsyncRelayCommand WalkActionCommand(Func<Task> action) =>
        AsyncCommand(action, () => IsReady && !_busyAction && !IsCaged && !IsTraveling);

    private AsyncRelayCommand AsyncCommand(Func<Task> action, Func<bool>? canExecute = null) =>
        new(action, canExecute, _presentationHost.ReportRecoverableException);

    private bool TryAcceptBehaviorRequest(
        string behaviorId,
        BehaviorArbitrationSource source,
        BehaviorPriority priority,
        TimeSpan minimumDuration,
        TimeSpan cooldown,
        bool interruptible,
        BehaviorStateBlockers forbiddenStates,
        bool forceInterrupt = false,
        bool observationOnly = false,
        bool showRejectedBubble = true,
        string cooldownKey = "")
    {
        var request = new BehaviorArbitrationRequest
        {
            BehaviorId = behaviorId,
            Source = source,
            Priority = priority,
            RequestedAt = _clock.Now,
            MinimumDuration = minimumDuration,
            Cooldown = cooldown,
            Interruptible = interruptible,
            ForceInterrupt = forceInterrupt,
            ObservationOnly = observationOnly,
            ForbiddenStates = forbiddenStates,
            CooldownKey = cooldownKey
        };
        var result = _behaviorArbitrator.Evaluate(request, BuildArbitrationContext());
        RecordArbitrationResult(result);
        if (!result.Accepted)
        {
            if (showRejectedBubble)
                _ = ShowBubbleAsync(
                    RejectionBubble(result),
                    3600,
                    PetSpeechIntent.Busy);
            return false;
        }

        return true;
    }

    private BehaviorArbitrationContext BuildArbitrationContext()
    {
        var lease = _behaviorArbitrator.CurrentLease;
        return new()
        {
            CurrentBehaviorId = lease?.BehaviorId ?? _currentBehaviorKey,
            CurrentPriority = lease?.Priority ?? BehaviorPriority.DecorativeIdle,
            CurrentStartedAt = lease?.StartedAt ?? _currentBehaviorStartedAt,
            CurrentMinimumDuration = lease?.MinimumDuration ?? TimeSpan.Zero,
            CurrentInterruptible = lease?.Interruptible ?? !_busyAction,
            ActiveStates = CurrentBehaviorStates()
        };
    }

    private async Task<BehaviorProposalRecord?> SubmitBehaviorProposalAsync(
        BehaviorProposal proposal,
        bool showRejectedBubble = false)
    {
        var queued = _behaviorProposalQueue.Enqueue(proposal);
        CurrentIntent = $"{proposal.Source}: {proposal.Reason}";
        RefreshBehaviorProposalDebug();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var outcome = await _behaviorProposalExecutor.ProcessNextAsync(
                _clock.Now,
                BuildArbitrationContext(),
                result =>
                {
                    RecordArbitrationResult(result);
                    if (!result.Accepted && showRejectedBubble)
                    {
                        _ = ShowBubbleAsync(
                            RejectionBubble(result),
                            3600,
                            PetSpeechIntent.Busy);
                    }
                    return Task.CompletedTask;
                },
                ExecuteBehaviorProposalAsync,
                _lifetimeCancellation.Token);
            RefreshBehaviorProposalDebug();
            if (outcome.Record is null) return queued;
            if (outcome.Record.Proposal.Id == proposal.Id)
            {
                LastProposalResult = outcome.Record.Display;
                if (outcome.Record.State == BehaviorProposalState.Failed)
                    ResetArbitrationToIdle();
                return outcome.Record;
            }
        }
        return queued;
    }

    private async Task<bool> ProcessPendingBehaviorProposalAsync()
    {
        if (_behaviorProposalQueue.Snapshot().Count == 0) return false;
        var outcome = await _behaviorProposalExecutor.ProcessNextAsync(
            _clock.Now,
            BuildArbitrationContext(),
            result =>
            {
                RecordArbitrationResult(result);
                return Task.CompletedTask;
            },
            ExecuteBehaviorProposalAsync,
            _lifetimeCancellation.Token);
        if (outcome.Record is not null)
            LastProposalResult = outcome.Record.Display;
        RefreshBehaviorProposalDebug();
        return outcome.Executed;
    }

    private void RefreshBehaviorProposalDebug()
    {
        BehaviorProposalItems.Clear();
        foreach (var item in _behaviorProposalQueue.Snapshot().Take(12))
            BehaviorProposalItems.Add(item.Display);
        foreach (var item in _behaviorProposalQueue.History().Take(12))
            BehaviorProposalItems.Add(item.Display);
        OnPropertyChanged(nameof(CurrentPromptTokenEstimate));
        OnPropertyChanged(nameof(LlmFallbackReason));
    }

    private async Task<bool> ExecuteBehaviorProposalAsync(
        BehaviorProposal proposal,
        CancellationToken cancellationToken)
    {
        switch (proposal.BehaviorId)
        {
            case "command.quiet":
                _memory.State.QuietModeUntil = _clock.Now.AddMinutes(30);
                await _memory.SaveStateAsync();
                ScheduleNextAutonomousAction();
                SetBehavior(
                    proposal.BehaviorId,
                    "安静模式：延长 idle/rest 驻留并暂时减少自主走动",
                    "proposal_command",
                    "desktop",
                    "local:quiet");
                SetIdleAnimation();
                return true;
            case "command.self_play":
                _memory.State.SelfPlayAllowedUntil = _clock.Now.AddMinutes(20);
                await _memory.SaveStateAsync();
                _nextAutonomousActionAt = _clock.Now.AddSeconds(_random.Next(18, 46));
                SetBehavior(
                    proposal.BehaviorId,
                    "主人允许低打扰自主玩耍；不会强制立刻乱跑",
                    "proposal_command",
                    "desktop",
                    "local:self-play");
                ResetArbitrationToIdle();
                return true;
            case "anchor.food.approach":
            case "anchor.toy.approach":
                return await ExecuteAnchorProposalAsync(proposal, cancellationToken);
            case "celebrate.idle":
            case "play.wand":
            case "feed.snack":
            case "rest.window":
            case "rest.near_owner":
                ExecuteMemoryRecallProposal(proposal);
                return true;
            default:
                return false;
        }
    }

    private void ExecuteMemoryRecallProposal(BehaviorProposal proposal)
    {
        var sequence = proposal.BehaviorId switch
        {
            "celebrate.idle" => RollSequence,
            "play.wand" => WandLoopSequence,
            "feed.snack" => FreezeDriedEatingLoopSequence,
            "rest.window" => SideLieIdleSequence,
            _ => SideLieIdleSequence
        };
        _touchReactionCancellation?.Cancel();
        _touchReactionCancellation?.Dispose();
        _touchReactionCancellation = new CancellationTokenSource();
        var token = _touchReactionCancellation.Token;
        SetBehavior(
            proposal.BehaviorId,
            $"统一提案执行：{proposal.Reason}",
            "memory_recall",
            "album_experience",
            $"proposal:{sequence.Name}");
        PlaySequence(sequence);
        _ = ShowBubbleAsync("那次的感觉又轻轻冒出来了。", 3000, PetSpeechIntent.Remembered);
        _ = RestoreAfterProposalAsync(token);
    }

    private async Task RestoreAfterProposalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                SetIdleAnimation();
        }
        catch (OperationCanceledException) { }
    }

    private BehaviorStateBlockers CurrentBehaviorStates()
    {
        var states = BehaviorStateBlockers.None;
        if (_memory.State.IsCaged) states |= BehaviorStateBlockers.Caged;
        if (_memory.State.Travel.IsTraveling) states |= BehaviorStateBlockers.Traveling;
        if (_isPetrified) states |= BehaviorStateBlockers.Petrified | BehaviorStateBlockers.Magic;
        if (_activeMoveMode is not null ||
            _walkingBehaviorActive() ||
            _currentBehaviorKey.EndsWith(".approach", StringComparison.Ordinal))
            states |= BehaviorStateBlockers.Movement;
        if (_isTouchEscaping || _currentInteractionType == "touch")
            states |= BehaviorStateBlockers.TouchReaction;
        if (_currentBehaviorKey.StartsWith("rest.sleep", StringComparison.Ordinal) ||
            _currentBehaviorKey == "rest.bed")
            states |= BehaviorStateBlockers.Sleeping;
        if (_currentBehaviorKey.StartsWith("routine.toilet", StringComparison.Ordinal))
            states |= BehaviorStateBlockers.Toilet;
        if (_currentBehaviorKey.StartsWith("magic.", StringComparison.Ordinal))
            states |= BehaviorStateBlockers.Magic;
        if (_currentBehaviorKey.StartsWith("feed", StringComparison.Ordinal) ||
            _currentBehaviorKey.StartsWith("care.feed", StringComparison.Ordinal) ||
            _currentInteractionType == "feed")
            states |= BehaviorStateBlockers.Feeding;
        if (_currentBehaviorKey.StartsWith("play.", StringComparison.Ordinal) ||
            _currentInteractionType == "play")
            states |= BehaviorStateBlockers.Playing;
        return states;
    }

    private bool _walkingBehaviorActive() =>
        _currentBehaviorKey.StartsWith("walk.", StringComparison.Ordinal) ||
        _currentBehaviorKey.StartsWith("explore.", StringComparison.Ordinal) ||
        _currentBehaviorKey.StartsWith("independent.patrol", StringComparison.Ordinal);

    private void RecordArbitrationResult(BehaviorArbitrationResult result)
    {
        LastArbitrationResult = result.Display;
        ArbitrationItems.Insert(0, result.Display);
        while (ArbitrationItems.Count > 20)
            ArbitrationItems.RemoveAt(ArbitrationItems.Count - 1);
        _ = _decisionLogger.AppendArbitrationAsync(result);
    }

    private static string RejectionBubble(BehaviorArbitrationResult result) =>
        result.ReasonCode switch
        {
            "state_forbidden" => "现在不想换。挑个轻松点的，也许会赏脸。",
            "current_not_interruptible" => "看见了。等本猫把爪子收好，再考虑理你。",
            "minimum_duration" => "才刚躺稳就催？先让尾巴摆完这一拍。",
            "lower_priority" => "嗯，先放那儿。本猫有空再审。",
            "request_cooldown" => "刚理过你一次。别得寸进尺，等一小会儿。",
            _ => "这次没兴趣。过会儿拿点诚意再来。"
        };

    private void ResetArbitrationToIdle()
        => _behaviorArbitrator.ResetCurrent(
            _clock.Now,
            "idle.side_lie",
            TimeSpan.FromSeconds(
                IsReady ? _memory.BehaviorPolicy.MinimumIdleActionSeconds : 90));

    private async Task<bool> TryParticipateAsync(
        OwnerInteractionKind kind,
        string requestedAction)
    {
        var context = new OwnerInteractionContext(
            _memory.State.Fullness,
            _memory.State.Energy,
            _memory.State.Cleanliness,
            _memory.State.LitterLevel,
            $"action={requestedAction};time={TimeBucket(_clock.Now)}");
        var decision = _participationEvaluator.Evaluate(
            _memory.Personality,
            kind,
            context,
            _random.NextDouble());
        if (decision.Accepted) return true;

        var owner = _memory.Profile.OwnerAddress;
        var line = decision.ReasonCode switch
        {
            "full" => "肚子还是圆圆的。这顿先放一放。",
            "low_energy" => "今天的爪子有点沉。先不出去啦。",
            "sleepy" => "眼皮已经快合上了。这次先让我睡一会儿。",
            "need_space" => $"{owner}，现在先别催我。我想安静趴一会儿。",
            "not_playful" => "看见啦，不过现在不想追。晚一点再来。",
            "not_touching_now" => "今天的毛先自己管。梳子晚点再拿来。",
            "no_need" => "这里现在很干净。让我先检查完再说。",
            "chose_other" => "这次不参加。猫已经有别的安排了。",
            _ => "现在不想做这个。等朴朴自己点头。"
        };
        SetBehavior(
            $"interaction.refused.{kind.ToString().ToLowerInvariant()}",
            $"根据当下状态拒绝“{requestedAction}”",
            "owner_request",
            context.ContextKey,
            "local:refusal");
        _ = ShowBubbleAsync(line, 4300, PetSpeechIntent.General);
        await _memory.RecordAsync(
            "interaction_refused",
            $"主人提出“{requestedAction}”，pupu根据当下状态选择不参与；没有关系惩罚。",
            $"interaction.refused.{kind.ToString().ToLowerInvariant()}",
            0.30,
            0,
            true,
            "owner_request",
            context.ContextKey,
            "local:refusal");
        RefreshAll();
        return false;
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _memory.InitializeAsync();
            if (PrepareDailyToiletPlan(_clock.Now, skipPastPending: true))
                await _memory.SaveStateAsync();
            _modelApiSettings = await _modelApi.LoadAsync();
            OnPropertyChanged(nameof(ModelApiEnabled));
            OnPropertyChanged(nameof(ModelApiProvider));
            OnPropertyChanged(nameof(ModelApiRequestFormat));
            OnPropertyChanged(nameof(ModelApiProviderCapability));
            OnPropertyChanged(nameof(ModelApiEndpoint));
            OnPropertyChanged(nameof(ModelApiModel));
            OnPropertyChanged(nameof(ModelApiVisionEnabled));
            OnPropertyChanged(nameof(ModelApiVisionModel));
            OnPropertyChanged(nameof(ModelApiSendAlbumImages));
            OnPropertyChanged(nameof(ModelApiConversationTurns));
            OnPropertyChanged(nameof(ModelApiOmitTemperature));
            OnPropertyChanged(nameof(ModelApiTemperature));
            OnPropertyChanged(nameof(HasStoredModelApiKey));
            await RestoreConversationHistoryAsync();
            ModelApiStatus = _modelApiSettings.Enabled
                ? HasStoredModelApiKey
                    ? "模型对话已启用，密钥保存在 Windows 凭据管理器。"
                    : "模型对话已启用，但尚未保存 API 密钥。"
                : "模型对话未启用；朴朴会继续使用本地性格台词。";
            _editableTraits = _memory.Profile.Baseline.Clone();
            _editableProfile = _memory.Profile.Clone();
            if (string.IsNullOrWhiteSpace(_editableProfile.SystemPrompt))
                _editableProfile.SystemPrompt = PetProfile.DefaultSystemPrompt;
            _persona = _memory.Profile.Persona;
            _persona.Normalize();
            _agentKernel.ReplaceAgent(new RulePetAgent(_persona));
            OnPropertyChanged(nameof(CurrentPersonaSummary));
            OnPropertyChanged(nameof(CurrentPromptPreview));
            OnPropertyChanged(nameof(CurrentPromptTokenEstimate));
            RefreshEditableProfile();
            EditableMemoryText = await _memory.GetEditableNotebookAsync();
            EditableMemoryStatus = $"主人可编辑主文件：{StoragePaths.EditableMemoryFile}";
            CodexProjectPath = await _codexIteration.LoadProjectPathAsync();
            RefreshNaturalRules();
            RefreshHiddenActionRules();
            IsReady = true;
            ResetArbitrationToIdle();
            _lastActiveStateTickAt = _clock.Now;
            if (_memory.State.Travel.IsTraveling &&
                _memory.State.Travel.ReturnsAt is { } returnsAt &&
                returnsAt <= _clock.Now)
            {
                await ReturnFromTravelAsync(recalled: false);
            }
            else if (_memory.State.Travel.IsTraveling)
            {
                _behaviorArbitrator.RestoreCurrent(
                    "travel.away",
                    BehaviorPriority.OwnerForced,
                    _clock.Now,
                    TimeSpan.Zero,
                    interruptible: false);
                SetBehavior(
                    "travel.away",
                    $"外出旅行：{_memory.State.Travel.Destination}",
                    "travel",
                    "away",
                    "local:travel");
                RaiseRestrictedStateProperties();
            }
            else if (_memory.State.IsCaged)
            {
                _behaviorArbitrator.RestoreCurrent(
                    "owner.cage",
                    BehaviorPriority.OwnerForced,
                    _clock.Now,
                    TimeSpan.Zero,
                    interruptible: false);
                SetBehavior(
                    "owner.cage",
                    "关笼子／原地锁定，等待主人释放",
                    "owner_forced",
                    "desktop",
                    "routines:prone-idle");
                PlaySequence(ProneIdleSequence);
                RaiseRestrictedStateProperties();
            }
            RefreshAll();
            _animationTimer.Start();
            _needsTimer.Start();
            _autonomyTimer.Start();
            ScheduleNextAutonomousAction();
            if (!IsTraveling && !IsCaged)
            {
                _ = ShowBubbleAsync(
                    ComposePetSpeech(PetSpeechIntent.Startup),
                    4800,
                    PetSpeechIntent.Startup);
                _ = TryRunCalendarSpecialAsync();
            }
        }
        catch (Exception ex)
        {
            MemoryStatus = $"初始化失败：{ex.Message}";
            BubbleText = ComposePetSpeech(PetSpeechIntent.RecoverableProblem);
            IsBubbleVisible = true;
        }
    }

    private async Task FeedAsync(FoodKind food)
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.Feeding, "吃饭")) return;
        var (key, label, sequence, bubble, memory, fullness, happiness) = food switch
        {
            FoodKind.FreezeDried => (
                "feed_freeze_dried",
                "看见冻干后立刻饿猫扑食，扑近后持续吃",
                FreezeDriedEatingSequence,
                "冻干！让开让开，我现在就要吃！",
                "主人给pupu冻干，pupu立刻扑过去，急切地追着每一块吃。",
                19d, 8d),
            FoodKind.Canned => (
                "feed_canned",
                "闻到罐头后眼睛发亮，扑到碗边飞快舔食",
                CannedEatingSequence,
                "是罐头！这碗现在归我，谁都别碰。",
                "主人打开罐头，pupu闻到以后饿猫扑食，飞快地舔着碗。",
                25d, 9d),
            _ => (
                "feed_kibble",
                "面对猫粮磨磨蹭蹭，吃一口就停下来东张西望",
                KibbleEatingSequence,
                "猫粮啊……我会吃，但我要先慢慢闻一会儿。",
                "主人给pupu猫粮，pupu磨磨蹭蹭，吃一口就停下来看看别处。",
                21d, 4d)
        };
        if (!TryAcceptBehaviorRequest(
                $"care.{key}",
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(4),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;
        var token = BeginAction(key, label, sequence);
        var context = $"food={food.ToString().ToLowerInvariant()};time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            $"care.{key}",
            "feed",
            context,
            $"routines:{sequence.Name}");
        _activeInteraction = session;
        var total = TimeSpan.FromSeconds(_memory.BehaviorPolicy.FeedingSeconds);
        var completed = false;
        try
        {
            _ = ShowBubbleAsync(_memory.State.Fullness > 100 ? "其实已经饱了……但这个可以再吃一点。" : bubble, 5200);
            if (food is FoodKind.FreezeDried or FoodKind.Canned)
            {
                if (!await WaitPhaseAsync(TimeSpan.FromSeconds(1.55), token)) return;
                PlaySequence(
                    food == FoodKind.FreezeDried
                        ? FreezeDriedEatingLoopSequence
                        : CannedEatingLoopSequence);
            }
            completed = await RunProgressiveInteractionAsync(
                session,
                total,
                TimeSpan.FromSeconds(1.2),
                4,
                token,
                fraction =>
                {
                    var fullnessStep = fullness * fraction;
                    var happinessStep = happiness * fraction;
                    _memory.State.Fullness += fullnessStep;
                    _memory.State.Happiness += happinessStep;
                    _memory.Personality.Runtime.Safety += 0.025 * fraction;
                    _memory.Personality.Runtime.Stress -= 0.02 * fraction;
                    _memory.ApplyRelationshipDelta(
                        trust: 0.006 * fraction,
                        familiarity: 0.004 * fraction);
                    return new[]
                    {
                        new AppliedEffect("fullness", fullnessStep, "points"),
                        new AppliedEffect("happiness", happinessStep, "points"),
                        new AppliedEffect("runtime.safety", 0.025 * fraction),
                        new AppliedEffect("relationship.trust", 0.006 * fraction)
                    };
                });
            if (!completed) return;
            _memory.State.FeedCount++;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                "care",
                memory,
                $"care.{key}",
                0.62,
                0.82,
                true,
                "feed",
                context,
                $"routines:{sequence.Name}");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(completed ? ProneIdleSequence : SideLieIdleSequence, expectedToken: token);
        }
    }

    private async Task WalkAsync(DesktopMoveMode mode)
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.Walk, "遛猫")) return;
        var harnessed = mode == DesktopMoveMode.HarnessedWalk;
        var behaviorId = harnessed ? "walk.harnessed" : "walk.free";
        if (!TryAcceptBehaviorRequest(
                behaviorId,
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(8),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;
        var token = BeginAction(
            behaviorId,
            harnessed
                ? "穿好孔雀蓝色背带，在桌面随机选择方向巡逻"
                : "解除背带后自由探索；“停下”仍可安全结束",
            harnessed ? HarnessWalkLeftSequence : FreeWalkLeftSequence);
        var context = $"mode={(harnessed ? "harnessed" : "free")};time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "walk",
            context,
            harnessed ? "walkModes:harness-left" : "walkModes:free-left");
        _activeInteraction = session;
        var duration = TimeSpan.FromMinutes(_memory.BehaviorPolicy.WalkDurationMinutes);
        var completed = false;
        _activeMoveMode = mode;
        OnPropertyChanged(nameof(IsHarnessedWalkActive));
        OnPropertyChanged(nameof(IsFreeRoamActive));
        RaiseCommands();
        try
        {
            _memory.State.WalkEndsAt = _clock.Now.Add(duration);
            _ = ShowBubbleAsync(
                harnessed
                    ? "孔雀蓝背带穿好啦。今天往哪走，由朴朴决定。"
                    : $"背带解开啦。朴朴要自己探险 {FormatDuration(duration)}。",
                6200);

            var move = new DesktopMoveRequestEventArgs(mode, duration, token);
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            var progressTask = RunProgressiveInteractionAsync(
                session,
                duration,
                TimeSpan.FromSeconds(0.8),
                8,
                token,
                fraction =>
                {
                    var energy = -16 * fraction;
                    var happiness = 15 * fraction;
                    _memory.State.Energy += energy;
                    _memory.State.Happiness += happiness;
                    _memory.Personality.Runtime.PlayDesire -= 0.16 * fraction;
                    _memory.Personality.Runtime.Fatigue += 0.22 * fraction;
                    _memory.Personality.Runtime.Stress -= 0.06 * fraction;
                    _memory.ApplyRelationshipDelta(
                        trust: 0.008 * fraction,
                        familiarity: 0.006 * fraction);
                    return new[]
                    {
                        new AppliedEffect("energy", energy, "points"),
                        new AppliedEffect("happiness", happiness, "points"),
                        new AppliedEffect("runtime.fatigue", 0.22 * fraction),
                        new AppliedEffect("relationship.trust", 0.008 * fraction)
                    };
                });
            bool movementCompleted;
            try { movementCompleted = await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
            if (!movementCompleted) _actionScheduler.Stop("movement_unavailable");
            completed = movementCompleted && await progressTask;
            if (!completed) return;

            _memory.State.WalkCount++;
            _memory.State.LastWalkAt = _clock.Now;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                "care",
                harnessed
                    ? "主人给pupu穿上孔雀蓝背带遛猫；pupu每次实时随机选择路线、方向、速度和停顿。"
                    : "主人解除背带让pupu自由探索；路线实时随机生成，停下可安全结束。",
                behaviorId,
                0.82,
                0.9,
                true,
                "walk",
                context,
                harnessed ? "walkModes:harness" : "walkModes:free");
            _ = ShowBubbleAsync("玩够了。我决定侧躺一会儿。", 4400);
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            _memory.State.WalkEndsAt = null;
            _activeMoveMode = null;
            OnPropertyChanged(nameof(IsHarnessedWalkActive));
            OnPropertyChanged(nameof(IsFreeRoamActive));
            EndAction(SideLieIdleSequence, expectedToken: token);
        }
    }

    private async Task CleanAsync()
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.CleanLitter, "检查和清理猫砂")) return;
        const string behaviorId = "care.clean_litter";
        var token = BeginAction(behaviorId, "检查猫砂、监督清理并认真刨砂", CleanIntroSequence);
        var context = $"location=litter;time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "clean_litter",
            context,
            "life:clean-intro");
        _activeInteraction = session;
        var total = TimeSpan.FromSeconds(_memory.BehaviorPolicy.CleaningSeconds);
        var completed = false;
        var cleanlinessRemaining = Math.Max(0, 100 - _memory.State.Cleanliness);
        var litterRemaining = Math.Max(0, _memory.State.LitterLevel);
        try
        {
            _ = ShowBubbleAsync($"我要检查一会儿，大约 {FormatDuration(total)}。埋平一点。", 5200);
            completed = await RunProgressiveInteractionAsync(
                session,
                total,
                TimeSpan.FromSeconds(2.3),
                4,
                token,
                fraction =>
                {
                    PlaySequence(CleaningLoopSequence, restart: false);
                    var cleanliness = cleanlinessRemaining * fraction;
                    var litter = -litterRemaining * fraction;
                    var happiness = 3 * fraction;
                    _memory.State.Cleanliness += cleanliness;
                    _memory.State.LitterLevel += litter;
                    _memory.State.Happiness += happiness;
                    _memory.Personality.Runtime.Safety += 0.03 * fraction;
                    return new[]
                    {
                        new AppliedEffect("cleanliness", cleanliness, "points"),
                        new AppliedEffect("litter_level", litter, "points"),
                        new AppliedEffect("happiness", happiness, "points")
                    };
                });
            if (!completed) return;
            _memory.State.CleanCount++;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                "care",
                "主人清理猫砂，pupu在旁边持续检查并满意地监工。",
                behaviorId,
                0.46,
                0.65,
                true,
                "clean_litter",
                context,
                "life:clean-loop");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    public void RegisterPetClick()
    {
        RegisterPointerDown(128, 128);
        RegisterPointerUp(128, 128);
    }

    public void RegisterPointerDown(double x, double y)
    {
        if (!IsReady || _disposed) return;
        var region = _interactionRegions.HitTest(
            _currentSequence.Name,
            _framePosition,
            _currentDirection.ToString(),
            PetDisplaySize,
            PetDisplaySize,
            x,
            y);
        _gestureInterpreter.PointerDown(x, y, region);
    }

    public void RegisterPointerMove(double x, double y) =>
        _gestureInterpreter.PointerMove(x, y);

    public void RegisterMousePresence(double x, double y)
    {
        if (!IsReady || _disposed) return;
        _perception.Accept(new PerceptionEvent
        {
            Timestamp = _clock.Now,
            Source = "pointer",
            Kind = "mouse_nearby",
            Confidence = 1,
            Ttl = TimeSpan.FromSeconds(5),
            DeduplicationKey = "pointer:mouse_nearby",
            Priority = PerceptionPriority.Background,
            Intensity = 1,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x"] = x.ToString("0"),
                ["y"] = y.ToString("0")
            }
        }, _clock.Now);
    }

    public void UpdateCursorGaze(int frame, bool nearby)
    {
        if (!nearby || MouseInteractionMode is not MouseInteractionMode.Attention)
        {
            EndCursorGaze();
            CursorAttentionStatus = MouseInteractionMode is MouseInteractionMode.Attention
                ? "普通注意力：鼠标不在附近"
                : MouseInteractionModeLabel;
            return;
        }

        frame = Math.Clamp(frame, 0, 7);
        RegisterMousePresence(frame * 32, 128);
        if (_cursorGazeFrame != frame)
        {
            if (_pendingCursorGazeFrame != frame)
            {
                _pendingCursorGazeFrame = frame;
                _pendingCursorGazeSamples = 1;
                return;
            }
            _pendingCursorGazeSamples++;
            if (_pendingCursorGazeSamples < 3 ||
                (_isCursorGazeActive &&
                 _clock.Now - _cursorGazeFrameChangedAt < TimeSpan.FromMilliseconds(420)))
                return;
        }
        if (_clock.Now >= _nextCursorGazeTailPhaseAt)
        {
            _cursorGazeTailPhase = (_cursorGazeTailPhase + 1) % 2;
            _nextCursorGazeTailPhaseAt = _clock.Now.AddSeconds(1.6);
        }
        if (_clock.Now < _nextCursorAttentionArbitrationAt &&
            _isCursorGazeActive &&
            _cursorGazeFrame == frame)
            return;
        _nextCursorAttentionArbitrationAt = _clock.Now.AddSeconds(1);
        if (!TryAcceptBehaviorRequest(
                "attention.mouse",
                BehaviorArbitrationSource.MouseAttention,
                BehaviorPriority.MouseAttention,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(850),
                interruptible: true,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Sleeping |
                BehaviorStateBlockers.Toilet |
                BehaviorStateBlockers.Magic |
                BehaviorStateBlockers.Movement |
                BehaviorStateBlockers.TouchReaction |
                BehaviorStateBlockers.Feeding |
                BehaviorStateBlockers.Playing |
                BehaviorStateBlockers.Petrified,
                observationOnly: true,
                showRejectedBubble: false,
                cooldownKey: "attention.mouse"))
        {
            CursorAttentionStatus = MouseAttentionBlockedStatus();
            EndCursorGaze();
            return;
        }

        var gazeFrame = ResolveGazeFullBodyFrame(frame);
        if (gazeFrame is null)
        {
            CursorAttentionStatus = PoseCompatibleAttentionStatus(frame);
            EndCursorGaze();
            return;
        }

        // Full-body variants preserve the current posture and only change face,
        // ears and a small amount of head direction. They replace the legacy
        // floating-head overlay without changing the behavior or its dwell.
        _isCursorGazeActive = true;
        _cursorGazeFrame = frame;
        _pendingCursorGazeFrame = frame;
        _pendingCursorGazeSamples = 0;
        _cursorGazeFrameChangedAt = _clock.Now;
        PetFrame = gazeFrame;
        CursorAttentionStatus = PoseCompatibleAttentionStatus(frame);
    }

    private object? ResolveGazeFullBodyFrame(int frame)
    {
        var postureOffset = _currentBehaviorKey switch
        {
            "idle.side_lie" => 0,
            "idle.prone_observe" => 4,
            "idle.sploot" => 8,
            "rest.near_owner" => 12,
            _ => -1
        };
        if (postureOffset < 0) return null;
        var directionOffset = frame switch
        {
            1 or 2 or 7 => 0,
            3 => 1,
            4 or 5 or 6 => 2,
            _ => 3
        };
        return _assetPack.CreateActionFrame(
            "gaze-fullbody-16",
            postureOffset + directionOffset);
    }

    private object? CurrentGazeFullBodyFrame() =>
        !_isCursorGazeActive || _cursorGazeFrame < 0
            ? null
            : ResolveGazeFullBodyFrame(_cursorGazeFrame);

    private string PoseCompatibleAttentionStatus(int frame)
    {
        var eightDirections = new[]
        {
            "近处", "左侧", "左上", "上方", "右上", "右侧", "右下", "左下"
        };
        return _currentBehaviorKey switch
        {
            "idle.prone_observe" =>
                $"低趴观察：记录八方向视线 · {eightDirections[frame]}",
            "idle.side_lie" =>
                $"侧躺：轻微看向{(frame is 1 or 2 or 7 ? "左侧" : frame == 3 ? "上方" : frame is 4 or 5 or 6 ? "右侧" : "近处")}",
            "idle.sploot" =>
                $"板鸭趴：{(frame is 1 or 2 or 7 ? "向左看" : frame is 4 or 5 ? "向右看" : "低头看近处")}",
            "rest.near_owner" =>
                $"主人附近休息：慢眨眼并记住{eightDirections[frame]}方向",
            "self.groom" =>
                "舔毛：不切动作，只记录这一拍注意力",
            _ => $"当前姿态没有局部方向帧，仅记录注意力 · {eightDirections[frame]}"
        };
    }

    private string MouseAttentionBlockedStatus()
    {
        if (_currentBehaviorKey.StartsWith("rest.sleep", StringComparison.Ordinal) ||
            _currentBehaviorKey == "rest.bed")
            return "睡眠：不跟随鼠标；本版不强制添加耳朵帧";
        if (_currentBehaviorKey.StartsWith("self.groom", StringComparison.Ordinal))
            return "舔毛：不抢占，最多记录一拍注意力";
        if (_currentBehaviorKey.StartsWith("routine.toilet", StringComparison.Ordinal))
            return "如厕：禁止鼠标视线";
        if (_currentBehaviorKey.StartsWith("magic.", StringComparison.Ordinal))
            return "魔法：禁止鼠标视线";
        if (CurrentBehaviorStates().HasFlag(BehaviorStateBlockers.Movement))
            return "移动：禁止鼠标视线";
        if (_currentInteractionType == "touch")
            return "触摸反应：禁止鼠标视线";
        if (CurrentBehaviorStates().HasFlag(BehaviorStateBlockers.Feeding))
            return "进食：禁止鼠标视线";
        if (CurrentBehaviorStates().HasFlag(BehaviorStateBlockers.Playing))
            return "玩耍：普通鼠标注意力禁止；主动玩具锚点流程除外";
        return $"普通注意力未表现：{LastArbitrationResult}";
    }

    private void EndCursorGaze()
    {
        _pendingCursorGazeFrame = -1;
        _pendingCursorGazeSamples = 0;
        if (!_isCursorGazeActive) return;
        _isCursorGazeActive = false;
        _cursorGazeFrame = -1;
        _cursorGazeFrameChangedAt = DateTimeOffset.MinValue;
        _cursorGazeTailPhase = 0;
        _nextCursorGazeTailPhaseAt = DateTimeOffset.MinValue;
        RenderNextFrame();
    }

    private void ActivateAnchorMode(MouseInteractionMode mode)
    {
        if (mode is MouseInteractionMode.Attention) return;
        var isFood = mode == MouseInteractionMode.FoodAnchor;
        // Selecting a placement cursor is UI state, not a pet behavior. The old
        // implementation admitted an "anchor.*.prepare" lease here, then the
        // real approach proposal was rejected by that lease. Admission now
        // happens exactly once, after the owner chooses the desktop target.
        EndCursorGaze();
        MouseInteractionMode = mode;
        _ = ShowBubbleAsync(
            isFood
                ? "把冻干丢到哪里？点一下桌面位置。"
                : "激光点放在哪里？点一下桌面，我去抓。",
            5200,
            isFood ? PetSpeechIntent.General : PetSpeechIntent.Play);
    }

    private void CancelAnchorMode()
    {
        if (MouseInteractionMode is MouseInteractionMode.Attention) return;
        MouseInteractionMode = MouseInteractionMode.Attention;
        if (_currentBehaviorKey.StartsWith("anchor.", StringComparison.Ordinal))
            SetIdleAnimation();
    }

    public async Task PlaceActiveAnchorAsync(DesktopPoint target)
    {
        var mode = MouseInteractionMode;
        if (mode is MouseInteractionMode.Attention || IsCaged || IsTraveling)
            return;
        MouseInteractionMode = MouseInteractionMode.Attention;

        var isFood = mode == MouseInteractionMode.FoodAnchor;
        var anchor = new InteractionAnchor(
            isFood ? InteractionAnchorKind.Food : InteractionAnchorKind.Toy,
            target.X,
            target.Y,
            _clock.Now);
        var participates = await TryParticipateAsync(
            isFood ? OwnerInteractionKind.Feeding : OwnerInteractionKind.LaserPlay,
            isFood ? "追到冻干落点" : "追到激光点");
        if (!participates)
        {
            ResetArbitrationToIdle();
            return;
        }

        var behaviorId = isFood ? "anchor.food.approach" : "anchor.toy.approach";
        await SubmitBehaviorProposalAsync(
            new BehaviorProposal
            {
                BehaviorId = behaviorId,
                Source = BehaviorArbitrationSource.OwnerAnchor,
                Priority = BehaviorPriority.OwnerAnchor,
                CreatedAt = _clock.Now,
                ExpiresAt = _clock.Now.AddSeconds(18),
                Cancellable = true,
                AllowDelay = true,
                Reason = isFood ? "主人投掷冻干" : "主人放置激光点",
                MinimumDuration = TimeSpan.FromSeconds(5),
                Cooldown = TimeSpan.FromSeconds(4),
                Interruptible = false,
                ForbiddenStates =
                    BehaviorStateBlockers.Caged |
                    BehaviorStateBlockers.Traveling |
                    BehaviorStateBlockers.Sleeping |
                    BehaviorStateBlockers.Toilet |
                    BehaviorStateBlockers.Magic |
                    BehaviorStateBlockers.Movement |
                    BehaviorStateBlockers.TouchReaction |
                    BehaviorStateBlockers.Feeding |
                    BehaviorStateBlockers.Playing |
                    BehaviorStateBlockers.Petrified,
                CooldownKey = behaviorId,
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x"] = anchor.X.ToString("R", CultureInfo.InvariantCulture),
                    ["y"] = anchor.Y.ToString("R", CultureInfo.InvariantCulture),
                    ["createdAt"] = anchor.CreatedAt.ToString("O")
                }
            },
            showRejectedBubble: true);
    }

    private async Task<bool> ExecuteAnchorProposalAsync(
        BehaviorProposal proposal,
        CancellationToken cancellationToken)
    {
        if (!proposal.Data.TryGetValue("x", out var xText) ||
            !proposal.Data.TryGetValue("y", out var yText) ||
            !double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;
        var isFood = proposal.BehaviorId == "anchor.food.approach";
        var anchor = new InteractionAnchor(
            isFood ? InteractionAnchorKind.Food : InteractionAnchorKind.Toy,
            x,
            y,
            proposal.CreatedAt);
        var actionToken = BeginAction(
            proposal.BehaviorId,
            isFood ? "朝食物锚点移动并在目标处进食" : "朝玩具锚点移动并在目标处扑玩",
            isFood ? RunRightSequence : FreeWalkRightSequence);
        _activeAnchorIsFood = isFood;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            actionToken,
            cancellationToken);
        var token = linked.Token;
        var completed = false;
        try
        {
            var move = new DesktopMoveRequestEventArgs(
                DesktopMoveMode.AnchorApproach,
                TimeSpan.FromSeconds(5),
                token,
                target: new DesktopPoint(anchor.X, anchor.Y));
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            try { completed = await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return false; }
            if (!completed)
            {
                _ = ShowBubbleAsync("那个位置我现在过不去。换一个近一点的地方吧。", 3600);
                return false;
            }

            PlaySequence(isFood ? FreezeDriedEatingLoopSequence : LaserPounceSequence);
            _ = ShowBubbleAsync(
                isFood ? "找到了。这个位置吃起来很安心。" : "抓到光点了。再放一个试试。",
                3600,
                isFood ? PetSpeechIntent.General : PetSpeechIntent.Play);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(isFood ? 4.5 : 5.5), token))
                return false;

            if (isFood)
            {
                _memory.State.Fullness += 6;
                _memory.State.Happiness += 2;
                _memory.Personality.Runtime.Safety += 0.01;
            }
            else
            {
                _memory.State.Happiness += 3;
                _memory.State.Energy -= 2;
                _memory.Personality.Runtime.PlayDesire -= 0.05;
                _memory.Personality.Runtime.Fatigue += 0.025;
            }
            _memory.State.Clamp();
            _memory.Personality.Runtime.Clamp();
            await _memory.RecordAsync(
                "owner_anchor",
                isFood
                    ? "主人在桌面投放食物锚点，pupu接受后移动到目标并短暂进食。"
                    : "主人在桌面放置激光点，pupu接受后低伏追到目标并短暂扑抓。",
                proposal.BehaviorId,
                0.46,
                0.52,
                true,
                isFood ? "feed_anchor" : "laser_anchor",
                $"x={anchor.X:0};y={anchor.Y:0};created={anchor.CreatedAt:O}",
                isFood ? "routines:freeze-dried-eating-loop" : "activity:laser-pounce");
            await _memory.SaveStateAsync();
            return true;
        }
        finally
        {
            _activeAnchorIsFood = false;
            EndAction(
                isFood ? ProneIdleSequence : SideLieIdleSequence,
                expectedToken: actionToken);
        }
    }

    private async Task CageAsync()
    {
        if (!TryAcceptBehaviorRequest(
                "owner.cage",
                BehaviorArbitrationSource.OwnerForced,
                BehaviorPriority.OwnerForced,
                TimeSpan.Zero,
                TimeSpan.Zero,
                interruptible: false,
                BehaviorStateBlockers.Traveling,
                forceInterrupt: true))
            return;

        ForceStopForRestrictedState("owner_cage");
        _memory.State.IsCaged = true;
        _memory.State.Happiness -= 1;
        _memory.Personality.Runtime.Stress += 0.018;
        _memory.ApplyRelationshipDelta(trust: -0.0008);
        _memory.State.Clamp();
        _memory.Personality.Runtime.Clamp();
        SetBehavior(
            "owner.cage",
            "关笼子／原地锁定，等待主人释放",
            "owner_forced",
            "desktop",
            "Actions:pupu-cage-rest-youthful-v14.png");
        PlaySequence(CageRestSequence);
        await _memory.SaveStateAsync();
        await _memory.RecordAsync(
            "owner_forced",
            "主人暂时把pupu关进笼子；pupu原地锁定，保留轻微表情，直到主人释放。",
            "owner.cage",
            0.45,
            -0.14,
            true,
            "confinement",
            "desktop",
            "routines:prone-idle");
        RaiseRestrictedStateProperties();
        _ = ShowBubbleAsync("我先待在这里。记得等会儿放我出来。", 5200);
    }

    private async Task ReleaseCageAsync()
    {
        if (!IsCaged) return;
        if (!TryAcceptBehaviorRequest(
                "owner.cage.release",
                BehaviorArbitrationSource.OwnerForced,
                BehaviorPriority.OwnerForced,
                TimeSpan.Zero,
                TimeSpan.Zero,
                interruptible: true,
                BehaviorStateBlockers.None,
                forceInterrupt: true))
            return;

        _memory.State.IsCaged = false;
        _memory.Personality.Runtime.Stress -= 0.012;
        _memory.Personality.Runtime.Clamp();
        await _memory.SaveStateAsync();
        ResetArbitrationToIdle();
        SetIdleAnimation();
        RaiseRestrictedStateProperties();
        await _memory.RecordAsync(
            "owner_forced",
            "主人释放pupu，原地锁定结束，普通行为恢复。",
            "owner.cage.release",
            0.38,
            0.18,
            true,
            "confinement",
            "desktop",
            "routines:prone-idle");
        _ = ShowBubbleAsync("门开啦。我要先把四只爪子都伸一伸。", 4200);
    }

    private async Task StartTravelAsync(string destination, TimeSpan duration)
    {
        if (IsTraveling) return;
        if (!TryAcceptBehaviorRequest(
                "travel.depart",
                BehaviorArbitrationSource.OwnerForced,
                BehaviorPriority.OwnerForced,
                TimeSpan.Zero,
                TimeSpan.Zero,
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Petrified,
                forceInterrupt: true))
            return;

        var destinations = new[]
        {
            "海边的小旅馆", "安静的山谷", "有风的草原", "旧城的窗台", "湖边木屋", "月牙形的小岛"
        };
        destination = string.IsNullOrWhiteSpace(destination)
            ? destinations[_random.Next(destinations.Length)]
            : destination.Trim();
        if (destination.Length > 48) destination = destination[..48];
        duration = TimeSpan.FromMinutes(
            Math.Clamp(duration.TotalMinutes, 15, TimeSpan.FromHours(24).TotalMinutes));

        ForceStopForRestrictedState("travel_depart");
        var now = _clock.Now;
        _memory.State.Travel = new PetTravelState
        {
            IsTraveling = true,
            Destination = destination,
            DepartedAt = now,
            ReturnsAt = now.Add(duration),
            LastStory = _memory.State.Travel.LastStory
        };
        _memory.State.Travel.Normalize();
        SetBehavior(
            "travel.away",
            $"外出旅行：{destination}",
            "travel",
            "away",
            "local:travel");
        await _memory.SaveStateAsync();
        await _memory.RecordLightweightEventAsync(
            "travel",
            $"pupu出发去{destination}，计划在24小时内返回；这是一条轻量本地事件。",
            "travel.depart",
            0.52,
            0.28,
            $"destination={destination};minutes={duration.TotalMinutes:0}",
            "local:travel");
        TravelDestinationInput = destination;
        RaiseRestrictedStateProperties();
        _ = ShowBubbleAsync(
            $"我要去{destination}看看。大尾巴会自己照顾好，回来再讲给你听。",
            6200);
    }

    private async Task ReturnFromTravelAsync(bool recalled)
    {
        if (!IsTraveling) return;
        if (!TryAcceptBehaviorRequest(
                recalled ? "travel.recall" : "travel.return",
                BehaviorArbitrationSource.OwnerForced,
                BehaviorPriority.OwnerForced,
                TimeSpan.Zero,
                TimeSpan.Zero,
                interruptible: true,
                BehaviorStateBlockers.None,
                forceInterrupt: true))
            return;

        var destination = _memory.State.Travel.Destination;
        var details = new[]
        {
            "在窗边看了很久的云，还把一片叶子当成了会逃跑的玩具",
            "找到一块晒得暖暖的地方，睡醒后认真记住了回来的路",
            "遇到一阵带着陌生气味的风，先躲好，再慢慢探头观察",
            "看见远处的灯一盏盏亮起来，决定把最亮的那盏讲给主人听"
        };
        var story =
            $"朴朴从{destination}回来了：{details[_random.Next(details.Length)]}。" +
            (recalled ? "主人提前召回，所以这次旅程短了一点。" : "时间到了，朴朴按约定自己回家。");
        _memory.State.Travel.IsTraveling = false;
        _memory.State.Travel.LastStory = story;
        _memory.State.Travel.Normalize();
        await _memory.SaveStateAsync();
        ResetArbitrationToIdle();
        SetBehavior(
            "travel.return",
            recalled ? "被主人召回并分享轻量旅行经历" : "旅行到期返回并分享轻量旅行经历",
            "travel",
            "desktop",
            "local:travel-story");
        PlaySequence(ProneIdleSequence);
        await _memory.RecordLightweightEventAsync(
            "travel",
            story,
            recalled ? "travel.recall" : "travel.return",
            0.58,
            0.46,
            $"destination={destination};recalled={recalled}",
            "local:travel-story");
        await TryIndexTravelExperienceAsync(
            destination,
            story,
            _clock.Now,
            recalled);
        RaiseRestrictedStateProperties();
        _ = ShowBubbleAsync(story, 9000);
        ScheduleNextAutonomousAction();
    }

    private void ForceStopForRestrictedState(string reason)
    {
        EndCursorGaze();
        MouseInteractionMode = MouseInteractionMode.Attention;
        _actionScheduler.Stop(reason);
        _touchReactionCancellation?.Cancel();
        _isTouchEscaping = false;
        _activeMoveMode = null;
        _busyAction = false;
        if (_isPetrified)
        {
            _isPetrified = false;
            _isCoinBackVisible = false;
            if (_petrificationSession is { IsTerminal: false } session)
                _ = _interactionLifecycle.InterruptAsync(session, reason);
            if (ReferenceEquals(_activeInteraction, _petrificationSession))
                _activeInteraction = null;
            _petrificationSession = null;
            OnPropertyChanged(nameof(IsPetrified));
        }
        OnPropertyChanged(nameof(IsLongActionRunning));
        OnPropertyChanged(nameof(IsHarnessedWalkActive));
        OnPropertyChanged(nameof(IsFreeRoamActive));
    }

    private void RaiseRestrictedStateProperties()
    {
        OnPropertyChanged(nameof(IsCaged));
        OnPropertyChanged(nameof(IsTraveling));
        OnPropertyChanged(nameof(IsPetOnDesktop));
        OnPropertyChanged(nameof(IsMovementLocked));
        OnPropertyChanged(nameof(ConfinementStatus));
        OnPropertyChanged(nameof(TravelStatus));
        OnPropertyChanged(nameof(AwayDesktopStatus));
        RaiseCommands();
    }

    private async Task<string?> HandleLocalInteractionCommandAsync(string input)
    {
        var command = _localCommandParser.Parse(input);
        switch (command.Intent)
        {
            case LocalInteractionIntent.None:
                return null;
            case LocalInteractionIntent.QuietForAWhile:
            {
                var proposal = await SubmitBehaviorProposalAsync(
                    new BehaviorProposal
                    {
                        BehaviorId = "command.quiet",
                        Source = BehaviorArbitrationSource.DialogueCommand,
                        Priority = BehaviorPriority.ExplicitCommand,
                        CreatedAt = _clock.Now,
                        ExpiresAt = _clock.Now.AddSeconds(15),
                        Reason = "本地口令：安静一会",
                        Cooldown = TimeSpan.FromSeconds(2),
                        Interruptible = true,
                        ForbiddenStates = BehaviorStateBlockers.Traveling,
                        CooldownKey = "command.quiet"
                    });
                return proposal?.State == BehaviorProposalState.Completed
                    ? "好。我先安静趴一会儿，半小时内少走动，也不主动打扰你。"
                    : "我听见了，已经排进本地行为队列；如果当前动作一直不能打断，它会自动过期。";
            }
            case LocalInteractionIntent.AllowSelfPlay:
            {
                var participation = _participationEvaluator.Evaluate(
                    _memory.Personality,
                    OwnerInteractionKind.WandPlay,
                    new OwnerInteractionContext(
                        _memory.State.Fullness,
                        _memory.State.Energy,
                        _memory.State.Cleanliness,
                        _memory.State.LitterLevel,
                        "command=self_play"),
                    _random.NextDouble());
                if (!participation.Accepted)
                {
                    ResetArbitrationToIdle();
                    return "我知道可以自己玩，不过现在更想安静看一会儿。";
                }
                var proposal = await SubmitBehaviorProposalAsync(
                    new BehaviorProposal
                    {
                        BehaviorId = "command.self_play",
                        Source = BehaviorArbitrationSource.DialogueCommand,
                        Priority = BehaviorPriority.ExplicitCommand,
                        CreatedAt = _clock.Now,
                        ExpiresAt = _clock.Now.AddSeconds(15),
                        Reason = "本地口令：自己玩吧",
                        Cooldown = TimeSpan.FromSeconds(2),
                        Interruptible = true,
                        ForbiddenStates =
                            BehaviorStateBlockers.Caged |
                            BehaviorStateBlockers.Traveling |
                            BehaviorStateBlockers.Petrified,
                        CooldownKey = "command.self_play"
                    });
                return proposal?.State == BehaviorProposalState.Completed
                    ? "知道啦。我想玩时会自己找点低调的，不会现在立刻乱跑。"
                    : "我现在腾不开爪子，口令会短暂等待，过期后不会突然补做。";
            }
            case LocalInteractionIntent.FoodAnchor:
                ActivateAnchorMode(MouseInteractionMode.FoodAnchor);
                return MouseInteractionMode == MouseInteractionMode.FoodAnchor
                    ? "点一下桌面位置，我会把那里当作食物锚点。"
                    : "我现在不方便追食物锚点，等这个动作结束再试。";
            case LocalInteractionIntent.ToyAnchor:
                ActivateAnchorMode(MouseInteractionMode.ToyAnchor);
                return MouseInteractionMode == MouseInteractionMode.ToyAnchor
                    ? "点一下桌面位置，我会先判断要不要追这个玩具。"
                    : "我看见玩具邀请了，但现在还不能进入锚点模式。";
            case LocalInteractionIntent.Cage:
                await CageAsync();
                return IsCaged ? "好，我先原地待在笼子里，等你释放。" : "现在不能关笼子。";
            case LocalInteractionIntent.ReleaseCage:
                await ReleaseCageAsync();
                return IsCaged ? "门还没有打开。" : "已经释放，普通行为恢复了。";
            case LocalInteractionIntent.Travel:
                await StartTravelAsync(
                    command.Destination,
                    command.Duration ?? TimeSpan.FromHours(1));
                return IsTraveling
                    ? $"我要去{_memory.State.Travel.Destination}，最晚24小时内回来。"
                    : "这次还不能出发。";
            case LocalInteractionIntent.RecallTravel:
                await ReturnFromTravelAsync(recalled: true);
                return IsTraveling ? "我还在回来的路上。" : _memory.State.Travel.LastStory;
            default:
                return null;
        }
    }

    public void RegisterSystemPerception(string kind)
    {
        if (!IsReady || _disposed) return;
        _perception.Accept(new PerceptionEvent
        {
            Timestamp = _clock.Now,
            Source = "operating_system",
            Kind = kind,
            Confidence = 1,
            Ttl = TimeSpan.FromSeconds(8),
            DeduplicationKey = $"operating_system:{kind}",
            Priority = PerceptionPriority.Important,
            Intensity = 1
        }, _clock.Now);
    }

    public void UpdateDesktopEnvironment(DesktopEnvironmentSnapshot snapshot)
    {
        _desktopEnvironment = snapshot;
    }

    public async Task NotifySuspendingAsync()
    {
        if (!IsReady || _disposed) return;
        _memory.MarkSuspended();
        await _memory.SaveStateAsync();
    }

    public async Task NotifyResumedAsync()
    {
        if (!IsReady || _disposed) return;
        _memory.RestoreAfterResume();
        _lastActiveStateTickAt = _clock.Now;
        PrepareDailyToiletPlan(_clock.Now, skipPastPending: true);
        RegisterSystemPerception("system_resume");
        await _memory.SaveStateAsync();
        RefreshAll();
    }

    public void RegisterPointerUp(double x, double y, bool windowDrag = false)
    {
        if (!IsReady || _disposed) return;
        var events = _gestureInterpreter.PointerUp(x, y, _currentBehaviorKey, windowDrag);
        _ = ProcessGestureEventsAsync(events);
    }

    private async Task ProcessGestureEventsAsync(IReadOnlyList<GestureEvent> events)
    {
        var gesture = events.First(x => x.Kind != GestureKind.Release);
        var release = events.First(x => x.Kind == GestureKind.Release);
        if (_lastInitiativeWasIgnored)
        {
            _lastInitiativeWasIgnored = false;
            _interactionSessions.MarkUserResponse();
            _interactionSessions.EndActive("user_responded");
            _memory.ApplyRelationshipDelta(initiativeAcceptance: 0.0015);
        }

        // Input interpretation is still recorded while an action is running,
        // but it does not forcibly replace an active long-action animation.
        if (_busyAction || _isTouchEscaping)
        {
            await _memory.RecordAsync(
                "gesture",
                $"长动作期间收到 {gesture.Kind}，未强制绑定动画。",
                $"gesture.{gesture.Kind.ToString().ToLowerInvariant()}",
                0.20,
                0,
                true,
                "gesture",
                $"x={gesture.X:0};y={gesture.Y:0}",
                _currentAnimationSource);
            _ = ShowBubbleAsync(null, 2400, PetSpeechIntent.Busy);
            return;
        }

        AreQuickActionsVisible = false;
        var touchProfile = _memory.GetTouchReactionProfile();
        var recentPetting = gesture.RecentInteractionHistory.Count(x =>
            x is "touch" or "stroke" or "hold") + 1;
        var pettingLoad = Math.Clamp((recentPetting - 1) / 6.0, 0, 1.25);
        var rapidTapPressure = gesture.Kind == GestureKind.RapidTap
            ? Math.Clamp(
                (gesture.RecentTapCount - touchProfile.AnnoyedAt + 1) /
                (double)Math.Max(1, touchProfile.AngryAt - touchProfile.AnnoyedAt + 1),
                0,
                1.25)
            : 0;
        var boundaryPressure = gesture.Kind switch
        {
            GestureKind.Drag => 1.0,
            GestureKind.LiftIntent => 0.82,
            GestureKind.Hold => 0.52,
            GestureKind.RapidTap => rapidTapPressure,
            GestureKind.Stroke when recentPetting >= 5 => pettingLoad,
            _ => 0
        };
        var escapePressure = gesture.Kind switch
        {
            GestureKind.Drag when _memory.Personality.Runtime.Stress >= 0.72 => 1.0,
            GestureKind.LiftIntent when _memory.Personality.Runtime.Stress >= 0.82 => 0.95,
            GestureKind.RapidTap when gesture.RecentTapCount >= touchProfile.AngryAt => 1.0,
            GestureKind.Stroke when recentPetting >= 8 &&
                                    _memory.Personality.Runtime.Stress >= 0.72 => 0.94,
            _ => 0
        };
        _gestureStateUpdater.Apply(_memory.Personality, gesture);
        var contextKey = BuildGestureContext(gesture);
        var context = new BehaviorContext
        {
            Now = _clock.Now,
            ContextKey = contextKey,
            LocationKey = $"x{Math.Clamp((int)(gesture.X / 64), 0, 3)}-y{Math.Clamp((int)(gesture.Y / 64), 0, 3)}",
            TimeBucket = TimeBucket(_clock.Now),
            EnvironmentAllowsMovement = true,
            Signals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [gesture.Kind.ToString().ToLowerInvariant()] = 1,
                ["gesture_frequency"] = Math.Clamp(gesture.ClicksPerSecond / 6, 0, 1.5),
                ["petting_load"] = pettingLoad,
                ["boundary_pressure"] = boundaryPressure,
                ["escape_pressure"] = escapePressure
            }
        };
        context.RequestSource = BehaviorRequestSource.Touch;
        var decision = _agentKernel.Decide(
            BehaviorCatalog.TouchResponses,
            context,
            BuildArbitrationContext(),
            new BehaviorSelectionOptions
            {
                Source = BehaviorArbitrationSource.Touch,
                ActivePriority = BehaviorPriority.TouchFeedback,
                PassivePriority = BehaviorPriority.TouchFeedback,
                CommitAdmission = true,
                MinimumDurationOverride = TimeSpan.FromSeconds(4),
                CooldownOverride = TimeSpan.FromMilliseconds(450),
                InterruptibleOverride = false,
                CooldownKey = "touch.feedback",
                ForbiddenStates =
                    BehaviorStateBlockers.Caged |
                    BehaviorStateBlockers.Traveling |
                    BehaviorStateBlockers.Sleeping |
                    BehaviorStateBlockers.Toilet |
                    BehaviorStateBlockers.Magic |
                    BehaviorStateBlockers.Movement |
                    BehaviorStateBlockers.Feeding |
                    BehaviorStateBlockers.Playing |
                    BehaviorStateBlockers.Petrified
            });
        await LogDecisionAsync(decision);
        if (decision.Deferred || decision.Admission?.Accepted != true)
            return;
        _touchReactionCancellation?.Cancel();
        _touchReactionCancellation?.Dispose();
        _touchReactionCancellation = new CancellationTokenSource();
        var token = _touchReactionCancellation.Token;

        var (sequence, label, bubble, sentiment, importance) =
            decision.SelectedBehaviorId switch
            {
                "touch.enjoy" => (
                    _memory.Personality.Relationship.Trust >= 0.68
                        ? TrustTouchSequence
                        : PurrSequence,
                    _memory.Personality.Relationship.Trust >= 0.68
                        ? "高信任时主动走近、轻微放大并安心贴近"
                        : "根据手势、当前压力、关系和偏好选择享受触摸",
                    ComposePetSpeech(PetSpeechIntent.TouchEnjoy),
                    0.82,
                    0.62),
                "touch.curiosity" => (
                    CuriousTouchSequence,
                    "对这次触摸保持好奇并抬爪观察",
                    ComposePetSpeech(PetSpeechIntent.TouchCurious),
                    0.46,
                    0.52),
                "touch.warning" => (
                    AnnoyedTouchSequence,
                    "连续互动超过当前容忍后移开视线、甩尾并表达边界",
                    ComposePetSpeech(PetSpeechIntent.TouchBoundary),
                    -0.42,
                    0.72),
                "touch.avoid" => (
                    AnnoyedTouchSequence,
                    "当前不愿继续触摸，转身退开并寻找安静位置",
                    ComposePetSpeech(PetSpeechIntent.TouchAvoid),
                    -0.52,
                    0.76),
                "touch.run_away" => (
                    AngryTouchSequence,
                    "持续越界与高压力触发跑开；不哈气、不攻击主人",
                    ComposePetSpeech(PetSpeechIntent.TouchAvoid),
                    -0.72,
                    0.88),
                _ => (
                    GentleTouchSequence,
                    "接受但不主动延长这次触摸",
                    "知道了。我现在先这样待着。",
                    0.12,
                    0.42)
            };

        SetBehavior(
            decision.SelectedBehaviorId,
            label,
            "touch",
            contextKey,
            $"touch:{sequence.Name}");
        PlaySequence(sequence);
        if (sequence.Name == TrustTouchSequence.Name)
            _ = AnimateTrustApproachScaleAsync(token);
        var interactionSession = _interactionSessions.GetOrCreateTouch(
            decision.SelectedBehaviorId,
            contextKey,
            $"touch:{sequence.Name}");
        gesture.SessionId = interactionSession.Id;
        release.SessionId = interactionSession.Id;
        ApplyTouchOutcome(decision.SelectedBehaviorId);
        _ = ShowBubbleAsync(bubble, 3900);
        await _memory.RecordAsync(
            "gesture",
            $"GestureInterpreter: {gesture.Kind}, 位置({gesture.X:0},{gesture.Y:0}), " +
            $"频率{gesture.ClicksPerSecond:0.0}/s, 时长{gesture.DurationMilliseconds:0}ms, 拖动{gesture.DragDistance:0}px。",
            decision.SelectedBehaviorId,
            importance,
            sentiment,
            true,
            "touch",
            contextKey,
            $"touch:{sequence.Name}",
            interactionSession.Id);
        await _memory.RecordAsync(
            "gesture",
            $"GestureInterpreter: {release.Kind}, 当前behavior_id={decision.SelectedBehaviorId}。",
            "gesture.release",
            0.10,
            0,
            true,
            "release",
            contextKey,
            $"touch:{sequence.Name}",
            interactionSession.Id);

        if (decision.SelectedBehaviorId == "touch.run_away")
        {
            await RunTouchEscapeAsync(token);
            return;
        }

        try { await Task.Delay(decision.SelectedBehaviorId == "touch.enjoy" ? 6200 : 4600, token); }
        catch (OperationCanceledException) { return; }
        if (!token.IsCancellationRequested) SetIdleAnimation();
        RefreshAll();
    }

    private async Task AnimateTrustApproachScaleAsync(CancellationToken token)
    {
        try
        {
            const int steps = 18;
            for (var index = 1; index <= steps; index++)
            {
                token.ThrowIfCancellationRequested();
                var progress = index / (double)steps;
                InteractionScale = 1 + 0.06 * progress;
                await Task.Delay(85, token);
            }
            await Task.Delay(1500, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            InteractionScale = 1;
        }
    }

    private void ApplyTouchOutcome(string behaviorId)
    {
        switch (behaviorId)
        {
            case "touch.enjoy":
                _memory.State.Happiness += 2.2;
                _memory.Personality.Runtime.Stress -= 0.025;
                _memory.ApplyRelationshipDelta(
                    trust: 0.0015,
                    familiarity: 0.001,
                    touchAcceptance: 0.002);
                break;
            case "touch.curiosity":
                _memory.State.Happiness += 0.8;
                _memory.Personality.Runtime.Curiosity += 0.03;
                _memory.ApplyRelationshipDelta(familiarity: 0.0008);
                break;
            case "touch.warning":
                _memory.State.Happiness -= 0.8;
                _memory.ApplyRelationshipDelta(touchAcceptance: -0.001);
                break;
            case "touch.avoid":
            case "touch.run_away":
                _memory.State.Happiness -= 1.2;
                _memory.ApplyRelationshipDelta(touchAcceptance: -0.0015);
                break;
        }
        _memory.Personality.Runtime.Clamp();
    }

    private async Task RunTouchEscapeAsync(CancellationToken token)
    {
        _isTouchEscaping = true;
        RaiseCommands();
        try
        {
            try { await Task.Delay(1200, token); }
            catch (OperationCanceledException) { return; }
            var duration = TimeSpan.FromSeconds(_memory.GetTouchReactionProfile().EscapeSeconds);
            var move = new DesktopMoveRequestEventArgs(
                DesktopMoveMode.AngryEscape,
                duration,
                token);
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            try { await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
            SetBehavior(
                "avoid.quiet_place",
                "根据压力、安全感和冷却短暂保持距离",
                "touch",
                "post_escape",
                "life:prone-observe");
            PlaySequence(ProneIdleSequence);
            try { await Task.Delay(2600, token); }
            catch (OperationCanceledException) { return; }
            SetIdleAnimation();
        }
        finally
        {
            _isTouchEscaping = false;
            RaiseCommands();
            RefreshAll();
        }
    }

    private async Task PlayWandAsync()
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.WandPlay, "玩逗猫棒")) return;
        const string behaviorId = "play.accept_toy";
        var context = $"toy=wand;time={TimeBucket(_clock.Now)}";
        var acceptanceContext = new BehaviorContext
        {
            Now = _clock.Now,
            RequestSource = BehaviorRequestSource.Owner,
            CurrentBehaviorId = _currentBehaviorKey,
            CurrentBehaviorStartedAt = DateTimeOffset.MinValue,
            CurrentBehaviorInterruptible = true,
            IsDeepNight = _clock.Now.Hour >= 23 || _clock.Now.Hour < 7,
            DoNotDisturb = _memory.BehaviorPolicy.DoNotDisturb,
            MeetingMode = _memory.BehaviorPolicy.MeetingMode,
            FullScreen = _memory.BehaviorPolicy.SuppressHighDisruptionInFullScreen &&
                         _desktopEnvironmentProbe.IsForegroundApplicationFullScreen(),
            ContextKey = context,
            LocationKey = "desktop",
            TimeBucket = TimeBucket(_clock.Now),
            Signals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["toy_available"] = 1
            }
        };
        var decision = _agentKernel.Decide(
            new[]
            {
                BehaviorCatalog.Find(behaviorId)!,
                BehaviorCatalog.Find("idle.prone_observe")!
            },
            acceptanceContext,
            BuildArbitrationContext(),
            new BehaviorSelectionOptions
            {
                Source = BehaviorArbitrationSource.PanelCommand,
                ActivePriority = BehaviorPriority.ExplicitCommand,
                PassivePriority = BehaviorPriority.ExplicitCommand
            });
        await LogDecisionAsync(decision);
        if (!string.Equals(decision.SelectedBehaviorId, behaviorId, StringComparison.Ordinal))
        {
            SetBehavior(
                "play.decline_toy",
                "当前压力、疲劳或偏好使pupu暂时不想接受玩具",
                "play",
                context,
                "routines:prone-idle");
            PlaySequence(ProneIdleSequence);
            _ = ShowBubbleAsync("我看到玩具了，不过现在想安静一会儿。", 3600);
            await _memory.RecordAsync(
                "play",
                "主人提供逗猫棒；pupu根据当前状态与具体玩具偏好选择暂不接受。",
                "play.decline_toy",
                0.24,
                0.05,
                true,
                "play",
                context,
                "routines:prone-idle");
            return;
        }
        if (!TryAcceptBehaviorRequest(
                behaviorId,
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;

        var token = BeginAction(behaviorId, "盯住红色逗猫棒、伏低、挥爪并扑跳", WandIntroSequence);
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "play",
            context,
            "core:wand-intro");
        _activeInteraction = session;
        var completed = false;
        var total = TimeSpan.FromSeconds(_memory.BehaviorPolicy.WandPlaySeconds);
        try
        {
            _ = ShowBubbleAsync($"别动！这根羽毛我要玩 {FormatDuration(total)}。", 4800);
            completed = await RunProgressiveInteractionAsync(
                session,
                total,
                TimeSpan.FromSeconds(2.1),
                5,
                token,
                fraction =>
                {
                    PlaySequence(WandLoopSequence, restart: false);
                    var happiness = 11 * fraction;
                    var energy = -7 * fraction;
                    _memory.State.Happiness += happiness;
                    _memory.State.Energy += energy;
                    _memory.Personality.Runtime.PlayDesire -= 0.20 * fraction;
                    _memory.Personality.Runtime.Fatigue += 0.12 * fraction;
                    _memory.Personality.Runtime.Arousal += 0.08 * fraction;
                    return new[]
                    {
                        new AppliedEffect("happiness", happiness, "points"),
                        new AppliedEffect("energy", energy, "points"),
                        new AppliedEffect("runtime.play_desire", -0.20 * fraction),
                        new AppliedEffect("runtime.fatigue", 0.12 * fraction)
                    };
                });
            if (!completed) return;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                "play",
                "主人用红色逗猫棒陪pupu持续玩耍，pupu观察、伏低、挥爪并小扑。",
                behaviorId,
                0.68,
                0.85,
                true,
                "play",
                context,
                "core:wand-loop");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(SideLieIdleSequence, expectedToken: token);
        }
    }

    private async Task PlayLaserAsync()
    {
        ActivateAnchorMode(MouseInteractionMode.ToyAnchor);
        await Task.CompletedTask;
    }

    private async Task LieDownAsync()
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.PosePlay, "原地趴下")) return;
        if (!TryAcceptBehaviorRequest(
                "idle.owner_lie_down",
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(3),
                interruptible: true,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;
        var settle = _random.NextDouble() < 0.58 ? ProneIdleSequence : SideLieIdleSequence;
        var token = BeginAction("idle", "原地趴着或侧躺，缓慢呼吸、眨眼和甩尾尖", settle);
        _ = ShowBubbleAsync("我就在这里趴一会儿。不是困。", 3200);
        if (!await WaitPhaseAsync(TimeSpan.FromSeconds(4.3), token)) return;
        EndAction(settle, restart: false, expectedToken: token);
    }

    private async Task RollAsync()
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.PosePlay, "侧躺打滚")) return;
        if (!TryAcceptBehaviorRequest(
                "play.roll",
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(4),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;
        var token = BeginAction("roll", "侧躺、露肚皮并慢慢翻身", RollSequence);
        _ = ShowBubbleAsync("肚皮只是路过，不代表可以一直摸。", 3800);
        if (!await WaitPhaseAsync(TimeSpan.FromSeconds(3.9), token)) return;
        await _memory.RecordAsync(
            "expression",
            "pupu在桌面原地侧躺并露肚皮打滚。",
            "play.roll",
            0.36,
            0.4,
            true,
            "play",
            $"time={TimeBucket(_clock.Now)}",
            "core:roll");
        EndAction(SideLieIdleSequence, expectedToken: token);
    }

    private async Task SpinAsync()
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.PosePlay, "追尾转圈")) return;
        if (!TryAcceptBehaviorRequest(
                "play.tail_chase",
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;
        var token = BeginAction("spin", "追着自己的大尾巴快速转一圈", SpinSequence);
        _ = ShowBubbleAsync("尾巴怎么又跑到后面去了？", 3100);
        if (!await WaitPhaseAsync(TimeSpan.FromSeconds(2.5), token)) return;
        await _memory.RecordAsync(
            "expression",
            "pupu追着自己的大尾巴原地转了一圈。",
            "play.tail_chase",
            0.34,
            0.35,
            true,
            "play",
            $"time={TimeBucket(_clock.Now)}",
            "core:spin");
        EndAction(ProneIdleSequence, expectedToken: token);
    }

    private async Task AccioBroomAsync(
        bool autonomous,
        bool alreadyAdmitted = false)
    {
        const string behaviorId = "magic.accio_broom";
        if (!alreadyAdmitted &&
            !TryAcceptBehaviorRequest(
                behaviorId,
                autonomous
                    ? BehaviorArbitrationSource.ContinuousEffect
                    : BehaviorArbitrationSource.OwnerForced,
                autonomous
                    ? BehaviorPriority.ContinuousEffect
                    : BehaviorPriority.OwnerForced,
                TimeSpan.FromSeconds(8),
                autonomous ? TimeSpan.FromMinutes(2) : TimeSpan.Zero,
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified,
                forceInterrupt: !autonomous))
            return;
        var token = BeginAction(
            behaviorId,
            "披上斗篷召来扫帚，并在当前桌面随机飞行一分钟",
            AccioBroomIntroSequence);
        var context = $"source={(autonomous ? "autonomous" : "owner")};time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "magic",
            context,
            "specials:magic-accio-broom");
        _activeInteraction = session;
        var completed = false;
        try
        {
            _ = ShowBubbleAsync("Accio Broom！扫帚，过来。", 4200, PetSpeechIntent.Play);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(3.2), token)) return;
            PlaySequence(BroomFlightSequence);
            var move = new DesktopMoveRequestEventArgs(
                DesktopMoveMode.BroomFlight,
                TimeSpan.FromMinutes(1),
                token);
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            try { completed = await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
            if (!completed) return;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                autonomous ? "autonomous_magic" : "owner_magic",
                "pupu念出 Accio Broom，披着斗篷骑扫帚在桌面随机飞行一分钟。",
                behaviorId,
                0.78,
                0.62,
                !autonomous,
                "magic",
                context,
                "specials:magic-accio-broom");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    private async Task ApparateAsync(
        bool autonomous,
        bool alreadyAdmitted = false)
    {
        const string behaviorId = "magic.apparate";
        if (!alreadyAdmitted &&
            !TryAcceptBehaviorRequest(
                behaviorId,
                autonomous
                    ? BehaviorArbitrationSource.ContinuousEffect
                    : BehaviorArbitrationSource.OwnerForced,
                autonomous
                    ? BehaviorPriority.ContinuousEffect
                    : BehaviorPriority.OwnerForced,
                TimeSpan.FromSeconds(5),
                autonomous ? TimeSpan.FromMinutes(2) : TimeSpan.Zero,
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified,
                forceInterrupt: !autonomous))
            return;
        var token = BeginAction(
            behaviorId,
            "披上斗篷原地转圈，消失几秒后在当前屏幕另一处出现",
            ApparateSequence);
        var context = $"source={(autonomous ? "autonomous" : "owner")};time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "magic",
            context,
            "specials:magic-apparate");
        _activeInteraction = session;
        var completed = false;
        try
        {
            _ = ShowBubbleAsync("Apparate！这次要落在哪里呢？", 3600, PetSpeechIntent.Play);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(1.9), token)) return;
            var move = new DesktopMoveRequestEventArgs(
                DesktopMoveMode.Apparate,
                TimeSpan.FromSeconds(3),
                token);
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            try { completed = await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
            if (!completed) return;
            // Reappearance deliberately remains magical for a short beat before
            // returning to the ordinary body pose. This closes the visual arc
            // instead of snapping from an invisible window straight to idle.
            PlaySequence(ApparateReappearSequence);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(1.65), token)) return;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                autonomous ? "autonomous_magic" : "owner_magic",
                "pupu使用 Apparate，原地旋转消失后在当前屏幕另一处重新出现。",
                behaviorId,
                0.72,
                0.48,
                !autonomous,
                "magic",
                context,
                "specials:magic-apparate");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    private async Task PetrificusTotalusAsync(
        bool autonomous,
        bool alreadyAdmitted = false)
    {
        const string behaviorId = "magic.petrificus_totalus";
        if (!alreadyAdmitted &&
            !TryAcceptBehaviorRequest(
                behaviorId,
                autonomous
                    ? BehaviorArbitrationSource.ContinuousEffect
                    : BehaviorArbitrationSource.OwnerForced,
                autonomous
                    ? BehaviorPriority.ContinuousEffect
                    : BehaviorPriority.OwnerForced,
                TimeSpan.FromSeconds(5),
                autonomous ? TimeSpan.FromMinutes(2) : TimeSpan.Zero,
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified,
                forceInterrupt: !autonomous))
            return;
        var token = BeginAction(
            behaviorId,
            "逐渐僵硬成石像，再变成印有头像的银币，等待主人解除",
            PetrifySequence);
        var context = $"source={(autonomous ? "autonomous" : "owner")};time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "magic",
            context,
            "specials:magic-petrificus-totalus");
        _activeInteraction = session;
        _petrificationSession = session;
        try
        {
            _ = ShowBubbleAsync(
                "Petrificus Totalus！等我变成银币以后，要记得解除。",
                4600,
                PetSpeechIntent.Play);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(4.1), token))
            {
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
                _petrificationSession = null;
                if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
                return;
            }
            PlaySequence(ResolveCoinSequence("normalColor", SilverCoinSequence));
            _isCoinBackVisible = false;
            CoinFlipScaleX = 1;
            _isPetrified = true;
            RefreshPetrifiedCoinColor();
            OnPropertyChanged(nameof(IsPetrified));
            RaiseCommands();
            await _memory.RecordAsync(
                autonomous ? "autonomous_magic" : "owner_magic",
                "pupu使用 Petrificus Totalus 变成银币，保持在原地等待主人解除。",
                behaviorId,
                0.82,
                0.24,
                !autonomous,
                "magic",
                context,
                "gazeCoin:magic-petrificus-coin");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            _petrificationSession = null;
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(SideLieIdleSequence, expectedToken: token);
            throw;
        }
    }

    public async Task FlipPetrifiedCoinAsync()
    {
        if (!_isPetrified || _isCoinFlipRunning || _disposed) return;
        _isCoinFlipRunning = true;
        var turnToBack = !_isCoinBackVisible;
        PlaySequence(
            turnToBack
                ? ResolveCurrentCoinFrontSequence()
                : ResolveCoinSequence("back", SilverCoinBackSequence));
        try
        {
            const int halfSteps = 7;
            for (var index = 1; index <= halfSteps; index++)
            {
                if (!_isPetrified) return;
                CoinFlipScaleX = 1 - 0.84 * (index / (double)halfSteps);
                await Task.Delay(45, _lifetimeCancellation.Token);
            }
            PlaySequence(
                turnToBack
                    ? ResolveCoinSequence("back", SilverCoinBackSequence)
                    : ResolveCurrentCoinFrontSequence());
            for (var index = 1; index <= halfSteps; index++)
            {
                if (!_isPetrified) return;
                CoinFlipScaleX = 0.16 + 0.84 * (index / (double)halfSteps);
                await Task.Delay(45, _lifetimeCancellation.Token);
            }
            _isCoinBackVisible = turnToBack;
            PlaySequence(
                _isCoinBackVisible
                    ? ResolveCoinSequence("back", SilverCoinBackSequence)
                    : ResolveCurrentCoinFrontSequence());
        }
        catch (OperationCanceledException) { }
        finally
        {
            CoinFlipScaleX = 1;
            _isCoinFlipRunning = false;
        }
    }

    public void RefreshPetrifiedCoinColor()
    {
        if (!_isPetrified || _disposed) return;
        _coinColorRefreshedAt = _clock.Now;
        _nextAutomaticCoinRefreshAt = _clock.Now.AddMinutes(12);
        OnPropertyChanged(nameof(CoinColorFreshness));
        if (!_isCoinBackVisible)
            PlaySequence(ResolveCurrentCoinFrontSequence());
    }

    public double CoinColorFreshness
    {
        get
        {
            if (!_isPetrified) return 0;
            var elapsed = (_clock.Now - _coinColorRefreshedAt).TotalSeconds;
            return Math.Clamp(1 - elapsed / 75, 0, 1);
        }
    }

    private AnimationSequence ResolveCurrentCoinFrontSequence()
    {
        var unhappy = _memory.State.Happiness < 42;
        var color = CoinColorFreshness > 0.05;
        var key = (unhappy, color) switch
        {
            (true, true) => "unhappyColor",
            (true, false) => "unhappyFaded",
            (false, true) => "normalColor",
            _ => "normalFaded"
        };
        return ResolveCoinSequence(key, SilverCoinSequence);
    }

    private AnimationSequence ResolveCoinSequence(string key, AnimationSequence fallback)
    {
        if (!_assetPack.Manifest.CoinStates.TryGetValue(key, out var definition) ||
            !Enum.TryParse<SpriteAtlas>(definition.Atlas, true, out var atlas) ||
            definition.Frames.Count == 0)
            return fallback;
        var durations = definition.FrameDurations.Count == definition.Frames.Count
            ? definition.FrameDurations.Select(value => Math.Clamp(value, 80, 5000)).ToArray()
            : Enumerable.Repeat(1000, definition.Frames.Count).ToArray();
        return Sequence(
            $"coin-{key}",
            atlas,
            definition.Row,
            durations,
            definition.Frames.ToArray());
    }

    private async Task ReleasePetrificationAsync()
    {
        if (!_isPetrified) return;
        if (!TryAcceptBehaviorRequest(
                "magic.petrificus.release",
                BehaviorArbitrationSource.OwnerForced,
                BehaviorPriority.OwnerForced,
                TimeSpan.Zero,
                TimeSpan.Zero,
                interruptible: true,
                BehaviorStateBlockers.None,
                forceInterrupt: true))
            return;
        var expectedToken = _scheduledAction?.Token;
        var session = _petrificationSession;
        _isPetrified = false;
        _isCoinBackVisible = false;
        CoinFlipScaleX = 1;
        OnPropertyChanged(nameof(IsPetrified));
        SetBehavior(
            "magic.petrificus.release",
            "解除石化后完整伸懒腰，再恢复普通趴卧",
            "magic",
            $"time={TimeBucket(_clock.Now)}",
            "core:magic-petrificus-release-stretch");
        PlaySequence(PetrificationReleaseStretchSequence);
        _ = ShowBubbleAsync("终于能动啦。先把爪子和腰都伸开。", 4200, PetSpeechIntent.General);
        RaiseCommands();
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(6.1),
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) { }

        if (session is { IsTerminal: false })
            await _interactionLifecycle.CompleteAsync(session);
        if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
        _petrificationSession = null;
        if (!_disposed)
        {
            SetBehavior(
                "idle.prone_observe",
                "伸完懒腰后恢复普通低趴",
                "autonomous",
                $"time={TimeBucket(_clock.Now)};location=desktop",
                "routines:prone-idle");
            EndAction(
                ProneIdleSequence,
                expectedToken: expectedToken);
        }
        RaiseCommands();
    }

    private async Task ScourgifyAsync(
        bool autonomous,
        bool alreadyAdmitted = false)
    {
        const string behaviorId = "magic.scourgify";
        if (!alreadyAdmitted &&
            !TryAcceptBehaviorRequest(
                behaviorId,
                autonomous
                    ? BehaviorArbitrationSource.ContinuousEffect
                    : BehaviorArbitrationSource.OwnerForced,
                autonomous
                    ? BehaviorPriority.ContinuousEffect
                    : BehaviorPriority.OwnerForced,
                TimeSpan.FromSeconds(8),
                autonomous ? TimeSpan.FromMinutes(2) : TimeSpan.Zero,
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified,
                forceInterrupt: !autonomous))
            return;
        var token = BeginAction(
            behaviorId,
            "挥动魔杖沿窗口或屏幕边缘擦出闪光，不修改桌面图标",
            ScourgifySequence);
        var context = $"source={(autonomous ? "autonomous" : "owner")};time={TimeBucket(_clock.Now)}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "magic",
            context,
            "specials:magic-scourgify");
        _activeInteraction = session;
        var completed = false;
        try
        {
            _ = ShowBubbleAsync("Scourgify！边边角角也要亮起来。", 4200, PetSpeechIntent.Play);
            var move = new DesktopMoveRequestEventArgs(
                DesktopMoveMode.EdgePolish,
                TimeSpan.FromSeconds(18),
                token,
                _desktopEnvironment.PreferredSurface);
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            try { completed = await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
            if (!completed) return;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                autonomous ? "autonomous_magic" : "owner_magic",
                "pupu使用 Scourgify，沿窗口或屏幕边缘擦出闪光；没有修改桌面图标。",
                behaviorId,
                0.70,
                0.52,
                !autonomous,
                "magic",
                context,
                "specials:magic-scourgify");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            SetBehavior(
                "idle.prone_observe",
                "擦拭光效结束后回到桌面低趴",
                "autonomous",
                $"time={TimeBucket(_clock.Now)};location=desktop",
                "routines:prone-idle");
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    private async Task GroomAsync()
    {
        if (!await TryParticipateAsync(OwnerInteractionKind.Grooming, "梳毛")) return;
        const string behaviorId = "care.groom";
        if (!TryAcceptBehaviorRequest(
                behaviorId,
                BehaviorArbitrationSource.PanelCommand,
                BehaviorPriority.ExplicitCommand,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified))
            return;
        var token = BeginAction(behaviorId, "坐好接受梳毛、梳大尾巴并舔爪整理", GroomIntroSequence);
        var context = $"location=desktop;time={TimeBucket(_clock.Now)}";
        var outcome = _interactionEvaluator.EvaluateGrooming(_memory.Personality, context, _clock.Now);
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "groom",
            context,
            "life:groom-intro");
        _activeInteraction = session;
        var completed = false;
        var total = TimeSpan.FromSeconds(_memory.BehaviorPolicy.GroomingSeconds);
        try
        {
            _ = ShowBubbleAsync(
                outcome.Acceptance < 0.38
                    ? "今天压力有点高，尾巴轻一点梳……"
                    : "大尾巴要慢慢梳得蓬蓬的。",
                4800);
            completed = await RunProgressiveInteractionAsync(
                session,
                total,
                TimeSpan.FromSeconds(2.2),
                4,
                token,
                fraction =>
                {
                    PlaySequence(GroomingLoopSequence, restart: false);
                    var cleanliness = outcome.CleanlinessDelta * fraction;
                    var happiness = outcome.HappinessDelta * fraction;
                    var stress = outcome.StressDelta * fraction;
                    _memory.State.Cleanliness += cleanliness;
                    _memory.State.Happiness += happiness;
                    _memory.Personality.Runtime.Stress += stress;
                    _memory.ApplyRelationshipDelta(
                        trust: outcome.Acceptance >= 0.5 ? 0.004 * fraction : 0,
                        touchAcceptance: (outcome.Acceptance - 0.5) * 0.004 * fraction);
                    return new[]
                    {
                        new AppliedEffect("cleanliness", cleanliness, "points"),
                        new AppliedEffect("happiness", happiness, "points"),
                        new AppliedEffect("runtime.stress", stress),
                        new AppliedEffect("groom_acceptance", outcome.Acceptance * fraction)
                    };
                });
            if (!completed) return;
            await _interactionLifecycle.CompleteAsync(session);
            await _memory.RecordAsync(
                "service",
                $"主人持续给pupu梳毛。上下文结果：{outcome.Explanation}",
                behaviorId,
                0.55,
                outcome.Acceptance * 2 - 1,
                true,
                "groom",
                context,
                "life:groom-loop");
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            EndAction(completed ? FocusSequence : SideLieIdleSequence, expectedToken: token);
        }
    }

    private async Task ApplyNaturalRuleAsync()
    {
        var input = NaturalRuleInput.Trim();
        if (input.Length == 0) return;
        NaturalRuleInput = string.Empty;
        var commandReply = await HandleLocalInteractionCommandAsync(input);
        if (commandReply is not null)
        {
            NaturalRuleStatus = $"已作为即时口令处理，不写入长期规则：{commandReply}";
            _ = ShowBubbleAsync(commandReply, 5200, PetSpeechIntent.Conversation);
            RefreshAll();
            return;
        }
        var result = await _memory.ApplyNaturalLanguageAsync(input);
        NaturalRuleStatus = result.Summary;
        _editableTraits = _memory.Profile.Baseline.Clone();
        RefreshNaturalRules();
        RefreshEditableTraits();
        RefreshAll();
        RefreshHiddenActionRules();
        ScheduleNextAutonomousAction();
        await RefreshEditableNotebookAsync();
        _ = ShowBubbleAsync(
            null,
            4200,
            result.Changed ? PetSpeechIntent.Remembered : PetSpeechIntent.RecoverableProblem);
    }

    private async Task SaveEditableMemoryAsync()
    {
        await _memory.SaveEditableNotebookAsync(EditableMemoryText);
        _editableTraits = _memory.Profile.Baseline.Clone();
        RefreshEditableTraits();
        RefreshNaturalRules();
        RefreshAll();
        await RefreshEditableNotebookAsync();
        EditableMemoryStatus = $"已保存并应用：{StoragePaths.EditableMemoryFile}";
        _ = ShowBubbleAsync(null, 4200, PetSpeechIntent.Remembered);
    }

    private async Task ReloadEditableMemoryAsync()
    {
        EditableMemoryText = await _memory.GetEditableNotebookAsync();
        await _memory.SaveEditableNotebookAsync(EditableMemoryText);
        _editableTraits = _memory.Profile.Baseline.Clone();
        RefreshEditableTraits();
        RefreshNaturalRules();
        RefreshAll();
        EditableMemoryStatus = $"已从磁盘重新载入：{StoragePaths.EditableMemoryFile}";
    }

    private async Task RefreshEditableNotebookAsync()
    {
        EditableMemoryText = await _memory.GetEditableNotebookAsync();
        OnPropertyChanged(nameof(PersonalityMemoryMatchSummary));
    }

    private static void OpenEditableMemoryFile()
    {
        Directory.CreateDirectory(StoragePaths.MemoryDirectory);
        Process.Start(new ProcessStartInfo(StoragePaths.EditableMemoryFile) { UseShellExecute = true });
    }

    private async Task CreateCodexIterationAsync()
    {
        var request = CodexIterationRequest.Trim();
        if (request.Length == 0) return;
        var context = await _memory.BuildChatContextAsync();
        try
        {
            CodexIterationStatus = await _codexIteration.CreateIterationRequestAsync(request, context, CodexProjectPath);
            CodexIterationRequest = string.Empty;
            await _memory.RecordAsync(
                "codex_iteration",
                $"主人为 Codex 创建了一项 pupu 迭代任务：{TrimForMemory(request)}",
                "codex_iteration", 0.74, 0.35);
            await RefreshEditableNotebookAsync();
            _ = ShowBubbleAsync("这件事交给你啦。朴朴先把尾巴放好。", 4200);
        }
        catch (Exception ex)
        {
            CodexIterationStatus = $"无法创建 Codex 迭代任务：{ex.Message}";
        }
    }

    private async Task SaveModelApiAsync()
    {
        try
        {
            await _modelApi.SaveAsync(_modelApiSettings, ModelApiKey);
            ModelApiKey = string.Empty;
            RefreshModelSettingBindings();
            OnPropertyChanged(nameof(HasStoredModelApiKey));
            ModelApiStatus = _modelApiSettings.Enabled
                ? "设置已保存。API 密钥只保存在 Windows 凭据管理器，不写入配置文件。"
                : "设置已保存，模型对话当前关闭。";
            _ = ShowBubbleAsync(
                null,
                3600,
                PetSpeechIntent.Remembered);
        }
        catch (Exception ex)
        {
            ModelApiStatus = $"设置保存失败：{ex.Message}";
            _ = ShowBubbleAsync(null, 3400, PetSpeechIntent.RecoverableProblem);
        }
    }

    private async Task TestModelApiAsync()
    {
        if (IsChatBusy) return;
        IsChatBusy = true;
        try
        {
            await _modelApi.SaveAsync(_modelApiSettings, ModelApiKey);
            ModelApiKey = string.Empty;
            RefreshModelSettingBindings();
            await _modelApi.TestAsync(
                _modelApiSettings,
                _memory.Personality,
                _memory.Profile.SelfIdentity,
                _lifetimeCancellation.Token);
            _modelApiSettings.Enabled = true;
            await _modelApi.SaveAsync(_modelApiSettings, null);
            OnPropertyChanged(nameof(ModelApiEnabled));
            OnPropertyChanged(nameof(HasStoredModelApiKey));
            ModelApiStatus = "连接测试成功，已启用模型回复；现在可直接在桌面或主人页和朴朴说话。";
            _ = ShowBubbleAsync(null, 3600, PetSpeechIntent.Conversation);
        }
        catch (Exception ex)
        {
            ModelApiStatus = $"连接测试失败：{ex.Message}";
            _ = ShowBubbleAsync(null, 3400, PetSpeechIntent.RecoverableProblem);
        }
        finally
        {
            IsChatBusy = false;
        }
    }

    private async Task DeleteModelApiKeyAsync()
    {
        _modelApi.DeleteStoredApiKey(_modelApiSettings);
        ModelApiKey = string.Empty;
        OnPropertyChanged(nameof(HasStoredModelApiKey));
        ModelApiStatus = "已从 Windows 凭据管理器删除模型 API 密钥。";
        await Task.CompletedTask;
    }

    private void RefreshModelSettingBindings()
    {
        OnPropertyChanged(nameof(ModelApiEndpoint));
        OnPropertyChanged(nameof(ModelApiModel));
        OnPropertyChanged(nameof(ModelApiRequestFormat));
        OnPropertyChanged(nameof(ModelApiProviderCapability));
    }

    private async Task SendChatAsync()
    {
        var input = ChatInput.Trim();
        if (input.Length == 0 || IsChatBusy) return;
        ChatInput = string.Empty;
        ChatMessages.Add(new ChatMessage { Role = "owner", Text = input });
        IsChatBusy = true;
        CurrentIntent = "user_chat";
        try
        {
            string reply;
            var localCommandReply = await HandleLocalInteractionCommandAsync(input);
            if (localCommandReply is not null)
            {
                reply = localCommandReply;
                ModelApiStatus = "本次口令由本地规则识别并经行为仲裁执行；未让模型控制状态。";
            }
            else if (!TryAcceptBehaviorRequest(
                         "conversation",
                         BehaviorArbitrationSource.DialogueCommand,
                         BehaviorPriority.ExplicitCommand,
                         TimeSpan.FromSeconds(3),
                         TimeSpan.FromMilliseconds(800),
                         interruptible: true,
                         BehaviorStateBlockers.Caged |
                         BehaviorStateBlockers.Traveling |
                         BehaviorStateBlockers.Toilet |
                         BehaviorStateBlockers.Magic |
                         BehaviorStateBlockers.Movement |
                         BehaviorStateBlockers.TouchReaction |
                         BehaviorStateBlockers.Feeding |
                         BehaviorStateBlockers.Playing |
                         BehaviorStateBlockers.Petrified,
                         cooldownKey: "conversation"))
            {
                reply = "我听见了，不过现在正忙着把这个动作做完整。等一下再认真回答你。";
                ModelApiStatus = "本次对话行为被统一仲裁拒绝；未调用模型。";
            }
            else if (_modelApiSettings.Enabled)
            {
                SetBehavior("conversation", "听主人说话并按照当前性格回应", "conversation");
                PlaySequence(ExpressionSequence);
                var memoryContext = await _memory.BuildChatContextAsync();
                memoryContext =
                    $"{_persona.PromptSummary()}{Environment.NewLine}{memoryContext.TrimStart()}";
                var albumMemory = await BuildAlbumConversationMemoryAsync(
                    input,
                    includeLlmPayload: true,
                    cancellationToken: _lifetimeCancellation.Token);
                if (!string.IsNullOrWhiteSpace(albumMemory.Context))
                    memoryContext = $"{memoryContext.TrimEnd()}{Environment.NewLine}{albumMemory.Context}";
                var history = await _conversationSession.LoadAsync(
                    _modelApiSettings.ConversationTurns,
                    _lifetimeCancellation.Token);
                reply = await _modelApi.SendAsync(
                    _modelApiSettings,
                    _memory.Personality,
                    _memory.Profile.SelfIdentity,
                    memoryContext,
                    input,
                    history,
                    albumMemory.Images,
                    _lifetimeCancellation.Token);
                LastExperienceRuleUsed = false;
                OnPropertyChanged(nameof(ExperienceDebugStatus));
                ModelApiStatus = albumMemory.Images.Count > 0
                    ? $"最近一次模型回复注入 {albumMemory.InjectedExperienceCount} 条经历摘要，并读取了 {albumMemory.Images.Count} 张主人授权的相册图片。"
                    : albumMemory.InjectedExperienceCount > 0
                        ? $"最近一次模型回复注入 {albumMemory.InjectedExperienceCount} 条主人授权的经历摘要。"
                        : "最近一次模型回复已通过角色边界检查。";
            }
            else
            {
                SetBehavior("conversation", "听主人说话并按照当前性格回应", "conversation");
                PlaySequence(ExpressionSequence);
                var localAgent = _agentKernel.Handle(
                    new PetAgentEvent
                    {
                        Kind = PetAgentEventKind.UserChat,
                        At = _clock.Now,
                        Text = input
                    },
                    new PetAgentContext
                    {
                        CurrentStateSummary = RuntimeStateSummary,
                        Temperament = _memory.Personality.Temperament.Clone(),
                        RelationshipSummary = RelationshipStateSummary,
                        RecentConversation = ChatMessages
                            .TakeLast(4)
                            .Select(message => message.Text)
                            .ToList(),
                        CurrentBehaviorId = _currentBehaviorKey,
                        ArbitrationSummary = LastArbitrationResult
                    });
                var albumMemory = await BuildAlbumConversationMemoryAsync(
                    input,
                    includeLlmPayload: false,
                    cancellationToken: _lifetimeCancellation.Token);
                var ruleRecord = albumMemory.Matches
                    .Select(x => x.Record)
                    .FirstOrDefault(x =>
                        x.AllowRules &&
                        x.IncludeInConversation &&
                        _experienceSettings.AllowConversation &&
                        _experienceSettings.AllowRuleMode);
                if (ruleRecord is not null ||
                    (AlbumExperienceService.LooksLikeExperienceQuery(input) &&
                     _experienceSettings.AllowConversation &&
                     _experienceSettings.AllowRuleMode))
                {
                    reply = AlbumExperienceService.ComposeRuleReply(ruleRecord);
                    LastExperienceRuleUsed = true;
                    LastExperienceLlmCount = 0;
                    LastExperienceImageCount = 0;
                    await TryApplyExperienceBehaviorSuggestionAsync(ruleRecord);
                    ModelApiStatus = ruleRecord is null
                        ? "模型对话未启用；本地经历检索没有命中。"
                        : "模型对话未启用；本次使用本地经历摘要和规则模板。";
                }
                else
                {
                    // Preserve the existing default Pupu phrasing while still
                    // running the same Persona-backed local PetAgent pipeline.
                    CurrentIntent = string.Join(" · ", localAgent.Debug);
                    reply = ComposePetSpeech(PetSpeechIntent.Conversation);
                    LastExperienceRuleUsed = false;
                    ModelApiStatus = "模型对话未启用，本次使用本地性格台词。";
                }
                OnPropertyChanged(nameof(ExperienceDebugStatus));
            }
            ChatMessages.Add(new ChatMessage { Text = reply });
            await PersistConversationExchangeAsync(input, reply);
            _ = ShowBubbleAsync(reply, 6500, PetSpeechIntent.Conversation);
            await _memory.RecordAsync(
                "conversation",
                $"主人和朴朴聊了“{TrimForMemory(input)}”；朴朴按当前性格作出回应。",
                "conversation",
                0.62,
                0.42,
                true,
                "conversation",
                $"time={TimeBucket(_clock.Now)}",
                "local:pet-speech");
        }
        catch (Exception ex)
        {
            ModelApiStatus = $"对话失败：{ex.Message}";
            var fallback = ComposePetSpeech(PetSpeechIntent.RecoverableProblem);
            ChatMessages.Add(new ChatMessage { Text = fallback });
            await PersistConversationExchangeAsync(input, fallback);
            _ = ShowBubbleAsync(fallback, 4200, PetSpeechIntent.RecoverableProblem);
        }
        finally
        {
            IsChatBusy = false;
            IsChatComposerVisible = false;
            if (!IsCaged &&
                !IsTraveling &&
                MouseInteractionMode is MouseInteractionMode.Attention)
                SetIdleAnimation();
            RefreshAll();
        }
    }

    private async Task CorrectBehaviorAsync(int feedback)
    {
        var correction = await _memory.CorrectAsync(
            _currentBehaviorKey,
            feedback,
            CorrectionNote,
            _currentInteractionType,
            _currentBehaviorContext,
            _currentAnimationSource);
        CorrectionNote = string.Empty;
        _ = ShowBubbleAsync(feedback > 0 ? "记住了，这样才像真正的pupu。" : "好吧，我会收一点……但猫也有自己的主意。", 4300);
        MemoryStatus = $"已记录纠正：{correction.Note}";
        RefreshAll();
        await RefreshEditableNotebookAsync();
    }

    private async Task UndoCorrectionAsync()
    {
        var undone = await _memory.UndoLastCorrectionAsync();
        _ = ShowBubbleAsync(undone ? "刚才那条纠正撤回啦。" : "现在没有可撤回的纠正。", 3500);
        RefreshAll();
        await RefreshEditableNotebookAsync();
    }

    private async Task SavePersonalityAsync()
    {
        await _memory.SaveBaselineAsync(_editableTraits);
        _ = ShowBubbleAsync("底色记住了。我还是pupu，只是更像你认识的那一只。", 4500);
        RefreshAll();
        await RefreshEditableNotebookAsync();
    }

    private async Task SavePetProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(_editableProfile.SystemPrompt))
        {
            _editableProfile.SystemPrompt = PetProfile.DefaultSystemPrompt;
            OnPropertyChanged(nameof(OwnerPersonalityPrompt));
        }
        _editableProfile.Normalize();
        await _memory.SaveProfileAsync(_editableProfile);
        _editableProfile = _memory.Profile.Clone();
        _persona = _memory.Profile.Persona;
        _agentKernel.ReplaceAgent(new RulePetAgent(_persona));
        OnPropertyChanged(nameof(CurrentPersonaSummary));
        OnPropertyChanged(nameof(CurrentPromptPreview));
        OnPropertyChanged(nameof(CurrentPromptTokenEstimate));
        RefreshEditableProfile();
        await RefreshEditableNotebookAsync();
        RefreshAll();
        _ = ShowBubbleAsync(
            $"档案记好啦。{_memory.Profile.ChineseName}还是主人的{_memory.Profile.RelationshipToOwner}。",
            4600,
            PetSpeechIntent.Remembered);
        _ = TryRunCalendarSpecialAsync();
    }

    private async Task ResetLearningAsync()
    {
        var confirmed = _presentationHost.Confirm(
            "重置习惯与偏好",
            "这会清除跨天形成的习惯与互动证据，并重新应用仍有效的主人纠正；天生性格不会改变。继续吗？");
        if (!confirmed) return;
        await _memory.ResetLearningAsync();
        _ = ShowBubbleAsync("好，过去学来的小习惯先忘掉。朴朴还是朴朴。", 4100);
        RefreshAll();
        await RefreshEditableNotebookAsync();
    }

    private async Task ChangeScaleAsync(double delta)
    {
        _memory.State.PetScale = Math.Round(Math.Clamp(_memory.State.PetScale + delta, 0.55, 1.8), 2);
        await _memory.SaveStateAsync();
        OnPropertyChanged(nameof(PetDisplaySize));
        OnPropertyChanged(nameof(PetScaleLabel));
    }

    private async Task ResetScaleAsync()
    {
        _memory.State.PetScale = 1;
        await _memory.SaveStateAsync();
        OnPropertyChanged(nameof(PetDisplaySize));
        OnPropertyChanged(nameof(PetScaleLabel));
    }

    private async Task UpdateNeedsAsync()
    {
        if (!IsReady || _disposed) return;
        var now = _clock.Now;
        var elapsed = now - _lastActiveStateTickAt;
        _lastActiveStateTickAt = now;
        _memory.AdvanceActiveRuntime(
            elapsed,
            now.Hour >= 23 || now.Hour < 7);
        PrepareDailyToiletPlan(now, skipPastPending: false);
        if (now - _memory.Personality.LastOvertouchAt > TimeSpan.FromMinutes(10))
            _memory.Personality.RecentOvertouchCount =
                Math.Max(0, _memory.Personality.RecentOvertouchCount - 1);
        await _memory.SaveStateAsync();
        RefreshAll();
        _ = TryRunCalendarSpecialAsync();
    }

    private async Task<bool> TryRunCalendarSpecialAsync()
    {
        if (!IsReady ||
            _disposed ||
            _busyAction ||
            _calendarSpecialRunning ||
            IsCaged ||
            IsTraveling)
            return false;
        var now = _clock.Now;
        _calendarSpecialRunning = true;
        try
        {
            if (DailySpecialRules.IsOwnerBirthday(now, _memory.Profile.OwnerBirthday) &&
                !DailySpecialRules.WasTriggeredToday(_memory.State.LastBirthdayGreetingAt, now))
            {
                _memory.State.LastBirthdayGreetingAt = now;
                await _memory.SaveStateAsync();
                var age = DailySpecialRules.OwnerAgeOnBirthday(now, _memory.Profile.OwnerBirthday);
                var ageText = age is { } years
                    ? $"今天是你{years}岁生日"
                    : "今天是你的生日";
                await RunSeasonalActionAsync(
                    SeasonalOccasion.OwnerBirthday,
                    BirthdaySequence,
                    $"{ageText}！{_memory.Profile.ChineseName}把今天最软的一声呼噜送给你。");
                return true;
            }

            var occasion = DailySpecialRules.HolidayFor(now);
            if (occasion == SeasonalOccasion.None ||
                DailySpecialRules.WasTriggeredToday(_memory.State.LastSeasonalOutfitAt, now))
                return false;
            _memory.State.LastSeasonalOutfitAt = now;
            await _memory.SaveStateAsync();
            var (sequence, bubble) = occasion switch
            {
                SeasonalOccasion.Christmas => (
                    ChristmasSequence,
                    "今天是圣诞节。帽子只戴今天，尾巴还是照常摆。"),
                SeasonalOccasion.Halloween => (
                    HalloweenSequence,
                    "今天是万圣节。斗篷可以神秘，猫不负责吓人。"),
                SeasonalOccasion.SpringFestival => (
                    SpringFestivalSequence,
                    "今天过春节。红围巾戴好啦，愿主人新年平安。"),
                _ => (ProneIdleSequence, string.Empty)
            };
            await RunSeasonalActionAsync(occasion, sequence, bubble);
            return true;
        }
        finally
        {
            _calendarSpecialRunning = false;
        }
    }

    private async Task RunSeasonalActionAsync(
        SeasonalOccasion occasion,
        AnimationSequence sequence,
        string bubble)
    {
        var behaviorId = occasion switch
        {
            SeasonalOccasion.Christmas => "seasonal.christmas",
            SeasonalOccasion.Halloween => "seasonal.halloween",
            SeasonalOccasion.SpringFestival => "seasonal.spring_festival",
            SeasonalOccasion.OwnerBirthday => "seasonal.owner_birthday",
            _ => "seasonal.none"
        };
        if (!TryAcceptBehaviorRequest(
                behaviorId,
                BehaviorArbitrationSource.Autonomous,
                BehaviorPriority.AutonomousMovement,
                TimeSpan.FromSeconds(12),
                TimeSpan.FromHours(12),
                interruptible: true,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified,
                showRejectedBubble: false))
            return;
        var token = BeginAction(
            behaviorId,
            "仅在对应日期出现的节日装扮或生日祝福",
            sequence);
        try
        {
            _ = ShowBubbleAsync(bubble, 6800, PetSpeechIntent.General);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(12), token)) return;
            await _memory.RecordAsync(
                "calendar_special",
                $"{behaviorId} 只在本地日期满足条件时触发。",
                behaviorId,
                0.72,
                0.58,
                false,
                "calendar",
                $"date={_clock.Now:yyyy-MM-dd}",
                $"{sequence.Atlas}:{sequence.Name}");
        }
        finally
        {
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    private bool PrepareDailyToiletPlan(
        DateTimeOffset now,
        bool skipPastPending)
    {
        var preparation = _dailyToiletPlanner.EnsurePlan(
            _memory.State.DailyToiletPlan,
            now,
            _dailyToiletRandom);
        _memory.State.DailyToiletPlan = preparation.Plan;
        var changed = preparation.Rebuilt;
        if (!preparation.Rebuilt && skipPastPending)
            changed |= _dailyToiletPlanner.SkipPastPending(preparation.Plan, now);
        else
            changed |= _dailyToiletPlanner.ExpireMissed(preparation.Plan, now);
        return changed;
    }

    private async Task RunAutonomousToiletAsync(bool alreadyAdmitted = false)
    {
        var now = _clock.Now;
        if (!alreadyAdmitted &&
            !TryAcceptBehaviorRequest(
                "routine.toilet",
                BehaviorArbitrationSource.ContinuousEffect,
                BehaviorPriority.ContinuousEffect,
                TimeSpan.FromSeconds(12),
                TimeSpan.FromMinutes(20),
                interruptible: false,
                BehaviorStateBlockers.Caged |
                BehaviorStateBlockers.Traveling |
                BehaviorStateBlockers.Petrified |
                BehaviorStateBlockers.Magic |
                BehaviorStateBlockers.Movement |
                BehaviorStateBlockers.TouchReaction |
                BehaviorStateBlockers.Feeding |
                BehaviorStateBlockers.Playing))
            return;
        if (!_dailyToiletPlanner.TryReserveDueSlot(
                _memory.State.DailyToiletPlan,
                now,
                out var slotId))
        {
            SetIdleAnimation();
            return;
        }

        // Reserve before the first frame so a crash cannot replay the same
        // physiological event after restart.
        await _memory.SaveStateAsync();
        const string behaviorId = "routine.toilet";
        var token = BeginAction(
            behaviorId,
            "自发去猫砂盆，如厕后认真抓挠并埋好",
            ToiletEnterSequence);
        var context = $"source=autonomous;time={TimeBucket(now)};slot={slotId}";
        var session = await _interactionLifecycle.StartAsync(
            behaviorId,
            "autonomous_toilet",
            context,
            "litter:toilet-enter");
        _activeInteraction = session;
        var reliefCommitted = false;
        var buryCompleted = false;
        var fullyCompleted = false;
        try
        {
            _ = ShowBubbleAsync("我去忙一下。等埋好了再出来。", 3600);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(2.2), token)) return;

            PlaySequence(ToiletRelieveSequence);
            if (_scheduledAction is not null && _scheduledAction.Token == token)
                _actionScheduler.EnterLoop(_scheduledAction);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(3.4), token)) return;
            reliefCommitted = true;
            await _interactionLifecycle.ProgressAsync(session, 0.52);

            // Looking up is a characterful variation, not a compulsory toilet
            // pose. Most runs stay focused on the litter.
            if (_random.NextDouble() < 0.28)
            {
                PlaySequence(ToiletLookUpSequence);
                if (!await WaitPhaseAsync(TimeSpan.FromSeconds(1.35), token)) return;
            }

            PlaySequence(ToiletBurySequence);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(4.8), token)) return;
            buryCompleted = true;
            await _interactionLifecycle.ProgressAsync(session, 0.86);

            PlaySequence(ToiletExitSequence);
            if (!await WaitPhaseAsync(TimeSpan.FromSeconds(2.1), token)) return;
            _dailyToiletPlanner.TryCompleteSlot(
                _memory.State.DailyToiletPlan,
                slotId,
                _clock.Now);
            await _memory.SaveStateAsync();
            await _interactionLifecycle.CompleteAsync(session);
            fullyCompleted = true;
            await _memory.RecordAsync(
                "autonomous",
                "pupu自发完成如厕；抬头只是概率变化，如厕后固定认真抓挠并埋好。",
                behaviorId,
                0.42,
                0.18,
                false,
                "autonomous_toilet",
                context,
                "litter:toilet-chain",
                session.Id);
        }
        catch (Exception ex)
        {
            await _interactionLifecycle.FailAsync(session, ex);
            throw;
        }
        finally
        {
            // Once relief has happened, interruption still receives a short,
            // coherent burying close instead of cutting straight to idle.
            if (reliefCommitted &&
                !buryCompleted &&
                !_disposed &&
                _scheduledAction is not null &&
                _scheduledAction.Token == token)
            {
                _busyAction = true;
                PlaySequence(ToiletBurySequence);
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(1.4),
                        _lifetimeCancellation.Token);
                    buryCompleted = true;
                }
                catch (OperationCanceledException) { }
            }

            if (!fullyCompleted && reliefCommitted && buryCompleted)
            {
                _dailyToiletPlanner.TryCompleteSlot(
                    _memory.State.DailyToiletPlan,
                    slotId,
                    _clock.Now);
                await _memory.SaveStateAsync();
            }
            if (!session.IsTerminal)
                await _interactionLifecycle.InterruptAsync(session, StopReasonFor(token));
            if (ReferenceEquals(_activeInteraction, session)) _activeInteraction = null;
            SetBehavior(
                "idle.prone_observe",
                "埋好后回到原地低趴",
                "autonomous",
                $"time={TimeBucket(_clock.Now)};location=desktop",
                "routines:prone-idle");
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    private async Task RunScoredAutonomousMagicAsync(
        string behaviorId,
        DateTimeOffset now,
        bool alreadyAdmitted = false)
    {
        if (!DailySpecialRules.CanTriggerAutonomousMagic(
                _memory.State.LastAutonomousMagicAt,
                now))
            return;

        // Persist before starting so a crash or interruption cannot produce a
        // second self-triggered spell on the same local calendar date.
        _memory.State.LastAutonomousMagicAt = now;
        await _memory.SaveStateAsync();
        switch (behaviorId)
        {
            case "magic.accio_broom":
                await AccioBroomAsync(true, alreadyAdmitted);
                break;
            case "magic.apparate":
                await ApparateAsync(true, alreadyAdmitted);
                break;
            case "magic.petrificus_totalus":
                await PetrificusTotalusAsync(true, alreadyAdmitted);
                break;
            case "magic.scourgify":
                await ScourgifyAsync(true, alreadyAdmitted);
                break;
        }
    }

    private async Task RunAutonomyAsync()
    {
        if (!IsReady || _disposed) return;
        if (IsTraveling)
        {
            if (_memory.State.Travel.ReturnsAt is { } returnsAt &&
                _clock.Now >= returnsAt)
                await ReturnFromTravelAsync(recalled: false);
            else
                OnPropertyChanged(nameof(TravelStatus));
            return;
        }
        if (IsCaged) return;
        if (_isPetrified)
        {
            if (_clock.Now >= _nextAutomaticCoinRefreshAt)
                RefreshPetrifiedCoinColor();
            OnPropertyChanged(nameof(CoinColorFreshness));
            if (!_isCoinBackVisible && CoinColorFreshness <= 0.05 &&
                !_currentSequence.Name.Equals("coin-normalFaded", StringComparison.Ordinal) &&
                !_currentSequence.Name.Equals("coin-unhappyFaded", StringComparison.Ordinal))
                PlaySequence(ResolveCurrentCoinFrontSequence());
            return;
        }
        if (IsChatBusy || _busyAction) return;
        if (await ProcessPendingBehaviorProposalAsync()) return;
        if (_busyAction) return;
        var now = _clock.Now;
        if (now < _nextAutonomousActionAt) return;
        if (PrepareDailyToiletPlan(now, skipPastPending: false))
            await _memory.SaveStateAsync();
        if (_lastInitiativeWasIgnored && now >= _initiativeCooldownUntil)
        {
            _lastInitiativeWasIgnored = false;
            _interactionSessions.EndActive("unanswered_natural_end");
        }

        var policy = _memory.BehaviorPolicy;
        var quietCommandActive =
            _memory.State.QuietModeUntil is { } quietUntil &&
            quietUntil > now;
        var selfPlayInvited =
            _memory.State.SelfPlayAllowedUntil is { } selfPlayUntil &&
            selfPlayUntil > now;
        var context = new BehaviorContext
        {
            Now = now,
            RequestSource = BehaviorRequestSource.Autonomous,
            CurrentBehaviorId = _currentBehaviorKey,
            CurrentBehaviorStartedAt = _currentBehaviorStartedAt,
            CurrentBehaviorInterruptible =
                _behaviorArbitrator.CurrentLease?.Interruptible ?? !_busyAction,
            IsDeepNight = now.Hour >= 23 || now.Hour < 7,
            DoNotDisturb = policy.DoNotDisturb || quietCommandActive,
            MeetingMode = policy.MeetingMode,
            FullScreen = policy.SuppressHighDisruptionInFullScreen &&
                         _desktopEnvironmentProbe.IsForegroundApplicationFullScreen(),
            EnvironmentAllowsMovement = true,
            UserRespondedToLastInitiative = !_lastInitiativeWasIgnored,
            AllowOwnerInitiative = policy.AllowOwnerInitiative,
            InitiativeCooldownActive = now < _initiativeCooldownUntil,
            MinimumAutonomousDwell = TimeSpan.FromSeconds(policy.MinimumIdleActionSeconds),
            ContextKey = $"time={TimeBucket(now)};location=desktop",
            LocationKey = "desktop",
            TimeBucket = TimeBucket(now),
            Signals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["mouse_nearby"] = _perception.Signal("mouse_nearby", now),
                ["self_play_invited"] = selfPlayInvited ? 1 : 0,
                ["daytime"] = now.Hour is >= 7 and < 19 ? 1 : 0,
                ["deep_night"] = now.Hour >= 23 || now.Hour < 7 ? 1 : 0,
                ["daily_magic_available"] =
                    DailySpecialRules.CanTriggerAutonomousMagic(
                        _memory.State.LastAutonomousMagicAt,
                        now)
                        ? 1
                        : 0,
                ["toilet_due"] =
                    _dailyToiletPlanner.IsDue(_memory.State.DailyToiletPlan, now)
                        ? 1
                        : 0
            }
        };
        var definitions = BehaviorCatalog.Autonomous.Where(definition =>
            (policy.AllowAutonomousMovement || !definition.RequiresMovement) &&
            (!quietCommandActive ||
             (!definition.RequiresMovement &&
              !definition.IsHighDisruption &&
              !definition.IsOwnerInitiative)) &&
            (policy.AllowLowDisruptionMischief || !definition.BehaviorId.StartsWith("mischief.", StringComparison.Ordinal)) &&
            (policy.AllowOwnerInitiative || !definition.IsOwnerInitiative));
        var decision = _agentKernel.Decide(
            definitions,
            context,
            BuildArbitrationContext(),
            new BehaviorSelectionOptions
            {
                Source = BehaviorArbitrationSource.Autonomous,
                ActivePriority = BehaviorPriority.AutonomousMovement,
                PassivePriority = BehaviorPriority.DecorativeIdle,
                CommitAdmission = true,
                ForbiddenStates =
                    BehaviorStateBlockers.Caged |
                    BehaviorStateBlockers.Traveling |
                    BehaviorStateBlockers.Petrified
            });
        await LogDecisionAsync(decision);
        if (decision.Deferred)
        {
            _nextAutonomousActionAt = now.AddSeconds(
                Math.Clamp(policy.AutonomousDecisionSeconds / 3.0, 8, 30));
            return;
        }
        var definition = BehaviorCatalog.Find(decision.SelectedBehaviorId)!;
        _nextAutonomousActionAt = now.AddSeconds(
            Math.Max(
                quietCommandActive ? policy.AutonomousDecisionSeconds * 1.5 : policy.AutonomousDecisionSeconds,
                Math.Min(90, definition.MinimumDwell.TotalSeconds / 3)));
        await ExecuteAutonomousDecisionAsync(decision.SelectedBehaviorId, definition);
    }

    private static IBehaviorPresentationResolver<DesktopBehaviorPresentation>
        BuildPresentationResolver()
    {
        var map = new Dictionary<string, DesktopBehaviorPresentation>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["idle.side_lie"] = new(SideLieIdleSequence, "侧躺慢呼吸"),
            ["idle.prone_observe"] = new(ProneIdleSequence, "低趴观察周围"),
            ["idle.sploot"] = new(SplootSequence, "板鸭趴着，把后腿舒舒服服伸开"),
            ["self.groom"] = new(
                FurGroomSequence,
                "低频舔毛并依次梳理前爪、胸口、侧腹和大尾巴"),
            ["self.paw_nibble"] = new(
                PawNibbleSequence,
                "偶尔抱住后脚舔毛并轻轻啃爪子"),
            ["rest.far"] = new(RearIdleSequence, "在稍远的位置背向休息"),
            ["rest.near_owner"] = new(FocusSequence, "选择在主人附近安静休息"),
            ["rest.bed"] = new(BlueBedSleepSequence, "在蓝色长方形垫子里安静睡觉"),
            ["rest.sleep"] = new(SleepSequence, "疲劳或深夜时原地睡觉"),
            ["rest.sleep.curled"] = new(
                SleepCurledSequence,
                "白天安静团成一团睡觉"),
            ["rest.sleep.belly_up"] = new(
                SleepBellyUpSequence,
                "安全感充足时四仰八叉睡觉"),
            ["rest.sleep.side"] = new(
                SleepSideSequence,
                "白天侧身伸展着睡觉"),
            ["play.roll"] = new(RollSequence, "精力充足时自主打滚玩耍"),
            ["play.tail_chase"] = new(SpinSequence, "追着大尾巴转圈玩耍"),
            ["play.pounce"] = new(WandLoopSequence, "突然伏低并小扑"),
            ["play.accept_toy"] = new(WandLoopSequence, "看到可用玩具后接受玩耍"),
            ["explore.short_walk"] = new(RunLeftSequence, "好奇地进行短距离探索"),
            ["explore.mouse_track"] = new(
                CuriousTouchSequence,
                "追踪附近鼠标并歪头观察"),
            ["social.approach"] = new(AttentionSequence, "主动靠近主人片刻"),
            ["social.purr"] = new(PurrSequence, "在主人附近自主呼噜"),
            ["social.knead"] = new(PurrSequence, "安心地踩奶片刻"),
            ["social.respond_call"] = new(
                CuriousTouchSequence,
                "听到呼唤后回应主人",
                "我听见了。"),
            ["social.ask_attention"] = new(
                AttentionSequence,
                "短暂求关注，未响应就自动结束"),
            ["social.ask_play"] = new(
                AttentionSequence,
                "短暂提示想玩，未响应就自然结束"),
            ["vigilance.observe"] = new(ProneIdleSequence, "敏感地观察环境变化"),
            ["vigilance.guard"] = new(AnnoyedTouchSequence, "压力升高时压耳警戒"),
            ["avoid.quiet_place"] = new(RearIdleSequence, "压力较高时寻找安静位置"),
            ["mischief.bat_object"] = new(
                MischiefSequence,
                "状态良好时自然拨弄物品",
                "这只是低干扰的小实验。"),
            ["mischief.hide"] = new(RearIdleSequence, "淘气地藏到远一点的位置"),
            ["mischief.detour"] = new(RunLeftSequence, "淘气地绕行一小段"),
            ["independent.patrol"] = new(RunLeftSequence, "独自在桌面短距离巡视")
        };
        return new DictionaryBehaviorPresentationResolver<DesktopBehaviorPresentation>(
            "sprite-atlas-v17",
            map,
            new DesktopBehaviorPresentation(ProneIdleSequence, "安静陪伴"));
    }

    private async Task ExecuteAutonomousDecisionAsync(
        string behaviorId,
        BehaviorDefinition definition)
    {
        if (behaviorId == "routine.toilet")
        {
            await RunAutonomousToiletAsync(alreadyAdmitted: true);
            return;
        }

        if (behaviorId.StartsWith("magic.", StringComparison.Ordinal))
        {
            await RunScoredAutonomousMagicAsync(
                behaviorId,
                _clock.Now,
                alreadyAdmitted: true);
            return;
        }

        var intent = new BehaviorPresentationIntent
        {
            BehaviorId = behaviorId,
            Phase = BehaviorPresentationPhase.Enter,
            Motion = definition.RequiresMovement
                ? BehaviorMotionKind.Locomotion
                : BehaviorMotionKind.Stationary,
            Direction = _currentDirection.ToString().ToLowerInvariant(),
            Loop = !definition.IsHighDisruption
        };
        _presentationResolver.TryResolve(intent, out var resolution);
        var presentation = resolution?.Presentation ??
            new DesktopBehaviorPresentation(ProneIdleSequence, "安静陪伴");
        var sequence = presentation.Sequence;
        var label = presentation.Label;
        var bubble = behaviorId switch
        {
            "social.ask_attention" =>
                ComposePetSpeech(PetSpeechIntent.InitiativeAttention),
            "social.ask_play" =>
                ComposePetSpeech(PetSpeechIntent.InitiativePlay),
            _ => presentation.Bubble
        };

        if (definition.RequiresMovement &&
            behaviorId is "explore.short_walk" or "independent.patrol" or "mischief.detour" or "avoid.quiet_place")
        {
            await RunScoredAutonomousMovementAsync(behaviorId, label);
            return;
        }

        var switchingSleepPose =
            _currentBehaviorKey.StartsWith("rest.sleep", StringComparison.Ordinal) &&
            behaviorId.StartsWith("rest.sleep", StringComparison.Ordinal) &&
            _currentSequence.Name != sequence.Name;
        if (switchingSleepPose)
        {
            PlaySequence(SleepTransitionSequence);
            await Task.Delay(1800);
        }

        SetBehavior(
            behaviorId,
            label,
            "autonomous",
            $"time={TimeBucket(_clock.Now)};location=desktop",
            $"{sequence.Atlas}:{sequence.Name}");
        PlaySequence(sequence, restart: false);
        ApplyAutonomousRuntimeEffect(behaviorId);

        if (definition.IsOwnerInitiative)
        {
            if (_clock.Now < _initiativeCooldownUntil || _lastInitiativeWasIgnored) return;
            _lastInitiativeWasIgnored = true;
            _initiativeCooldownUntil = _clock.Now.AddMinutes(_memory.BehaviorPolicy.InitiativeCooldownMinutes);
            _interactionSessions.StartInitiative(
                behaviorId,
                $"time={TimeBucket(_clock.Now)};location=desktop",
                $"{sequence.Atlas}:{sequence.Name}");
            if (!string.IsNullOrWhiteSpace(bubble)) _ = ShowBubbleAsync(bubble, 4200);
        }
        else if (!string.IsNullOrWhiteSpace(bubble) &&
                 _clock.Now - _lastAutonomousMessageAt > TimeSpan.FromMinutes(4))
        {
            _lastAutonomousMessageAt = _clock.Now;
            _ = ShowBubbleAsync(bubble, 3800);
        }

        await _memory.RecordAsync(
            "autonomous",
            $"统一行为评分选择 {behaviorId}；该行为不由主人缺席或忽略时长触发。",
            behaviorId,
            0.34,
            0.10,
            false,
            "autonomous",
            $"time={TimeBucket(_clock.Now)};location=desktop",
            $"{sequence.Atlas}:{sequence.Name}");
        RefreshAll();
    }

    private async Task RunScoredAutonomousMovementAsync(string behaviorId, string label)
    {
        var token = BeginAction(behaviorId, label, RunLeftSequence);
        var completed = false;
        try
        {
            var move = new DesktopMoveRequestEventArgs(
                DesktopMoveMode.AttentionRoam,
                TimeSpan.FromSeconds(_memory.BehaviorPolicy.AutonomousRoamSeconds),
                token);
            DesktopMoveRequested?.Invoke(this, move);
            if (DesktopMoveRequested is null) move.Completion.TrySetResult(false);
            try { completed = await move.Completion.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
            if (!completed) return;
            ApplyAutonomousRuntimeEffect(behaviorId);
            await _memory.RecordAsync(
                "autonomous",
                $"统一行为评分选择 {behaviorId} 并完成短距离移动。",
                behaviorId,
                0.42,
                0.12,
                false,
                "autonomous",
                $"time={TimeBucket(_clock.Now)};location=desktop",
                "directions:run");
        }
        finally
        {
            EndAction(ProneIdleSequence, expectedToken: token);
        }
    }

    private void ApplyAutonomousRuntimeEffect(string behaviorId)
    {
        var runtime = _memory.Personality.Runtime;
        if (behaviorId.StartsWith("play.", StringComparison.Ordinal))
        {
            runtime.PlayDesire -= 0.035;
            runtime.Fatigue += 0.025;
            runtime.Arousal += 0.025;
        }
        else if (behaviorId.StartsWith("social.", StringComparison.Ordinal))
        {
            runtime.SocialDesire -= 0.025;
            runtime.Safety += 0.012;
        }
        else if (behaviorId.StartsWith("rest.sleep", StringComparison.Ordinal) ||
                 behaviorId == "rest.bed")
        {
            runtime.Fatigue -= 0.035;
            runtime.Arousal -= 0.025;
            runtime.Stress -= 0.018;
        }
        else if (behaviorId is "self.groom" or "self.paw_nibble")
        {
            runtime.Stress -= 0.012;
            runtime.Arousal -= 0.008;
        }
        else if (behaviorId is "avoid.quiet_place" or "rest.far")
        {
            runtime.Stress -= 0.020;
            runtime.Safety += 0.018;
        }
        else if (behaviorId == "idle.sploot")
        {
            runtime.Stress -= 0.014;
            runtime.Safety += 0.012;
            runtime.Fatigue -= 0.008;
        }
        runtime.Clamp();
    }

    private CancellationToken BeginAction(string key, string label, AnimationSequence sequence)
    {
        EndCursorGaze();
        _touchReactionCancellation?.Cancel();
        AreQuickActionsVisible = false;
        _actionScheduler.Stop("superseded");
        _scheduledAction = _actionScheduler.Start(key);
        _busyAction = true;
        SetBehavior(key, label, "long_action", "desktop", $"{sequence.Atlas}:{sequence.Name}");
        PlaySequence(sequence);
        OnPropertyChanged(nameof(IsLongActionRunning));
        RaiseCommands();
        return _scheduledAction.Token;
    }

    private void EndAction(
        AnimationSequence settle,
        bool restart = true,
        CancellationToken? expectedToken = null)
    {
        if (expectedToken is { } expected &&
            (_scheduledAction is null || _scheduledAction.Token != expected))
            return;
        if (_scheduledAction is not null)
        {
            _actionScheduler.BeginExit(_scheduledAction);
            _actionScheduler.Complete(_scheduledAction);
            _scheduledAction = null;
        }
        _busyAction = false;
        if (IsTraveling)
        {
            SetBehavior(
                "travel.away",
                $"外出旅行：{_memory.State.Travel.Destination}",
                "travel",
                "away",
                "local:travel");
        }
        else if (IsCaged)
        {
            SetBehavior(
                "owner.cage",
                "关笼子／原地锁定，等待主人释放",
                "owner_forced",
                "desktop",
                "Actions:pupu-cage-rest-youthful-v14.png");
            PlaySequence(CageRestSequence, restart: false);
        }
        else
        {
            ResetArbitrationToIdle();
            PlaySequence(settle, restart);
            ScheduleNextAutonomousAction();
        }
        OnPropertyChanged(nameof(IsLongActionRunning));
        RefreshAll();
    }

    private void StopCurrentAction()
    {
        if (IsTraveling)
        {
            _ = ShowBubbleAsync("我还在外出中。要让我回来，请用“召回”。", 3600);
            return;
        }
        if (IsCaged)
        {
            _ = ShowBubbleAsync("笼子还锁着。要恢复普通行为，请先释放我。", 3600);
            return;
        }
        if (_isPetrified)
        {
            _ = ReleasePetrificationAsync();
            return;
        }
        if (!TryAcceptBehaviorRequest(
                "owner.stop",
                BehaviorArbitrationSource.OwnerForced,
                BehaviorPriority.OwnerForced,
                TimeSpan.Zero,
                TimeSpan.Zero,
                interruptible: true,
                BehaviorStateBlockers.None,
                forceInterrupt: true))
            return;
        _actionScheduler.Stop("user_stop");
        _touchReactionCancellation?.Cancel();
        _isTouchEscaping = false;
        _busyAction = false;
        ResetArbitrationToIdle();
        SetIdleAnimation();
        OnPropertyChanged(nameof(IsLongActionRunning));
        _ = ShowBubbleAsync(null, 3000, PetSpeechIntent.Stop);
        RaiseCommands();
    }

    private static async Task<bool> WaitPhaseAsync(TimeSpan duration, CancellationToken token)
    {
        if (duration <= TimeSpan.Zero) return !token.IsCancellationRequested;
        try
        {
            await Task.Delay(duration, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> RunProgressiveInteractionAsync(
        InteractionSession session,
        TimeSpan total,
        TimeSpan startDelay,
        int steps,
        CancellationToken token,
        Func<double, IReadOnlyList<AppliedEffect>> applyStep)
    {
        steps = Math.Clamp(steps, 1, 20);
        if (!await WaitPhaseAsync(
                TimeSpan.FromMilliseconds(Math.Min(total.TotalMilliseconds, startDelay.TotalMilliseconds)),
                token))
            return false;
        if (_scheduledAction is not null && _scheduledAction.Token == token)
            _actionScheduler.EnterLoop(_scheduledAction);

        var remaining = total - startDelay;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var interval = TimeSpan.FromMilliseconds(
            remaining.TotalMilliseconds / Math.Max(1, steps));
        var fraction = 1d / steps;
        for (var index = 0; index < steps; index++)
        {
            if (!await WaitPhaseAsync(interval, token)) return false;
            if (token.IsCancellationRequested) return false;
            var effects = applyStep(fraction);
            _memory.Personality.Runtime.Clamp();
            await _memory.SaveStateAsync();
            await _interactionLifecycle.ProgressAsync(
                session,
                Math.Min(0.99, (index + 1d) / steps),
                effects);
            RefreshAll();
        }
        return !token.IsCancellationRequested;
    }

    private string StopReasonFor(CancellationToken token)
    {
        if (!token.IsCancellationRequested) return "interrupted";
        return _scheduledAction is not null && _scheduledAction.Token == token
            ? _scheduledAction.StopReason
            : "cancelled";
    }

    private static string BuildGestureContext(GestureEvent gesture) =>
        $"gesture={gesture.Kind.ToString().ToLowerInvariant()};" +
        $"intent={gesture.Intent.ToString().ToLowerInvariant()};region={gesture.InteractionRegion};" +
        $"location=x{Math.Clamp((int)(gesture.X / 64), 0, 3)}-y{Math.Clamp((int)(gesture.Y / 64), 0, 3)};" +
        $"frequency={Math.Clamp((int)Math.Round(gesture.ClicksPerSecond), 0, 12)}";

    private static string TimeBucket(DateTimeOffset at) => at.Hour switch
    {
        < 7 => "deep_night",
        < 11 => "morning",
        < 18 => "day",
        < 23 => "evening",
        _ => "deep_night"
    };

    private async Task LogDecisionAsync(BehaviorDecision decision)
    {
        BehaviorScoreItems.Clear();
        BehaviorScoreItems.Add($"选择：{decision.SelectedBehaviorId}");
        foreach (var blocked in decision.Eligibility.Where(x => !x.IsEligible).Take(8))
            BehaviorScoreItems.Add($"过滤：{blocked.BehaviorId} · {string.Join(" / ", blocked.Reasons)}");
        foreach (var item in decision.Candidates.Take(12))
            BehaviorScoreItems.Add(item.Explain());
        try
        {
            await _decisionLogger.AppendAsync(decision);
        }
        catch (Exception ex)
        {
            _presentationHost.ReportRecoverableException(ex, "behavior decision log");
        }
    }

    private static string GalleryBehaviorId(string sequenceName) => sequenceName switch
    {
        "side-lie-idle" => "idle.side_lie",
        "prone-idle" => "idle.prone_observe",
        "paw-nibble" => "self.paw_nibble",
        "rear-idle" => "rest.far",
        "side-rear-transition" => "mischief.hide",
        "roll" => "play.roll",
        "spin" => "play.tail_chase",
        "wand" or "wand-intro" or "wand-loop" => "play.accept_toy",
        "purr" => "social.purr",
        "curious-touch" => "touch.curiosity",
        "gentle-touch" or "happy-petting" or "trust-touch" => "touch.enjoy",
        "annoyed-touch" or "over-petting" => "touch.warning",
        "angry-touch" => "touch.run_away",
        "kibble-slow" => "care.feed_kibble",
        "freeze-dried-pounce" => "care.feed_freeze_dried",
        "canned-pounce" => "care.feed_canned",
        "clean" or "clean-intro" or "clean-loop" => "care.clean_litter",
        var value when value.StartsWith("toilet-", StringComparison.Ordinal) => "routine.toilet",
        "fur-groom-daily" => "self.groom",
        "blue-bed-sleep" => "rest.bed",
        "groom" or "groom-intro" or "groom-loop" => "care.groom",
        "attention" => "social.ask_attention",
        "mischief" => "mischief.bat_object",
        "focus" => "rest.near_owner",
        "sleep-snore" => "rest.sleep",
        "sleep-curled" => "rest.sleep.curled",
        "sleep-belly-up" => "rest.sleep.belly_up",
        "sleep-side" => "rest.sleep.side",
        "sleep-transition" => "rest.sleep.transition",
        "sploot" => "idle.sploot",
        var value when value.StartsWith("cursor-gaze-", StringComparison.Ordinal) => "explore.mouse_track",
        "magic-accio-broom-intro" or "magic-accio-broom-flight" => "magic.accio_broom",
        var value when value.StartsWith("magic-accio-broom-flight-", StringComparison.Ordinal) => "magic.accio_broom",
        "magic-apparate" => "magic.apparate",
        "magic-apparate-reappear" => "magic.apparate",
        "magic-petrificus-totalus" or "magic-petrificus-coin" => "magic.petrificus_totalus",
        "magic-scourgify" => "magic.scourgify",
        "seasonal-christmas" => "seasonal.christmas",
        "seasonal-halloween" => "seasonal.halloween",
        "seasonal-spring-festival" => "seasonal.spring_festival",
        "seasonal-owner-birthday" => "seasonal.owner_birthday",
        "laser-chase-8" => "anchor.toy.approach",
        "snack-chase-8" => "anchor.food.approach",
        "ask-walk" => "social.respond_call",
        var value when value.StartsWith("harness-", StringComparison.Ordinal) => "walk.harnessed",
        var value when value.StartsWith("free-", StringComparison.Ordinal) => "walk.free",
        var value when value.StartsWith("run-", StringComparison.Ordinal) => "explore.short_walk",
        _ => $"animation.{sequenceName}"
    };

    private void SetIdleAnimation()
    {
        if (IsTraveling)
        {
            SetBehavior(
                "travel.away",
                $"外出旅行：{_memory.State.Travel.Destination}",
                "travel",
                "away",
                "local:travel");
            return;
        }
        if (IsCaged)
        {
            SetBehavior(
                "owner.cage",
                "关笼子／原地锁定，等待主人释放",
                "owner_forced",
                "desktop",
                "routines:prone-idle");
            PlaySequence(ProneIdleSequence, restart: false);
            return;
        }
        ResetArbitrationToIdle();
        // Returning from an explicit action uses one neutral settle pose. The
        // The next autonomous choice always comes from the unified BehaviorArbitrator.
        SetBehavior(
            "idle.side_lie",
            "原地侧躺，等待下一次统一行为评分",
            "autonomous",
            $"time={TimeBucket(_clock.Now)};location=desktop",
            "motion:side-lie-idle");
        PlaySequence(SideLieIdleSequence, restart: false);
        ScheduleNextAutonomousAction();
    }

    private void SetBehavior(
        string key,
        string label,
        string interactionType = "autonomous",
        string context = "general",
        string animationSource = "")
    {
        if (!string.Equals(_currentBehaviorKey, key, StringComparison.Ordinal))
            _currentBehaviorStartedAt = _clock.Now;
        _currentBehaviorKey = key;
        _currentInteractionType = interactionType;
        _currentBehaviorContext = context;
        _currentAnimationSource = animationSource;
        CurrentBehaviorLabel = label;
    }

    private void PlaySequence(AnimationSequence sequence, bool restart = true)
    {
        var resolved = ResolveAnimationSequence(sequence);
        if (!restart && _currentSequence.Name == resolved.Name) return;
        _currentSequence = resolved;
        _framePosition = 0;
        if (!_synchronizedMovement)
            _animationTimer.Start();
        RenderNextFrame();
    }

    private AnimationSequence ResolveAnimationSequence(AnimationSequence fallback)
    {
        var resolved = _assetPack.ResolveActionGroup(fallback.Name);
        if (resolved is null) return fallback;
        return fallback with
        {
            Row = resolved.Row,
            Frames = resolved.Frames,
            FrameDurations = resolved.FrameDurationsMs,
            Loop = resolved.Loop,
            ExternalSheet = resolved.Sheet,
            FrameWidth = resolved.FrameWidth,
            FrameHeight = resolved.FrameHeight,
            VerticalStrip = resolved.Vertical,
            AtlasRowSource = resolved.AtlasRowSource,
            ResolvedSource = resolved.SourceLabel
        };
    }

    private void BuildActionGallery()
    {
        ActionGallery.Clear();
        RegularActionGalleryGroups.Clear();
        AutonomousActionGallery.Clear();
        InteractiveActionGallery.Clear();
        MagicActionGallery.Clear();
        SeasonalActionGallery.Clear();
        AddGallery("侧躺慢呼吸", "安静待机", "最常用的幼猫侧躺姿态，慢呼吸、眨眼和尾尖轻动", SideLieIdleSequence);
        AddGallery("低趴观察", "安静待机", "胸口贴地趴着，从侧面观察主人", ProneIdleSequence);
        AddGallery("舔脚吃脚", "安静待机", "抱住后脚舔毛并轻轻啃爪子", PawNibbleSequence);
        AddGallery("日常舔毛梳理", "生活日常", "低频依次舔前爪、胸口、侧腹和大尾巴，不会高频重复", FurGroomSequence);
        AddGallery("蓝色长方形小窝", "宠物装备", "使用预设窝内睡眠素材安静长驻留，不是叠加装扮", BlueBedSleepSequence);
        AddGallery("背影回头", "安静待机", "展示完整背部花纹、转耳朵并回头", RearIdleSequence);
        AddGallery("侧面转背面", "安静待机", "从侧躺起身、转向背面再趴下", SideRearTransitionSequence);
        AddGallery("趴着呼吸", "基础表情", "缓慢呼吸、眨眼和甩尾尖", IdleSequence);
        AddGallery("侧躺打滚", "基础表情", "露肚皮并原地翻身", RollSequence);
        AddGallery("追尾转圈", "基础表情", "追着大尾巴转圈", SpinSequence);
        AddGallery("眨眼哈欠", "基础表情", "眨眼、哈欠、舔爪和小脾气", ExpressionSequence);
        AddGallery("轻轻撸猫", "触摸反应", "注意触摸、慢眨眼和靠近脸颊", GentleTouchSequence);
        AddGallery("呼噜踩奶", "触摸反应", "闭眼、踩奶、侧躺与呼噜呼吸", PurrSequence);
        AddGallery("好奇询问", "触摸反应", "歪头、抬爪并看向主人", CuriousTouchSequence);
        AddGallery("开始烦躁", "触摸反应", "移开视线、轻压耳朵、甩尾并转身，不攻击主人", AnnoyedTouchSequence);
        AddGallery("需要距离", "触摸反应", "不理人、后退、转身并准备跑开，不哈气也不露齿", AngryTouchSequence);
        AddGallery("信任亲近", "触摸反应", "鼻尖触碰、蹭头、慢眨眼与安心睡下", TrustTouchSequence);
        AddGallery("猫粮慢慢吃", "投喂互动", "吃一口停一下，磨磨蹭蹭看向别处", KibbleEatingSequence);
        AddGallery("冻干饿猫扑食", "投喂互动", "看见冻干立刻扑过去急切地吃", FreezeDriedEatingSequence);
        AddGallery("罐头饿猫扑食", "投喂互动", "闻到罐头后飞快扑到碗边舔食", CannedEatingSequence);
        AddGallery("检查猫砂", "生活互动", "刨砂、检查并满意收尾", CleanSequence);
        AddGallery("自发如厕", "生活日常", "自己进入猫砂盆、蹲稳后完成如厕", ToiletRelieveSequence);
        AddGallery("如厕时偶尔抬头", "生活日常", "如厕过程中偶尔抬头看看主人，不是每次强制出现", ToiletLookUpSequence);
        AddGallery("爪爪开花埋屎", "生活日常", "如厕后连续抓砂、认真把便便埋好再离开", ToiletBurySequence);
        AddGallery("开心摸摸", "生活互动", "只表现猫本体：朝看不见的轻触方向靠近、眯眼和亲近", HappyPetSequence);
        AddGallery("过度rua", "生活互动", "只表现猫本体：移开视线、贴地甩尾、退开并安静表达边界", OverPetSequence);
        AddGallery("梳理毛发", "生活互动", "梳后背、大尾巴和舔爪整理", GroomSequence);
        AddGallery("玩逗猫棒", "玩耍服务", "观察、伏低、挥爪和扑跳", WandLoopSequence);
        AddGallery("追逐激光点", "移动互动", "八方向低伏追逐；每个方向四个脚步相位，换帧与位移同步", LaserAnchorChaseSequence);
        AddGallery("追逐冻干", "移动互动", "八方向急切小跑；脚步不动时窗口也不会漂移", SnackAnchorChaseSequence);
        AddGallery("笼中安静躺卧", "限制状态", "正面、侧面与背面三组笼中躺卧；释放前不切换普通动作", CageRestSequence);
        AddGallery("主动求关注", "自主行为", "趴卧、歪头、伸爪吸引主人", AttentionSequence);
        AddGallery("偷偷捣乱", "自主行为", "拨弄笔筒和装作无事发生", MischiefSequence);
        AddGallery("安静陪伴", "自主行为", "趴卧、蜷尾和打呼噜", FocusSequence);
        AddGallery("长时间睡觉", "自主行为", "原地缓慢呼吸和打呼噜", SleepSequence);
        AddGallery("团成一团睡", "自主睡眠", "白天更常出现，团起身体并以慢呼吸循环", SleepCurledSequence);
        AddGallery("四仰八叉睡", "自主睡眠", "安全感较高时露出肚皮安静熟睡", SleepBellyUpSequence);
        AddGallery("侧身伸展睡", "自主睡眠", "侧身拉长身体、尾巴轻动并持续熟睡", SleepSideSequence);
        AddGallery("睡姿平滑转换", "自主睡眠", "在不同睡姿之间缓慢翻身，避免动作跳变", SleepTransitionSequence);
        AddGallery("板鸭趴", "安静待机", "胸口贴地、后腿向后舒展的放松劈腿趴姿", SplootSequence);
        AddGallery(
            "鼠标视线跟随",
            "环境感知",
            "侧躺、低趴、板鸭趴和陪伴四种高频原地姿态使用完整身体视线变体，不叠加悬浮脑袋",
            Sequence(
                "gaze-fullbody-16",
                SpriteAtlas.GazeCoin,
                0,
                Enumerable.Repeat(520, 16).ToArray(),
                Enumerable.Range(0, 16).ToArray()));
        AddGallery("Accio Broom", "魔法特辑", "飞来咒·扫帚版：披斗篷召来扫帚并飞行", AccioBroomIntroSequence);
        AddGallery("Apparate", "魔法特辑", "幻影移形：原地转圈、消失并换一个位置出现", ApparateSequence);
        AddGallery("Petrificus Totalus", "魔法特辑", "统统石化：逐渐石化并变成头像银币等待解除", PetrifySequence);
        AddGallery("Scourgify", "魔法特辑", "清理一新：挥魔杖沿窗口或屏幕边缘擦出闪光", ScourgifySequence);
        AddGallery("圣诞帽", "节日装扮", "只在 12 月 25 日出现", ChristmasSequence, SeasonalOccasion.Christmas);
        AddGallery("万圣节斗篷帽", "节日装扮", "只在 10 月 31 日出现", HalloweenSequence, SeasonalOccasion.Halloween);
        AddGallery("春节红围巾", "节日装扮", "只在农历正月初一出现", SpringFestivalSequence, SeasonalOccasion.SpringFestival);
        AddGallery("主人生日祝福", "节日装扮", "只在档案中的主人生日出现，并结合年龄祝福", BirthdaySequence, SeasonalOccasion.OwnerBirthday);
        AddGallery("叼绳求遛", "自主行为", "看见牵引绳后兴奋要求散步", AskWalkSequence);
        AddGallery("向左跑", "四方向移动", "桌面向左移动帧", RunLeftSequence);
        AddGallery("向右跑", "四方向移动", "桌面向右移动帧", RunRightSequence);
        AddGallery("向上跑", "四方向移动", "桌面背向主人移动帧", RunUpSequence);
        AddGallery("向下跑", "四方向移动", "桌面朝向主人移动帧", RunDownSequence);
        AddGallery("孔雀蓝背带·左", "背带遛猫", "穿孔雀蓝背带向左随机巡逻", HarnessWalkLeftSequence);
        AddGallery("孔雀蓝背带·右", "背带遛猫", "穿孔雀蓝背带向右随机巡逻", HarnessWalkRightSequence);
        AddGallery("孔雀蓝背带·背影", "背带遛猫", "穿背带背向主人移动", HarnessWalkUpSequence);
        AddGallery("孔雀蓝背带·正面", "背带遛猫", "穿背带朝向主人移动", HarnessWalkDownSequence);
        AddGallery("孔雀蓝背带·左前", "八方向移动", "拿破仑矮脚体型向左前方连续步行", HarnessWalkDownLeftSequence);
        AddGallery("孔雀蓝背带·右前", "八方向移动", "拿破仑矮脚体型向右前方连续步行", HarnessWalkDownRightSequence);
        AddGallery("孔雀蓝背带·左后", "八方向移动", "拿破仑矮脚体型向左后方连续步行", HarnessWalkUpLeftSequence);
        AddGallery("孔雀蓝背带·右后", "八方向移动", "拿破仑矮脚体型向右后方连续步行", HarnessWalkUpRightSequence);
        AddGallery("自由外出·左", "无背带遛猫", "解除背带后向左探索", FreeWalkLeftSequence);
        AddGallery("自由外出·右", "无背带遛猫", "解除背带后向右探索", FreeWalkRightSequence);
        AddGallery("自由外出·背影", "无背带遛猫", "解除背带后背向主人探索", FreeWalkUpSequence);
        AddGallery("自由外出·正面", "无背带遛猫", "解除背带后朝向主人探索", FreeWalkDownSequence);
        AddGallery("自由外出·左前", "八方向移动", "无背带以矮脚步态向左前方探索", FreeWalkDownLeftSequence);
        AddGallery("自由外出·右前", "八方向移动", "无背带以矮脚步态向右前方探索", FreeWalkDownRightSequence);
        AddGallery("自由外出·左后", "八方向移动", "无背带以矮脚步态向左后方探索", FreeWalkUpLeftSequence);
        AddGallery("自由外出·右后", "八方向移动", "无背带以矮脚步态向右后方探索", FreeWalkUpRightSequence);
    }

    private void BuildAssetActionGroups()
    {
        AssetActionGroups.Clear();
        foreach (var status in _assetPack.ActionGroupStatuses)
        {
            var item = new AssetActionGroupViewItem
            {
                Status = status,
                GroupId = status.GroupId,
                BehaviorId = status.BehaviorId,
                Source = status.SourceLabel,
                Timing = status.TimingLabel,
                LoopMode = status.LoopMode,
                Fallback = status.FallbackLabel,
                Validation = status.Validation,
                Trigger = status.TriggerLabel
            };
            foreach (var frame in _assetPack.CreatePreviewFrames(status))
                item.PreviewFrames.Add(frame);
            AssetActionGroups.Add(item);
        }
        SelectedAssetActionGroup = AssetActionGroups.FirstOrDefault();
    }

    private void BuildCoinUpdateStates()
    {
        CoinUpdateStates.Clear();
        var states = new[]
        {
            ("normalColor", "正常·彩色"),
            ("normalFaded", "正常·褪色"),
            ("unhappyColor", "不开心·彩色"),
            ("unhappyFaded", "不开心·褪色"),
            ("back", "猫爪背面")
        };
        foreach (var (key, name) in states)
        {
            if (!_assetPack.Manifest.CoinStates.TryGetValue(key, out var definition))
                continue;
            var preview = _assetPack.CreateCoinStateFrame(definition);
            if (preview is null)
                continue;
            CoinUpdateStates.Add(new CoinStateViewItem
            {
                StateKey = key,
                Name = name,
                Coordinate = $"{definition.Atlas} · 行 {definition.Row} · 帧 {definition.Frames[0]}",
                Duration = definition.FrameDurations.Count == 0
                    ? "时长：默认 1000 ms"
                    : $"时长：{definition.FrameDurations[0]} ms",
                Preview = preview
            });
        }
    }

    private void AddGallery(
        string name,
        string category,
        string description,
        AnimationSequence sequence,
        SeasonalOccasion availability = SeasonalOccasion.None)
    {
        sequence = ResolveAnimationSequence(sequence);
        var behaviorId = GalleryBehaviorId(sequence.Name);
        var animationSource = string.IsNullOrWhiteSpace(sequence.ResolvedSource)
            ? $"{sequence.Atlas}:{sequence.Name}:row={sequence.Row}"
            : $"actionGroup:{sequence.Name}:{sequence.ResolvedSource}";
        var availabilityLabel = availability switch
        {
            SeasonalOccasion.Christmas => "圣诞限定 · 仅 12 月 25 日",
            SeasonalOccasion.Halloween => "万圣节限定 · 仅 10 月 31 日",
            SeasonalOccasion.SpringFestival => "春节限定 · 仅农历正月初一",
            SeasonalOccasion.OwnerBirthday => "主人生日限定",
            _ => string.Empty
        };
        if (availability != SeasonalOccasion.None)
        {
            SeasonalActionGallery.Add(new SeasonalGalleryItem
            {
                BehaviorId = behaviorId,
                Name = name,
                Description = description,
                AvailabilityLabel = availabilityLabel
            });
            return;
        }
        sequence = GalleryPreviewSequence(sequence);
        var sheet = sequence.ExternalSheet ?? SheetFor(sequence.Atlas);
        var x = sequence.VerticalStrip ? 0 : sequence.Frames[0] * sequence.FrameWidth;
        var y = sequence.ExternalSheet is null || sequence.AtlasRowSource
            ? sequence.Row * sequence.FrameHeight
            : sequence.VerticalStrip
                ? sequence.Frames[0] * sequence.FrameHeight
                : 0;
        var frame = _presentationHost.CropImage(
            sheet,
            x,
            y,
            sequence.FrameWidth,
            sequence.FrameHeight);
        if (frame is null) return;
        var previewCommand = new RelayCommand(
            () => _ = PreviewGalleryActionAsync(name, description, sequence));
        var item = new ActionGalleryItem
        {
            BehaviorId = behaviorId,
            AnimationSource = animationSource,
            Name = name,
            Category = category,
            Description = description,
            FrameLabel = $"{sequence.Frames.Length} 帧 · {sequence.Loop switch { true => "循环", false => "单次" }}",
            AvailabilityLabel = string.Empty,
            Thumbnail = frame,
            PreviewCommand = previewCommand
        };
        if (category == "魔法特辑")
        {
            MagicActionGallery.Add(item);
            return;
        }
        ActionGallery.Add(item);
        if (IsInteractiveGalleryCategory(category))
            InteractiveActionGallery.Add(item);
        else
            AutonomousActionGallery.Add(item);
        var group = RegularActionGalleryGroups.FirstOrDefault(x =>
            string.Equals(x.Name, category, StringComparison.Ordinal));
        if (group is null)
        {
            group = new ActionGalleryGroupItem { Name = category };
            RegularActionGalleryGroups.Add(group);
        }
        group.Items.Add(item);
    }

    private static AnimationSequence GalleryPreviewSequence(AnimationSequence sequence)
    {
        if (sequence.ExternalSheet is null ||
            (!string.Equals(sequence.Name, "laser-chase-8", StringComparison.Ordinal) &&
             !string.Equals(sequence.Name, "snack-chase-8", StringComparison.Ordinal)))
            return sequence;

        // A direction-major strip is eight separate gait loops, not one
        // continuous 32-frame movie.  The gallery previews one right-facing
        // gait and closes it as A-B-C-D-C-B to avoid a pose snap.
        return sequence with
        {
            Frames = new[] { 16, 17, 18, 19, 18, 17 },
            FrameDurations = Enumerable.Repeat(165, 6).ToArray(),
            Loop = true
        };
    }

    private static bool IsInteractiveGalleryCategory(string category) => category is
        "触摸反应" or
        "投喂互动" or
        "生活互动" or
        "玩耍服务" or
        "移动互动" or
        "限制状态" or
        "背带遛猫" or
        "无背带遛猫";

    private void BuildInformationCards()
    {
        ProductDesignCards.Clear();
        foreach (var (title, body) in new[]
                 {
                     ("产品定位", "pupu 是常驻 Windows 桌面的本地陪伴宠物。核心体验是可观察、可打断、有情绪边界的猫咪行为，而不是悬浮工具栏或持续催促主人的提醒器。"),
                     ("形象一致性", "所有动作共享银灰黑白长毛拿破仑矮脚猫身份：幼态圆脸、短口鼻、黄绿色圆眼、粉黑鼻头、较长躯干和完整大尾巴。新增道具不能改变本体比例。"),
                     ("注意力与主动锚点", "普通鼠标靠近只是最低优先级注意力信号，只在当前姿态支持时记录局部视线方向，不硬切大动作；食物和玩具必须由主人先进入一次性锚点模式，再由本地参与规则、行为提案队列和仲裁器决定是否追过去。"),
                     ("互动能力", "食物、玩具、安静／自主玩耍口令、关笼子和旅游都是本地结构化事件。关笼子与旅行是主人强制状态；模型只可润色回复，不能控制出发、返回、锚点或动画。"),
                     ("真实桌面移动", "只有行走、遛猫、逃离、窗口边缘活动和魔法飞行会改变窗口坐标。原地姿势只播放动画，避免出现原地踏步或无缘由漂移。"),
                     ("记忆与档案", "宠物档案、主人称呼、天生性格、关系、纠正、长期记忆、相册描述和短期会话共同构成连续相处背景；主人可在 Markdown 页面维护核心设定。"),
                     ("相册经历记忆库", "相册只链接主人选择的本地文件夹，不复制、移动或改写原图。逐图描述、Markdown／JSON 发帖和可选旅行返回故事进入独立经历索引；规则模式和模型模式共用同一检索结果，发送图片仍需显式授权。"),
                     ("无 API 与 LLM 共存", "本地规则 PetAgent 是默认可运行后端；LLM 只增强回复文本。两者读取同一 Persona 和同一批脱敏记忆摘要，行为候选与记忆候选仍由本地代码验证，不能由模型直接执行或写入。"),
                     ("素材包动作组化", "旧 SpriteAtlas + row 图集继续有效；schema 2 可按行为 ID 定义动作组、独立动作文件、帧节奏、intro／loop／exit、方向、姿态、鼠标视线、食物／玩具能力与 fallback。动作组在素材包页逐帧只读预览。"),
                     ("隐私与离线", "档案、行为、相册索引、描述、对话和 API 设置默认保存在本机。主人离线不会形成照料欠账、责怪、关系惩罚或报复性行为。")
                 })
            ProductDesignCards.Add(new InformationCardItem { Title = title, Body = body });

        CodeImplementationCards.Clear();
        foreach (var (title, body) in new[]
                 {
                     ("界面架构", "MainWindow 承载透明宠物窗、气泡和真实坐标移动；ControlWindow 承载产品设置、档案、相册、动作库、模型、记忆和调试视图；两者共享同一个 MainViewModel。"),
                     ("行为核心", "Pupu.Behavior 将资格过滤、效用评分、选择策略和动作调度分层。状态、关系、天生性格和具体 LearnedPreference 各自独立，互动不会偷偷回写天生性格。"),
                     ("行为仲裁与提案执行", "BehaviorArbitrator 继续负责优先级、保护期、可打断性、冷却和状态禁用；BehaviorProposalQueue／Executor 为口令、锚点和相册经历建议提供统一路径。暂时不能打断的提案可延迟，过期后取消；接受和拒绝原因都进入调试。"),
                     ("主人强制与普通互动", "主人从魔法菜单明确发起时使用 OwnerForced 优先级和 ForceInterrupt，只允许笼中、旅行中或已石化等硬状态阻止；自主魔法仍受每日一次、冷却和资格评分限制。普通照料与玩耍不使用强制优先级，继续尊重疲劳、压力和动作保护期。"),
                     ("冻干／激光投放链路", "点击按钮只进入一次性选点模式，不创建行为租约；主人选中桌面坐标后才生成一个 OwnerAnchor 提案。仲裁通过后显示冻干或激光实体、按目标向量选择八方向四相步态、同步移动窗口，到达后分别衔接进食或低伏扑光点。取消、拒绝、超时和路径不可达都会退出选点模式并给出反馈。"),
                     ("PetAgent／Persona", "RulePetAgent 是不依赖 API 的薄层，接收聊天、口令、面板、锚点、经历、旅游和自主定时器等结构化事件，输出回复草稿、行为提案、记忆候选和调试信息。Persona 不只是提示词，也保存默认性格、行为偏好和记忆偏好；默认朴朴配置保持原效果。"),
                     ("素材运行时", "AssetPackService 同时支持 schema 1 的 SpriteAtlas + row 和 schema 2 动作组。播放时优先解析动作组，来源缺失或无效则回到动作组 fallback 或原硬编码序列；单文件 PNG 与条带动作文件已预留解析和预览能力。"),
                     ("动作预览分类", "动作页按行为来源划分为自发行为、互动行为、魔法特辑和节日特辑四个页签。卡片保留更细的动作类别、行为 ID、动画来源、帧数、循环方式和预览；节日卡只展示日期门禁说明，不允许通过预览绕过日期规则。"),
                     ("模型协议", "ModelProtocolAdapter 负责 Chat Completions / Responses 的请求与响应翻译；ModelApiService 负责 HTTPS、凭据、重试和安全错误。OpenAI、Qwen、DeepSeek 与 Custom 共用同一会话管线。"),
                     ("系统提示词", "固定角色安全边界由 PetSpeechComposer 生成；宠物档案、状态、关系、自然语言规则和 pupu-memory.md 的宠物系统提示词作为 system 背景注入。模型回复仍要通过宠物语言边界。"),
                     ("相册与经历索引", "PhotoAlbumService 继续维护 albums.json、子相册和逐图描述；AlbumExperienceService 以独立 schema 版本索引图片、Markdown／JSON 和 travelEvent，后台扫描带取消与过期保护，索引只保存相对素材引用。"),
                     ("经历对话边界", "规则模式和 LLM 模式使用同一相关性排序。模型最多接收少量摘要、有限原文片段和最多两张授权图片，不接收绝对路径；经历建议先由本地 PetAgent 验证为提案，再经 BehaviorArbitrator 和统一执行器播放轻动作。"),
                     ("隐私与 Token", "提示词只包含相关长期记忆摘要和最多三条相册经历摘要；图片最多两张且使用 data URL，不发送本地绝对路径。调试页提供提示词边界预览、粗略 Token 估算和 LLM 降级原因。"),
                     ("Windows／Mac 边界", "Core 位于 Pupu.Behavior，不依赖 UI 框架、Windows API、LLM 或具体宠物形象。Windows WPF 平台层负责窗口、输入、渲染、文件选择和凭据；未来 Mac 平台可复用 Persona、PetAgent、提案管线和素材清单模型。"),
                     ("持久化", "关键 JSON 使用临时文件后原子替换；pupu-memory.md 和 events.md 是主人可读入口；短期对话有轮数上限；API Key 只进入 Windows 凭据管理器。"),
                     ("验证边界", "发布前执行绑定检查、行为与纯逻辑测试、WPF Release 构建和素材审计。Linux 环境不能替代 Windows 实机的混合 DPI、跨屏、窗口句柄和凭据管理器验证。")
                 })
            CodeImplementationCards.Add(new InformationCardItem { Title = title, Body = body });
    }

    private Task PreviewGalleryActionAsync(
        string name,
        string description,
        AnimationSequence sequence)
    {
        if (!IsReady) return Task.CompletedTask;
        var resolved = ResolveAnimationSequence(sequence);
        var sheet = resolved.ExternalSheet ?? SheetFor(resolved.Atlas);
        var frames = new List<object>();
        foreach (var frameNumber in resolved.Frames)
        {
            var x = resolved.VerticalStrip ? 0 : frameNumber * resolved.FrameWidth;
            var y = resolved.ExternalSheet is null || resolved.AtlasRowSource
                ? resolved.Row * resolved.FrameHeight
                : resolved.VerticalStrip
                    ? frameNumber * resolved.FrameHeight
                    : 0;
            var frame = _presentationHost.CropImage(
                sheet,
                x,
                y,
                resolved.FrameWidth,
                resolved.FrameHeight);
            if (frame is not null) frames.Add(frame);
        }
        _presentationHost.ShowActionPreview(
            name,
            frames,
            resolved.FrameDurations,
            resolved.Loop);
        return Task.CompletedTask;
    }

    public void SetMovementDirection(PetDirection direction, DesktopMoveMode mode)
    {
        _currentDirection = direction;
        var harnessed = mode == DesktopMoveMode.HarnessedWalk;
        var useWalkModes = mode is DesktopMoveMode.HarnessedWalk or DesktopMoveMode.FreeRoam;
        var sequence = mode == DesktopMoveMode.BroomFlight
            ? direction switch
            {
                PetDirection.Left => BroomFlightLeftSequence,
                PetDirection.Up => BroomFlightUpSequence,
                PetDirection.Down => BroomFlightDownSequence,
                PetDirection.UpLeft => BroomFlightUpLeftSequence,
                PetDirection.UpRight => BroomFlightUpRightSequence,
                PetDirection.DownLeft => BroomFlightDownLeftSequence,
                PetDirection.DownRight => BroomFlightDownRightSequence,
                _ => BroomFlightRightSequence
            }
            : useWalkModes ? direction switch
        {
            PetDirection.Right => harnessed ? HarnessWalkRightSequence : FreeWalkRightSequence,
            PetDirection.Up => harnessed ? HarnessWalkUpSequence : FreeWalkUpSequence,
            PetDirection.Down => harnessed ? HarnessWalkDownSequence : FreeWalkDownSequence,
            PetDirection.UpLeft => harnessed ? HarnessWalkUpLeftSequence : FreeWalkUpLeftSequence,
            PetDirection.UpRight => harnessed ? HarnessWalkUpRightSequence : FreeWalkUpRightSequence,
            PetDirection.DownLeft => harnessed ? HarnessWalkDownLeftSequence : FreeWalkDownLeftSequence,
            PetDirection.DownRight => harnessed ? HarnessWalkDownRightSequence : FreeWalkDownRightSequence,
            _ => harnessed ? HarnessWalkLeftSequence : FreeWalkLeftSequence
        } : direction switch
        {
            PetDirection.Right => RunRightSequence,
            PetDirection.Up => RunUpSequence,
            PetDirection.Down => RunDownSequence,
            PetDirection.UpLeft => FreeWalkUpLeftSequence,
            PetDirection.UpRight => FreeWalkUpRightSequence,
            PetDirection.DownLeft => FreeWalkDownLeftSequence,
            PetDirection.DownRight => FreeWalkDownRightSequence,
            _ => RunLeftSequence
        };
        PlayMovementSequence(sequence);
    }

    public void SetMovementHeading(double deltaX, double deltaY, DesktopMoveMode mode)
    {
        if (Math.Abs(deltaX) + Math.Abs(deltaY) < 0.001) return;
        var angle = Math.Atan2(deltaX, -deltaY);
        if (angle < 0) angle += Math.PI * 2;
        var direction16 = (int)Math.Round(angle / (Math.PI * 2) * 16) % 16;
        _currentDirection = direction16 switch
        {
            0 or 1 => PetDirection.Up,
            2 or 3 => PetDirection.UpRight,
            4 or 5 => PetDirection.Right,
            6 or 7 => PetDirection.DownRight,
            8 or 9 => PetDirection.Down,
            10 or 11 => PetDirection.DownLeft,
            12 or 13 => PetDirection.Left,
            _ => PetDirection.UpLeft
        };
        var groupId = mode == DesktopMoveMode.AnchorApproach
            ? (_activeAnchorIsFood ? "snack-chase-8" : "laser-chase-8")
            : mode == DesktopMoveMode.HarnessedWalk
                ? "harness-walk-16"
                : string.Empty;
        var resolved = groupId.Length == 0 ? null : _assetPack.ResolveActionGroup(groupId);
        if (resolved is null)
        {
            SetMovementDirection(_currentDirection, mode);
            return;
        }
        var direction8 = _currentDirection switch
        {
            PetDirection.Left => 0,
            PetDirection.UpLeft => 1,
            PetDirection.Up => 2,
            PetDirection.UpRight => 3,
            PetDirection.Right => 4,
            PetDirection.DownRight => 5,
            PetDirection.Down => 6,
            PetDirection.DownLeft => 7,
            _ => 4
        };
        var isFourPhaseAnchor = mode == DesktopMoveMode.AnchorApproach;
        var firstDirectionFrame = direction8 * 4;
        var frames = isFourPhaseAnchor
            ? new[]
            {
                firstDirectionFrame,
                firstDirectionFrame + 1,
                firstDirectionFrame + 2,
                firstDirectionFrame + 3,
                firstDirectionFrame + 2,
                firstDirectionFrame + 1
            }
            : new[] { direction16 };
        var sequence = new AnimationSequence(
            $"{groupId}-{(isFourPhaseAnchor ? direction8 : direction16):00}",
            SpriteAtlas.Directions,
            0,
            frames,
            Enumerable.Repeat(isFourPhaseAnchor ? 165 : 180, frames.Length).ToArray())
        {
            Loop = true,
            ExternalSheet = resolved.Sheet,
            FrameWidth = resolved.FrameWidth,
            FrameHeight = resolved.FrameHeight,
            VerticalStrip = resolved.Vertical,
            AtlasRowSource = false,
            ResolvedSource = resolved.SourceLabel
        };
        PlayMovementSequence(sequence);
    }

    private void PlayMovementSequence(AnimationSequence sequence)
    {
        sequence = ResolveAnimationSequence(sequence);
        if (_currentSequence.Name == sequence.Name) return;
        var preservePhase =
            _currentSequence.Name.StartsWith("run-", StringComparison.Ordinal) ||
            _currentSequence.Name.StartsWith("harness-", StringComparison.Ordinal) ||
            _currentSequence.Name.StartsWith("free-", StringComparison.Ordinal) ||
            _currentSequence.Name.StartsWith("laser-chase-8-", StringComparison.Ordinal) ||
            _currentSequence.Name.StartsWith("snack-chase-8-", StringComparison.Ordinal) ||
            _currentSequence.Name.StartsWith("harness-walk-16-", StringComparison.Ordinal) ||
            _currentSequence.Name.StartsWith("magic-accio-broom-flight-", StringComparison.Ordinal);
        var previousLength = Math.Max(1, _currentSequence.Frames.Length);
        var phase = Math.Clamp(_framePosition / (double)previousLength, 0, 0.999);
        _currentSequence = sequence;
        _framePosition = preservePhase
            ? Math.Min(sequence.Frames.Length - 1, (int)Math.Floor(phase * sequence.Frames.Length))
            : 0;
        if (!_synchronizedMovement)
            _animationTimer.Start();
        RenderNextFrame();
    }

    public void BeginSynchronizedMovement()
    {
        _synchronizedMovement = true;
        _animationTimer.Stop();
    }

    public void StepSynchronizedMovementFrame()
    {
        if (!_synchronizedMovement || _disposed) return;
        RenderNextFrame();
    }

    public int SynchronizedMovementStepMilliseconds
    {
        get
        {
            var position = Math.Clamp(
                _framePosition,
                0,
                Math.Max(0, _currentSequence.Frames.Length - 1));
            var speedMultiplier =
                IsReady ? _memory.BehaviorPolicy.AnimationSpeedMultiplier : 1.0;
            return (int)Math.Clamp(
                _currentSequence.DurationAt(position) * speedMultiplier,
                80,
                220);
        }
    }

    public void EndSynchronizedMovement()
    {
        if (!_synchronizedMovement) return;
        _synchronizedMovement = false;
        if (!_disposed)
            _animationTimer.Start();
    }

    private void RenderNextFrame()
    {
        var sheet = _currentSequence.ExternalSheet ?? SheetFor(_currentSequence.Atlas);
        var position = Math.Clamp(_framePosition, 0, _currentSequence.Frames.Length - 1);
        var frameNumber = _currentSequence.Frames[position];
        var x = _currentSequence.VerticalStrip ? 0 : frameNumber * _currentSequence.FrameWidth;
        var y = _currentSequence.ExternalSheet is null || _currentSequence.AtlasRowSource
            ? _currentSequence.Row * _currentSequence.FrameHeight
            : _currentSequence.VerticalStrip
                ? frameNumber * _currentSequence.FrameHeight
                : 0;
        var frame = _presentationHost.CropImage(
            sheet,
            x,
            y,
            _currentSequence.FrameWidth,
            _currentSequence.FrameHeight);
        if (frame is not null)
            PetFrame = CurrentGazeFullBodyFrame() ?? frame;
        var speedMultiplier = IsReady ? _memory.BehaviorPolicy.AnimationSpeedMultiplier : 1.35;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(_currentSequence.DurationAt(position) * speedMultiplier);
        if (!_currentSequence.Loop && position == _currentSequence.Frames.Length - 1)
        {
            _framePosition = position;
            _animationTimer.Stop();
        }
        else
        {
            _framePosition = (_framePosition + 1) % _currentSequence.Frames.Length;
        }
    }

    private object SheetFor(SpriteAtlas atlas) => _assetPack.GetSheet(atlas switch
    {
        SpriteAtlas.Core => "core",
        SpriteAtlas.Life => "life",
        SpriteAtlas.Directions => "directions",
        SpriteAtlas.Touch => "touch",
        SpriteAtlas.Routines => "routines",
        SpriteAtlas.WalkModes => "walkModes",
        SpriteAtlas.Activity => "activity",
        SpriteAtlas.LifeEquipment => "lifeEquipment",
        SpriteAtlas.Motion => "motion",
        SpriteAtlas.GazeCoin => "gazeCoin",
        SpriteAtlas.Litter => "litter",
        SpriteAtlas.Specials => "specials",
        SpriteAtlas.Seasonal => "seasonal",
        _ => "core"
    });

    private void ScheduleNextAutonomousAction()
    {
        var seconds = IsReady ? _memory.BehaviorPolicy.AutonomousDecisionSeconds : 12;
        _nextAutonomousActionAt = _clock.Now.AddSeconds(seconds);
    }

    private Task ShowBubbleAsync(
        string? text,
        int durationMilliseconds = 4000,
        PetSpeechIntent intent = PetSpeechIntent.General)
    {
        _bubbleCancellation?.Cancel();
        _bubbleCancellation = new CancellationTokenSource();
        var token = _bubbleCancellation.Token;
        BubbleText = ComposePetSpeech(intent, text);
        IsBubbleVisible = true;
        return HideBubbleLaterAsync(durationMilliseconds, token);
    }

    private async Task HideBubbleLaterAsync(int durationMilliseconds, CancellationToken token)
    {
        try
        {
            await Task.Delay(durationMilliseconds, token);
            if (!token.IsCancellationRequested) IsBubbleVisible = false;
        }
        catch (OperationCanceledException) { }
    }

    private void RefreshNaturalRules()
    {
        NaturalRules.Clear();
        foreach (var rule in _memory.BehaviorPolicy.NaturalLanguageRules.AsEnumerable().Reverse())
            NaturalRules.Add($"规则 · {rule}");
        foreach (var memory in _memory.Profile.ManualMemories.AsEnumerable().Reverse())
            NaturalRules.Add($"记忆 · {memory}");
        OnPropertyChanged(nameof(NaturalPolicySummary));
    }

    private void RefreshHiddenActionRules()
    {
        HiddenActionRules.Clear();
        if (!IsReady) return;
        var policy = _memory.BehaviorPolicy;
        var touch = _memory.GetTouchReactionProfile();
        HiddenActionRules.Add("统一自主决策：所有候选都按 BaseWeight + TemperamentAffinity + RuntimeStateFit + RelationshipFit + LearnedPreference + ContextFit - Cooldown - Repetition - Interruption + SeededJitter 评分。");
        HiddenActionRules.Add($"手势两阶段：GestureInterpreter 输出 touch/stroke/hold/drag/rapid_tap/release；先更新 RuntimeState，再统一选择享受、好奇、忍耐、警告、回避或跑开。当前有界容忍范围 {touch.AnnoyedAt}–{touch.AngryAt}，不是点击次数直绑动画。");
        HiddenActionRules.Add("活泼：进入自主玩耍、探索、鼠标追踪、短距离走动和接受玩具评分；疲劳、压力、深夜和勿扰可压过它。");
        HiddenActionRules.Add("黏人：进入主动靠近、附近休息、呼噜、踩奶、回应呼唤和一次性求关注评分；未响应后不会重复催促，高压力时会被压制。");
        HiddenActionRules.Add("敏感：影响快速点击/突然拖动的压力增量、警戒/退开/安静位置评分和压力恢复；梳毛由敏感、压力、信任与梳毛偏好连续计算，无 75% 硬阈值。");
        HiddenActionRules.Add("独立：提高巡视、自我清洁、远处休息和自行结束互动；不扣信任，也不延长生气或跑开时间。");
        HiddenActionRules.Add("淘气：清醒、低压力、有精力时可自然选择拨弄物品、藏起、绕行和扑击；不再要求长期忽略，深夜/会议/勿扰/全屏压制高干扰动作。");
        HiddenActionRules.Add($"长互动：投喂、梳毛、逗猫棒和散步按进度提交效果；Started/Progressed/Completed/Interrupted/Failed 全部写 events.md。“停下”始终有效，当前散步约 {policy.WalkDurationMinutes} 分钟。");
        HiddenActionRules.Add("自发如厕：每个本地日期随机安排 2–3 个时段，只有 toilet_due 才进入统一自主评分；离线错过不补播，如厕后固定衔接抓砂埋屎，抬头仅是概率变化。主人手动铲屎入口已停用。");
        HiddenActionRules.Add("主人互动参与：吃饭、遛猫、梳毛、玩具、姿势和魔法先读取饱腹、精力、压力、疲劳、关系、性格与具体偏好；拒绝使用宠物台词说明，不扣信任。");
        HiddenActionRules.Add("宠物魔法：四项魔法和普通自主行为进入同一过滤、评分、偏好与选择管线；daily_magic_available 是硬门槛，每个本地日期最多一次自发施法。");
        HiddenActionRules.Add("节日装扮：圣诞帽仅 12 月 25 日、万圣节斗篷帽仅 10 月 31 日、春节红围巾仅农历正月初一；主人生日按档案月日匹配并计算年龄。");
        HiddenActionRules.Add("统一行为仲裁：主人强制 > 主动锚点 > 明确口令/面板 > 触摸 > 持续魔法 > 自主移动 > 普通鼠标注意力 > 装饰 idle；每次请求检查保护期、冷却、可打断性和禁用状态。");
        HiddenActionRules.Add("统一提案执行：相册经历建议、本地安静／自主玩耍口令和食物／玩具锚点先形成含来源、优先级、有效期和取消策略的 BehaviorProposal，再由 BehaviorProposalExecutor 调用 BehaviorArbitrator；模型输出不能直接进入执行器。");
        HiddenActionRules.Add($"当前 Persona：{CurrentPersonaSummary}；规则 PetAgent 默认可独立运行，LLM 只增强回复。");
        HiddenActionRules.Add("鼠标视线：普通靠近只记录当前姿态可表达的方向，不再切换固定 GazeCoin；睡眠、如厕、魔法、移动、触摸、进食和普通玩耍期间禁止抢占。");
        HiddenActionRules.Add("主人锚点：只有面板或本地口令先进入食物/玩具模式后，下一次桌面点击才生成锚点；是否追过去还会读取性格、疲劳、压力、安全感和关系。");
        HiddenActionRules.Add("限制状态：关笼子和旅游都由主人强制请求进入；笼中锁定原地，旅行隐藏桌面宠物，只有释放、召回或到期返回能解除。");
        HiddenActionRules.Add("离线与缺席：关闭应用或多天不互动不会扣状态、累积猫砂/照料欠账，也不会触发责怪、报复或惩罚性捣乱。");
        HiddenActionRules.Add($"评分明细日志：{StoragePaths.BehaviorDecisionLog}");

        LearnedPreferenceItems.Clear();
        foreach (var line in _memory.GetLearnedPreferenceSummary()
                     .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            LearnedPreferenceItems.Add(line);
    }

    private void RefreshEditableTraits()
    {
        OnPropertyChanged(nameof(Playfulness));
        OnPropertyChanged(nameof(Clinginess));
        OnPropertyChanged(nameof(Sensitivity));
        OnPropertyChanged(nameof(Independence));
        OnPropertyChanged(nameof(Mischief));
    }

    private void RefreshEditableProfile()
    {
        OnPropertyChanged(nameof(PetChineseName));
        OnPropertyChanged(nameof(PetEnglishName));
        OnPropertyChanged(nameof(PetBreed));
        OnPropertyChanged(nameof(PetSex));
        OnPropertyChanged(nameof(PetBirthday));
        OnPropertyChanged(nameof(OwnerNickname));
        OnPropertyChanged(nameof(RelationshipToOwner));
        OnPropertyChanged(nameof(OwnerBirthday));
        OnPropertyChanged(nameof(OwnerPersonalityPrompt));
        OnPropertyChanged(nameof(PetProfileSummary));
        OnPropertyChanged(nameof(PetProfileTitle));
    }

    private string ComposePetSpeech(PetSpeechIntent intent, string? authoredDraft = null) =>
        _speech.Compose(
            intent,
            _memory.Personality,
            authoredDraft,
            _memory.Profile.ChineseName,
            _memory.Profile.OwnerAddress);

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(Fullness));
        OnPropertyChanged(nameof(Happiness));
        OnPropertyChanged(nameof(Cleanliness));
        OnPropertyChanged(nameof(Energy));
        OnPropertyChanged(nameof(Trust));
        OnPropertyChanged(nameof(LitterLevel));
        OnPropertyChanged(nameof(PetDisplaySize));
        OnPropertyChanged(nameof(PetScaleLabel));
        OnPropertyChanged(nameof(EffectivePersonality));
        OnPropertyChanged(nameof(PersonalityMemoryMatchSummary));
        OnPropertyChanged(nameof(RuntimeStateSummary));
        OnPropertyChanged(nameof(RelationshipStateSummary));
        OnPropertyChanged(nameof(NaturalPolicySummary));
        OnPropertyChanged(nameof(PetProfileTitle));
        OnPropertyChanged(nameof(PetProfileSummary));
        OnPropertyChanged(nameof(AutomaticPersonalitySummary));
        OnPropertyChanged(nameof(RelationshipStageDisplay));
        OnPropertyChanged(nameof(IsCaged));
        OnPropertyChanged(nameof(IsTraveling));
        OnPropertyChanged(nameof(IsPetOnDesktop));
        OnPropertyChanged(nameof(IsMovementLocked));
        OnPropertyChanged(nameof(ConfinementStatus));
        OnPropertyChanged(nameof(TravelStatus));
        OnPropertyChanged(nameof(AwayDesktopStatus));
        OnPropertyChanged(nameof(MouseInteractionModeLabel));
        RefreshHiddenActionRules();
        MemoryStatus = $"{_memory.Summary.TotalEvents} 条文本事件 · {_memory.Corrections.Count(x => !x.IsReverted)} 条有效纠正 · {_memory.Profile.ManualMemories.Count} 条手动记忆 · Markdown 主文件可直接编辑 · 最近整理 {_memory.Summary.LastConsolidatedAt.LocalDateTime:g}";
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        foreach (var command in new[]
                 {
                     FeedCommand, FeedKibbleCommand, FeedFreezeDriedCommand, FeedCannedCommand,
                     WalkCommand, HarnessWalkCommand, FreeRoamCommand,
                     CleanCommand, PetCommand, GroomCommand,
                     PlayWandCommand, PlayLaserCommand, LieDownCommand, RollCommand, SpinCommand, SendChatCommand,
                     AccioBroomCommand, ApparateCommand, PetrificusTotalusCommand, ScourgifyCommand,
                     ReleasePetrificationCommand, CageCommand, ReleaseCageCommand,
                     StartTravelCommand, RecallTravelCommand,
                     SaveModelApiCommand, TestModelApiCommand, DeleteModelApiKeyCommand, ApplyNaturalRuleCommand,
                     SaveEditableMemoryCommand, ReloadEditableMemoryCommand, CreateCodexIterationCommand,
                     LikeBehaviorCommand, DislikeBehaviorCommand, UndoCorrectionCommand,
                     SavePersonalityCommand, SavePetProfileCommand, ResetLearningCommand,
                     ZoomInCommand, ZoomOutCommand, ResetZoomCommand
                 }.OfType<AsyncRelayCommand>())
            command.RaiseCanExecuteChanged();
        foreach (var command in new[]
                 {
                     PetCommand, StopCurrentActionCommand, ToggleQuickActionsCommand,
                     StartFoodAnchorCommand, StartToyAnchorCommand, CancelMouseModeCommand,
                     OpenEditableMemoryCommand, OpenControlPanelCommand
                 }.OfType<RelayCommand>())
            command.RaiseCanExecuteChanged();
    }

    private static string BuildPersonalityLabel(TemperamentBaseline value) =>
        $"天生性格／主人设定：活泼 {value.Playful:P0} · 黏人 {value.Affectionate:P0} · 敏感 {value.Sensitive:P0} · 独立 {value.Independent:P0} · 淘气 {value.Mischievous:P0}";

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{Math.Round(duration.TotalMinutes, duration.TotalMinutes < 10 ? 1 : 0):0.#} 分钟"
            : $"{Math.Max(1, Math.Round(duration.TotalSeconds)):0} 秒";

    private static string TrimForMemory(string text)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 180 ? normalized : normalized[..180] + "…";
    }

    private static void OpenMemoryFolder()
    {
        Directory.CreateDirectory(StoragePaths.MemoryDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", StoragePaths.MemoryDirectory) { UseShellExecute = true });
    }

    private void OpenAssetFolder()
    {
        try
        {
            AssetPackStatus = _assetPack.EnsureEditableCopy();
            Process.Start(new ProcessStartInfo("explorer.exe", StoragePaths.AssetDirectory) { UseShellExecute = true });
            _ = ShowBubbleAsync("朴朴的新外套放好啦。", 3600, PetSpeechIntent.General);
        }
        catch (Exception ex)
        {
            AssetPackStatus = $"无法准备素材目录：{ex.Message}";
            _ = ShowBubbleAsync(null, 3600, PetSpeechIntent.RecoverableProblem);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animationTimer.Stop();
        _needsTimer.Stop();
        _autonomyTimer.Stop();
        _bubbleCancellation?.Cancel();
        _touchReactionCancellation?.Cancel();
        _touchReactionCancellation?.Dispose();
        _animationTimer.Dispose();
        _needsTimer.Dispose();
        _autonomyTimer.Dispose();
        _actionScheduler.Dispose();
        _modelApi.Dispose();
        if (_activeInteraction is { IsTerminal: false } interaction)
            _ = _interactionLifecycle.InterruptAsync(interaction, "shutdown");
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _ = _memory.SaveStateAsync();
    }
}
