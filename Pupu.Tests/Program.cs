using Pupu.Behavior;
using Pupu.Desktop.Models;
using Pupu.Desktop.Services;
using System.Text.Json;

var tests = new (string Name, Func<Task> Run)[]
{
    ("high playful changes long-run play/explore distribution", TestPlayfulDistribution),
    ("high affectionate approaches more and does not repeat unanswered initiatives", TestAffectionateInitiative),
    ("sensitive rapid tap stress and bounded trust buffer", TestSensitiveRapidTap),
    ("independent selects solo actions without trust or anger coupling", TestIndependentDistribution),
    ("mischief occurs naturally without ignored-owner prerequisite", TestMischiefWithoutIgnore),
    ("fatigue night do-not-disturb and stress override temperament", TestContextOverrides),
    ("single interaction never mutates temperament baseline", TestBaselineInvariant),
    ("habit requires enough samples across three dates", TestHabitEvidenceWindow),
    ("every autonomous score reads learned preference", TestEveryBehaviorReadsPreference),
    ("interrupted lifecycle preserves applied effects only", TestInterruptedLifecycle),
    ("legacy migration is idempotent", TestMigrationIdempotency),
    ("24-hour simulation avoids twitch switching", TestTwentyFourHourStability),
    ("multi-day absence creates no penalty debt or blame", TestOfflineAbsence),
    ("eligibility hard-blocks unsafe behaviors before scoring", TestEligibilityPipeline),
    ("selection policy is reproducible inside top utility band", TestSelectionPolicyReproducibility),
    ("runtime recovery and resume changes are bounded", TestRuntimeDynamics),
    ("mouse perception replay habituates repeated passes", TestMouseHabituationReplay),
    ("sleep wake time and display events replay without false inference", TestSystemPerceptionReplay),
    ("continuous touch remains one learning session", TestContinuousTouchSession),
    ("unanswered pet initiative naturally ends without trust loss", TestUnansweredInitiative),
    ("deleting evidence cascades into derived preference", TestMemoryDeletionCascade),
    ("confirmed owner fact overrides inferred fact", TestConfirmedFactPriority),
    ("interaction regions follow pose frame direction and scale", TestInteractionRegionMap),
    ("scheduler exposes enter loop exit and interruption phases", TestActionSchedulerPhases),
    ("daytime favors varied sleep while deep night allows quiet activity", TestSleepDayNightDistribution),
    ("double click remains two touch samples inside one session", TestDoubleClickGestureSemantics),
    ("first touch cannot jump directly to warning or escape", TestFirstTouchBoundaryGate),
    ("continuous petting load expires after a quiet gap", TestPettingLoadQuietGap),
    ("pet speech blocks technical language and follows temperament", TestPetSpeechBoundary),
    ("retired window edge behaviors are no longer autonomous candidates", TestWindowEdgeEligibility),
    ("owner interactions use state-dependent participation probabilities", TestInteractionParticipation),
    ("autonomous magic is limited to one trigger per local day", TestDailyMagicLimit),
    ("daily toilet plan is random idempotent and skips offline slots", TestDailyToiletPlan),
    ("autonomous toilet requires a due schedule signal", TestToiletEligibility),
    ("blue bed rest favors sleepy safe states and stays settled", TestBedRestCandidate),
    ("self grooming remains a low-frequency quiet daily behavior", TestSelfGroomCadence),
    ("holiday outfits are enabled only on exact calendar dates", TestHolidayDateGates),
    ("profile names owner address and self-reference flow into pet speech", TestProfileSpeechIdentity),
    ("walk and autonomous routes are continuous nonzero and bounded", TestContinuousDesktopRoutes),
    ("broom route uses smooth paced eight-direction coverage", TestBroomRouteCoverage),
    ("model provider defaults normalize DeepSeek base endpoint", TestModelProviderDefaults),
    ("markdown pet system prompt round-trips without loss", TestPetSystemPromptMarkdown),
    ("album discovers dated folders and persists searchable descriptions", TestAlbumDiscoveryAndDescription),
    ("album experiences parse markdown json and block path escape", TestAlbumExperienceParsing),
    ("album experiences index old descriptions and retrieve by keyword date tag", TestAlbumExperienceIndexAndSearch),
    ("album experience model context is path-free bounded and image-limited", TestAlbumExperienceModelBoundary),
    ("album experience rule reply and behavior suggestion stay local and arbitrated", TestAlbumExperienceRuleAndArbitration),
    ("travel return can append a lightweight album experience", TestTravelExperienceAppend),
    ("model context removes private paths and bounds local memory", TestModelContextPrivacy),
    ("arbitration keeps mouse attention below protected actions", TestMouseAttentionArbitration),
    ("cage blocks ordinary behavior until forced release", TestCageArbitration),
    ("travel blocks ordinary behavior until return or recall", TestTravelArbitration),
    ("local commands work without a model API", TestLocalInteractionCommands),
    ("food and toy anchors are accepted cooled down or state-blocked", TestAnchorArbitration),
    ("owner-triggered magic interrupts ordinary behavior but respects hard states", TestOwnerForcedMagicArbitration),
    ("coin drag click and double click remain distinct", TestCoinPointerGestures),
    ("asset manifest keeps schema 1 and normalizes schema 2 action groups", TestAssetActionGroupCompatibility),
    ("V19 runtime pack has no inherited cat atlases and uses eight-phase pursuit", TestV19RuntimeAssetContract),
    ("ask-walk is an autonomous behavior instead of a preview-only alias", TestAskWalkBehavior),
    ("default persona round-trips without changing Pupu identity", TestDefaultPersonaCompatibility),
    ("rule PetAgent works without API and only returns candidates", TestRulePetAgent),
    ("behavior proposal executor always arbitrates before execution", TestBehaviorProposalExecutor),
    ("rejected proposal records reason and transient proposal may defer", TestBehaviorProposalRejectionAndDelay),
    ("all blocked autonomous candidates defer without throwing", TestAllBlockedAutonomyDefers),
    ("single arbitrator owns eligibility scoring admission and cooldown", TestUnifiedBehaviorArbitrator),
    ("committed selection returns one reusable admission", TestCommittedSelectionAdmission),
    ("failed proposal rolls back lease and cooldown", TestProposalAdmissionRollback),
    ("Agent presentation contract swaps sprite and skeletal adapters", TestPresentationAdapterBoundary),
    ("PetAgent kernel reads memory through a model-neutral port", TestPetAgentKernelMemoryPort),
    ("Agent decision snapshot cannot mutate live personality", TestDecisionSnapshotIsolation),
    ("runtime atlas grid contract accepts the v15 motion sheet", TestAssetGridContract)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}
return 0;

static Task TestAllBlockedAutonomyDefers()
{
    var selector = new BehaviorSelector(
        new BehaviorArbitrator(
            new BehaviorScorer(),
            new SeededRandomSource(1901)));
    var state = NewState();
    var now = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);
    var context = Context(now);
    context.CurrentBehaviorId = "owner.long_action";
    context.CurrentBehaviorStartedAt = now.AddSeconds(-3);
    context.CurrentBehaviorInterruptible = true;
    context.MinimumAutonomousDwell = TimeSpan.FromMinutes(3);
    context.Signals.Clear();

    var decision = selector.Select(
        BehaviorCatalog.Autonomous,
        state,
        context);

    Assert(decision.Deferred, "all-blocked selection did not defer");
    Assert(decision.SelectedBehaviorId == "owner.long_action",
        "deferred selection did not preserve the current action");
    Assert(decision.Candidates.Count == 0,
        "deferred selection unexpectedly invented an eligible candidate");
    Assert(selector.History.Count == 0,
        "deferred selection polluted behavior history");
    return Task.CompletedTask;
}

static Task TestUnifiedBehaviorArbitrator()
{
    var now = new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);
    var arbitrator = new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(1101));
    var context = Context(now);
    context.RequestSource = BehaviorRequestSource.Autonomous;
    context.Signals["mouse_nearby"] = 1;
    var definitions = new[]
    {
        BehaviorCatalog.Find("explore.mouse_track")!,
        BehaviorCatalog.Find("idle.prone_observe")!
    };
    var options = new BehaviorSelectionOptions
    {
        Source = BehaviorArbitrationSource.Autonomous,
        ActivePriority = BehaviorPriority.AutonomousMovement,
        PassivePriority = BehaviorPriority.DecorativeIdle,
        CommitAdmission = true
    };
    var first = arbitrator.SelectAutonomous(
        definitions,
        NewState(),
        context,
        new BehaviorArbitrationContext(),
        options);
    Assert(!first.Deferred && first.Candidates.Count == 2,
        "unified arbitrator did not score its eligible candidate set");
    Assert(arbitrator.History.Count == 1,
        "unified arbitrator did not own selection history");

    var selected = first.SelectedBehaviorId;
    var second = arbitrator.SelectAutonomous(
        definitions,
        NewState(),
        new BehaviorContext
        {
            Now = now.AddSeconds(1),
            RequestSource = BehaviorRequestSource.Autonomous,
            ContextKey = "general",
            LocationKey = "desktop",
            TimeBucket = "day",
            Signals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["mouse_nearby"] = 1
            }
        },
        new BehaviorArbitrationContext(),
        options);
    var selectedEligibility = second.Eligibility.Single(item =>
        item.BehaviorId == selected);
    Assert(
        selectedEligibility.Reasons.Contains("arbitration:request_cooldown"),
        "accepted selection cooldown was not enforced by the same arbitrator");
    return Task.CompletedTask;
}

static Task TestCommittedSelectionAdmission()
{
    var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);
    var arbitrator = new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(21));
    var definition = BehaviorCatalog.Find("idle.prone_observe")!;
    var decision = arbitrator.SelectAutonomous(
        new[] { definition },
        NewState(),
        Context(now),
        new BehaviorArbitrationContext(),
        new BehaviorSelectionOptions
        {
            CommitAdmission = true,
            CooldownOverride = TimeSpan.FromMinutes(1),
            CooldownKey = "test:one-admission"
        });
    Assert(
        !decision.Deferred &&
        decision.Admission?.Accepted == true &&
        arbitrator.CurrentLease?.BehaviorId == definition.BehaviorId,
        "selection did not return and commit its own admission");

    var duplicate = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = definition.BehaviorId,
            Source = BehaviorArbitrationSource.Autonomous,
            Priority = BehaviorPriority.DecorativeIdle,
            RequestedAt = now.AddSeconds(1),
            Cooldown = TimeSpan.FromMinutes(1),
            CooldownKey = "test:one-admission"
        },
        new BehaviorArbitrationContext());
    Assert(
        !duplicate.Accepted && duplicate.ReasonCode == "request_cooldown",
        "a caller could silently admit the same decision a second time");
    return Task.CompletedTask;
}

static async Task TestProposalAdmissionRollback()
{
    var now = new DateTimeOffset(2026, 7, 31, 10, 30, 0, TimeSpan.Zero);
    var arbitrator = new BehaviorArbitrator();
    arbitrator.ResetCurrent(now, "idle.side_lie");
    var queue = new BehaviorProposalQueue();
    var executor = new BehaviorProposalExecutor(queue, arbitrator);
    BehaviorProposal Proposal(DateTimeOffset at) => new()
    {
        BehaviorId = "play.wand",
        Source = BehaviorArbitrationSource.PanelCommand,
        Priority = BehaviorPriority.ExplicitCommand,
        CreatedAt = at,
        ExpiresAt = at.AddMinutes(1),
        Cooldown = TimeSpan.FromMinutes(2),
        CooldownKey = "test:proposal-rollback"
    };

    queue.Enqueue(Proposal(now));
    var failed = await executor.ProcessNextAsync(
        now,
        new BehaviorArbitrationContext(),
        _ => Task.CompletedTask,
        (_, _) => Task.FromResult(false));
    Assert(
        failed.Record?.State == BehaviorProposalState.Failed &&
        arbitrator.CurrentLease?.BehaviorId == "idle.side_lie",
        "failed adapter execution left the accepted proposal lease active");

    queue.Enqueue(Proposal(now.AddSeconds(1)));
    var retried = await executor.ProcessNextAsync(
        now.AddSeconds(1),
        new BehaviorArbitrationContext(),
        _ => Task.CompletedTask,
        (_, _) => Task.FromResult(true));
    Assert(
        retried.Executed && retried.Arbitration?.Accepted == true,
        "failed adapter execution left a stale request cooldown");
}

static Task TestPresentationAdapterBoundary()
{
    var intent = new BehaviorPresentationIntent
    {
        BehaviorId = "idle.side_lie",
        Phase = BehaviorPresentationPhase.Loop,
        Motion = BehaviorMotionKind.Stationary,
        Loop = true
    };
    IBehaviorPresentationResolver<string> sprite =
        new DictionaryBehaviorPresentationResolver<string>(
            "sprite",
            new Dictionary<string, string>
            {
                ["idle.side_lie"] = "atlas:motion:9"
            },
            "atlas:fallback");
    IBehaviorPresentationResolver<string> skeletal =
        new DictionaryBehaviorPresentationResolver<string>(
            "skeletal-2d",
            new Dictionary<string, string>
            {
                ["idle.side_lie"] = "state-machine:side-lie"
            },
            "state-machine:idle");
    Assert(
        sprite.TryResolve(intent, out var spriteResult) &&
        skeletal.TryResolve(intent, out var skeletalResult) &&
        spriteResult!.Presentation != skeletalResult!.Presentation &&
        spriteResult.Intent == skeletalResult.Intent,
        "presentation technology leaked into Agent behavior semantics");
    return Task.CompletedTask;
}

static Task TestPetAgentKernelMemoryPort()
{
    var memory = new TestAgentMemoryPort();
    var kernel = new PetAgentKernel(
        memory,
        memory,
        new BehaviorArbitrator(
            new BehaviorScorer(),
            new SeededRandomSource(77)),
        new EchoMemoryAgent());
    var result = kernel.Handle(
        new PetAgentEvent
        {
            Kind = PetAgentEventKind.UserChat,
            Text = "你还记得吗"
        },
        new PetAgentContext
        {
            LongTermMemorySummaries = new[] { "界面提供的记忆" }
        });
    Assert(
        result.Debug.Contains("界面提供的记忆") &&
        result.Debug.Contains("昨天一起玩过逗猫棒") &&
        result.Debug.Contains("主人喜欢轻轻摸头"),
        "PetAgent kernel bypassed or lost the model-neutral memory port");
    return Task.CompletedTask;
}

static Task TestDecisionSnapshotIsolation()
{
    var port = new TestAgentMemoryPort();
    var snapshot = port.ReadDecisionState();
    snapshot.Temperament.Playful = 0;
    snapshot.Runtime.Stress = 1;
    snapshot.Relationship.Trust = 0;
    Assert(
        port.Personality.Temperament.Playful > 0 &&
        port.Personality.Runtime.Stress < 1 &&
        port.Personality.Relationship.Trust > 0,
        "decision code received the live mutable personality object");
    return Task.CompletedTask;
}

static Task TestAssetGridContract()
{
    Assert(AssetGridContract.RequiredColumns == 8,
        "runtime atlas column contract changed unexpectedly");
    Assert(AssetGridContract.MinimumRows["motion"] == 10,
        "runtime motion contract regressed to the obsolete 11-row sheet");
    Assert(AssetGridContract.MinimumRows.Count == 13,
        "runtime atlas contract lost or invented a required sheet");
    return Task.CompletedTask;
}

static Task TestAssetActionGroupCompatibility()
{
    const string legacyJson =
        """
        {
          "schemaVersion": 1,
          "name": "legacy 1.6.0",
          "version": "1.6.0",
          "cellSize": 256,
          "atlases": {
            "core": { "file": "core.png", "columns": 8, "rows": 6 }
          },
          "coinStates": {
            "normalColor": { "atlas": "core", "row": 0, "frames": [0] }
          }
        }
        """;
    var legacy = JsonSerializer.Deserialize<AssetPackManifest>(
        legacyJson,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    Assert(legacy is { SchemaVersion: 1 } &&
           legacy.Atlases["core"].Rows == 6 &&
           legacy.ActionGroups.Count == 0,
        "schema 1 atlas manifest no longer deserializes");
    Assert(legacy!.CoinStates.ContainsKey("normalColor"),
        "optional coinStates no longer round-trip");

    var incomplete = new AssetActionGroupDefinition
    {
        Source = new AssetActionSourceDefinition
        {
            Atlas = "core",
            Row = 1
        },
        LoopMode = "unknown",
        FrameDurationMs = 0,
        Fallback = "idle"
    };
    incomplete.Normalize("play.roll", 256, sourceFrameCapacity: 8);
    Assert(incomplete.GroupId == "play.roll" &&
           incomplete.BehaviorId == "play.roll",
        "missing schema 2 ids did not receive safe defaults");
    Assert(incomplete.Frames.SequenceEqual(Enumerable.Range(0, 8)) &&
           incomplete.FrameDurationsMs.Count == 8 &&
           incomplete.FrameDurationsMs.All(value => value >= 40),
        "missing frame fields did not receive safe fallbacks");
    Assert(incomplete.LoopMode == AssetLoopModes.Loop &&
           incomplete.Fallback == "idle",
        "loop or fallback normalization failed");

    var strip = new AssetActionGroupDefinition
    {
        GroupId = "new-strip",
        BehaviorId = "play.new",
        Source = new AssetActionSourceDefinition
        {
            Type = AssetActionSourceKinds.SpriteStrip,
            File = "play-new.png",
            FrameWidth = 256,
            FrameHeight = 256,
            Columns = 4
        },
        FrameCount = 4,
        Frames = new List<int> { 0, 1, 2, 3 },
        FrameDurationMs = 180,
        LoopMode = AssetLoopModes.Once,
        TriggerConditions = new List<string> { "owner anchor accepted" }
    };
    strip.Normalize(strip.GroupId, 256, sourceFrameCapacity: 4);
    Assert(strip.Source.Type == AssetActionSourceKinds.SpriteStrip &&
           strip.FrameCount == 4 &&
           !strip.IsLooping &&
           strip.TriggerConditions.SequenceEqual(new[] { "owner anchor accepted" }),
        "independent action file source did not normalize");

    var pingPong = new AssetActionGroupDefinition
    {
        GroupId = "closed-gait",
        BehaviorId = "anchor.closed_gait",
        Source = new AssetActionSourceDefinition
        {
            Type = AssetActionSourceKinds.SpriteStrip,
            File = "gait.png",
            FrameWidth = 256,
            FrameHeight = 256,
            Columns = 4
        },
        FrameCount = 4,
        Frames = new List<int> { 0, 1, 2, 3 },
        FrameDurationMs = 165,
        LoopMode = AssetLoopModes.PingPong
    };
    pingPong.Normalize(pingPong.GroupId, 256, sourceFrameCapacity: 4);
    Assert(
        pingPong.Frames.SequenceEqual(new[] { 0, 1, 2, 3, 2, 1 }) &&
        pingPong.FrameDurationsMs.Count == 6 &&
        pingPong.IsLooping,
        "ping-pong gait did not close without a last-to-first pose snap");
    return Task.CompletedTask;
}

static Task TestDefaultPersonaCompatibility()
{
    var profile = new PetProfile();
    profile.Normalize();
    Assert(profile.Persona.Id == "pupu.default" &&
           profile.Persona.DisplayName == "朴朴" &&
           Math.Abs(profile.Persona.DefaultTemperament.Playful - 0.82) < 1e-12,
        "default Persona changed existing Pupu defaults");
    var clone = profile.Clone();
    clone.Persona.BehaviorBias["play"] = 0.1;
    Assert(Math.Abs(profile.Persona.BehaviorBias["play"] - 0.82) < 1e-12,
        "Persona clone shared mutable behavior preferences");
    return Task.CompletedTask;
}

static Task TestRulePetAgent()
{
    var agent = new RulePetAgent(PersonaDefinition.CreateDefaultPupu());
    var result = agent.Handle(
        new PetAgentEvent
        {
            Kind = PetAgentEventKind.AlbumExperienceHit,
            At = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(8)),
            Text = "生日照片",
            BehaviorHint = "celebrate.idle"
        },
        new PetAgentContext
        {
            CurrentStateSummary = "calm",
            AlbumExperienceSummaries = new[] { "主人陪朴朴过生日" }
        });
    Assert(result.ReplyText.Contains("主人陪朴朴过生日", StringComparison.Ordinal),
        "rule PetAgent did not return an API-free memory reply");
    Assert(result.BehaviorProposals.Count == 1 &&
           result.BehaviorProposals[0].Source == BehaviorArbitrationSource.MemoryRecall,
        "rule PetAgent did not produce a structured local proposal");
    Assert(result.MemoryCandidates.Count == 0 &&
           result.Debug.Contains("backend=local-rules"),
        "rule PetAgent wrote memory or failed to report local backend");
    var chat = agent.Handle(
        new PetAgentEvent
        {
            Kind = PetAgentEventKind.UserChat,
            Text = "朴朴你好可爱"
        },
        new PetAgentContext());
    Assert(chat.ReplyText.Contains("眼光不错", StringComparison.Ordinal) &&
           !chat.ReplyText.Contains("动作做完整", StringComparison.Ordinal) &&
           !chat.ReplyText.Contains("认真回答", StringComparison.Ordinal),
        "local chat did not use the short energetic one-year-old Pupu persona");
    return Task.CompletedTask;
}

static async Task TestBehaviorProposalExecutor()
{
    var now = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(8));
    var queue = new BehaviorProposalQueue();
    queue.Enqueue(new BehaviorProposal
    {
        BehaviorId = "play.wand",
        Source = BehaviorArbitrationSource.MemoryRecall,
        Priority = BehaviorPriority.MemoryRecall,
        CreatedAt = now,
        ExpiresAt = now.AddSeconds(20),
        Reason = "test"
    });
    var order = new List<string>();
    var executor = new BehaviorProposalExecutor(queue, new BehaviorArbitrator());
    var result = await executor.ProcessNextAsync(
        now,
        new BehaviorArbitrationContext(),
        arbitration =>
        {
            order.Add($"arbitration:{arbitration.Accepted}");
            return Task.CompletedTask;
        },
        (proposal, _) =>
        {
            order.Add($"execute:{proposal.BehaviorId}");
            return Task.FromResult(true);
        });
    Assert(result.Executed &&
           order.SequenceEqual(new[] { "arbitration:True", "execute:play.wand" }),
        "proposal executed before arbitration or did not execute");
    Assert(queue.History().Single().State == BehaviorProposalState.Completed,
        "accepted proposal did not enter completed history");
}

static async Task TestBehaviorProposalRejectionAndDelay()
{
    var now = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(8));
    var queue = new BehaviorProposalQueue();
    queue.Enqueue(new BehaviorProposal
    {
        BehaviorId = "feed.snack",
        Source = BehaviorArbitrationSource.MemoryRecall,
        Priority = BehaviorPriority.MemoryRecall,
        CreatedAt = now,
        ExpiresAt = now.AddSeconds(20),
        AllowDelay = true,
        ForbiddenStates = BehaviorStateBlockers.Magic,
        Reason = "memory"
    });
    var executed = false;
    var executor = new BehaviorProposalExecutor(queue, new BehaviorArbitrator());
    var rejected = await executor.ProcessNextAsync(
        now,
        new BehaviorArbitrationContext
        {
            ActiveStates = BehaviorStateBlockers.Magic
        },
        _ => Task.CompletedTask,
        (_, _) =>
        {
            executed = true;
            return Task.FromResult(true);
        });
    Assert(!executed &&
           rejected.Record?.State == BehaviorProposalState.Rejected &&
           rejected.Record.ResultCode == "state_forbidden",
        "state-blocked proposal executed or lost its rejection reason");

    queue.Enqueue(new BehaviorProposal
    {
        BehaviorId = "rest.window",
        Source = BehaviorArbitrationSource.MemoryRecall,
        Priority = BehaviorPriority.MemoryRecall,
        CreatedAt = now,
        ExpiresAt = now.AddSeconds(20),
        AllowDelay = true,
        Reason = "memory"
    });
    var delayed = await executor.ProcessNextAsync(
        now,
        new BehaviorArbitrationContext
        {
            CurrentBehaviorId = "owner.cage",
            CurrentPriority = BehaviorPriority.OwnerForced,
            CurrentInterruptible = false
        },
        _ => Task.CompletedTask,
        (_, _) => Task.FromResult(true));
    Assert(delayed.Record?.State == BehaviorProposalState.Deferred &&
           queue.Snapshot().Any(item => item.Proposal.BehaviorId == "rest.window"),
        "transiently blocked proposal was not deferred");
}

static Task TestMouseAttentionArbitration()
{
    var now = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(8));
    foreach (var (behavior, state) in new[]
             {
                 ("walk.free", BehaviorStateBlockers.Movement),
                 ("magic.scourgify", BehaviorStateBlockers.Magic),
                 ("rest.sleep.side", BehaviorStateBlockers.Sleeping),
                 ("touch.enjoy", BehaviorStateBlockers.TouchReaction)
             })
    {
        var result = new BehaviorArbitrator().Evaluate(
            new BehaviorArbitrationRequest
            {
                BehaviorId = "attention.mouse",
                Source = BehaviorArbitrationSource.MouseAttention,
                Priority = BehaviorPriority.MouseAttention,
                RequestedAt = now,
                ObservationOnly = true,
                ForbiddenStates =
                    BehaviorStateBlockers.Movement |
                    BehaviorStateBlockers.Magic |
                    BehaviorStateBlockers.Sleeping |
                    BehaviorStateBlockers.TouchReaction
            },
            new BehaviorArbitrationContext
            {
                CurrentBehaviorId = behavior,
                CurrentPriority = BehaviorPriority.ContinuousEffect,
                CurrentStartedAt = now.AddSeconds(-1),
                CurrentMinimumDuration = TimeSpan.FromSeconds(8),
                CurrentInterruptible = false,
                ActiveStates = state
            });
        Assert(!result.Accepted && result.ReasonCode == "state_forbidden",
            $"mouse attention unexpectedly interrupted {behavior}");
    }

    var lowerPriority = new BehaviorArbitrator().Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "attention.mouse",
            Source = BehaviorArbitrationSource.MouseAttention,
            Priority = BehaviorPriority.MouseAttention,
            RequestedAt = now
        },
        new BehaviorArbitrationContext
        {
            CurrentBehaviorId = "magic.scourgify",
            CurrentPriority = BehaviorPriority.ContinuousEffect,
            CurrentStartedAt = now.AddMinutes(-1),
            CurrentInterruptible = true
        });
    Assert(!lowerPriority.Accepted && lowerPriority.ReasonCode == "lower_priority",
        "low-priority mouse request bypassed current behavior priority");

    var protectedDwell = new BehaviorArbitrator().Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "command.quiet",
            Source = BehaviorArbitrationSource.DialogueCommand,
            Priority = BehaviorPriority.ExplicitCommand,
            RequestedAt = now
        },
        new BehaviorArbitrationContext
        {
            CurrentBehaviorId = "touch.enjoy",
            CurrentPriority = BehaviorPriority.TouchFeedback,
            CurrentStartedAt = now.AddSeconds(-1),
            CurrentMinimumDuration = TimeSpan.FromSeconds(4),
            CurrentInterruptible = true
        });
    Assert(!protectedDwell.Accepted && protectedDwell.ReasonCode == "minimum_duration",
        "higher-priority command bypassed the current action minimum duration");
    return Task.CompletedTask;
}

static Task TestCageArbitration()
{
    var now = DateTimeOffset.Now;
    var arbitrator = new BehaviorArbitrator();
    var caged = new BehaviorArbitrationContext
    {
        CurrentBehaviorId = "owner.cage",
        CurrentPriority = BehaviorPriority.OwnerForced,
        CurrentStartedAt = now,
        CurrentInterruptible = false,
        ActiveStates = BehaviorStateBlockers.Caged
    };
    var move = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "explore.short_walk",
            Source = BehaviorArbitrationSource.Autonomous,
            Priority = BehaviorPriority.AutonomousMovement,
            RequestedAt = now,
            ForbiddenStates = BehaviorStateBlockers.Caged
        },
        caged);
    Assert(!move.Accepted && move.ReasonCode == "state_forbidden",
        "caged pet accepted autonomous movement");

    var release = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "owner.cage.release",
            Source = BehaviorArbitrationSource.OwnerForced,
            Priority = BehaviorPriority.OwnerForced,
            RequestedAt = now.AddSeconds(1),
            ForceInterrupt = true
        },
        caged);
    Assert(release.Accepted, "forced cage release was rejected");
    arbitrator.ResetCurrent(
        now.AddSeconds(1),
        "idle.side_lie",
        TimeSpan.Zero);

    var afterRelease = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "explore.short_walk",
            Source = BehaviorArbitrationSource.Autonomous,
            Priority = BehaviorPriority.AutonomousMovement,
            RequestedAt = now.AddSeconds(2),
            ForbiddenStates = BehaviorStateBlockers.Caged
        },
        new BehaviorArbitrationContext
        {
            CurrentBehaviorId = "idle.side_lie",
            CurrentStartedAt = now.AddMinutes(-3),
            CurrentInterruptible = true
        });
    Assert(afterRelease.Accepted, "ordinary behavior did not recover after release");
    return Task.CompletedTask;
}

static Task TestTravelArbitration()
{
    var now = DateTimeOffset.Now;
    var arbitrator = new BehaviorArbitrator();
    var away = new BehaviorArbitrationContext
    {
        CurrentBehaviorId = "travel.away",
        CurrentPriority = BehaviorPriority.OwnerForced,
        CurrentStartedAt = now,
        CurrentInterruptible = false,
        ActiveStates = BehaviorStateBlockers.Traveling
    };
    var ordinary = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "play.roll",
            Source = BehaviorArbitrationSource.PanelCommand,
            Priority = BehaviorPriority.ExplicitCommand,
            RequestedAt = now,
            ForbiddenStates = BehaviorStateBlockers.Traveling
        },
        away);
    Assert(!ordinary.Accepted, "traveling pet accepted ordinary play");

    foreach (var behaviorId in new[] { "travel.return", "travel.recall" })
    {
        var result = arbitrator.Evaluate(
            new BehaviorArbitrationRequest
            {
                BehaviorId = behaviorId,
                Source = BehaviorArbitrationSource.OwnerForced,
                Priority = BehaviorPriority.OwnerForced,
                RequestedAt = now.AddMinutes(1),
                ForceInterrupt = true
            },
            away);
        Assert(result.Accepted, $"{behaviorId} was rejected while traveling");
    }
    return Task.CompletedTask;
}

static Task TestLocalInteractionCommands()
{
    var parser = new LocalInteractionCommandParser();
    Assert(parser.Parse("安静一会").Intent == LocalInteractionIntent.QuietForAWhile,
        "quiet command was not recognized");
    Assert(parser.Parse("自己玩吧").Intent == LocalInteractionIntent.AllowSelfPlay,
        "self-play command was not recognized");
    Assert(parser.Parse("来吃一下").Intent == LocalInteractionIntent.FoodAnchor,
        "food anchor command was not recognized");
    Assert(parser.Parse("陪我玩").Intent == LocalInteractionIntent.ToyAnchor,
        "toy anchor command was not recognized");
    Assert(parser.Parse("先关起来").Intent == LocalInteractionIntent.Cage,
        "cage command was not recognized");
    Assert(parser.Parse("放出来").Intent == LocalInteractionIntent.ReleaseCage,
        "release command was not recognized");
    var travel = parser.Parse("去东京旅游2小时");
    Assert(travel.Intent == LocalInteractionIntent.Travel &&
           travel.Destination == "东京" &&
           travel.Duration == TimeSpan.FromHours(2),
        "travel destination or duration was not recognized locally");
    Assert(parser.Parse("叫回来").Intent == LocalInteractionIntent.RecallTravel,
        "travel recall command was not recognized");
    Assert(parser.Parse("给我看看旅行照片").Intent == LocalInteractionIntent.None,
        "ordinary travel conversation was mistaken for a state command");
    return Task.CompletedTask;
}

static Task TestAnchorArbitration()
{
    var now = DateTimeOffset.Now;
    var arbitrator = new BehaviorArbitrator();
    var idle = new BehaviorArbitrationContext
    {
        CurrentBehaviorId = "idle.prone_observe",
        CurrentPriority = BehaviorPriority.DecorativeIdle,
        CurrentStartedAt = now.AddMinutes(-3),
        CurrentInterruptible = true
    };
    var request = new BehaviorArbitrationRequest
    {
        BehaviorId = "anchor.food.approach",
        Source = BehaviorArbitrationSource.OwnerAnchor,
        Priority = BehaviorPriority.OwnerAnchor,
        RequestedAt = now,
        Cooldown = TimeSpan.FromSeconds(5),
        CooldownKey = "anchor.food",
        ForbiddenStates =
            BehaviorStateBlockers.Magic |
            BehaviorStateBlockers.Movement |
            BehaviorStateBlockers.Feeding
    };
    Assert(arbitrator.Evaluate(request, idle).Accepted, "food anchor was not accepted from idle");
    var cooledDown = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = request.BehaviorId,
            Source = request.Source,
            Priority = request.Priority,
            RequestedAt = now.AddSeconds(1),
            Cooldown = request.Cooldown,
            CooldownKey = request.CooldownKey
        },
        idle);
    Assert(!cooledDown.Accepted && cooledDown.ReasonCode == "request_cooldown",
        "food anchor cooldown was not enforced");

    var toyBlocked = arbitrator.Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = "anchor.toy.approach",
            Source = BehaviorArbitrationSource.OwnerAnchor,
            Priority = BehaviorPriority.OwnerAnchor,
            RequestedAt = now,
            ForbiddenStates = BehaviorStateBlockers.Magic
        },
        new BehaviorArbitrationContext
        {
            CurrentBehaviorId = "magic.accio_broom",
            CurrentPriority = BehaviorPriority.ContinuousEffect,
            CurrentStartedAt = now,
            CurrentInterruptible = false,
            ActiveStates = BehaviorStateBlockers.Magic
        });
    Assert(!toyBlocked.Accepted, "toy anchor ignored a protected magic state");

    var anchor = new InteractionAnchor(InteractionAnchorKind.Food, 420, 360, now);
    Assert(anchor.Kind == InteractionAnchorKind.Food && anchor.X == 420 && anchor.Y == 360,
        "food anchor target was not generated");
    return Task.CompletedTask;
}

static Task TestOwnerForcedMagicArbitration()
{
    var now = DateTimeOffset.Now;
    var arbitrator = new BehaviorArbitrator();
    var request = new BehaviorArbitrationRequest
    {
        BehaviorId = "magic.accio_broom",
        Source = BehaviorArbitrationSource.OwnerForced,
        Priority = BehaviorPriority.OwnerForced,
        RequestedAt = now,
        ForceInterrupt = true,
        Interruptible = false,
        ForbiddenStates =
            BehaviorStateBlockers.Caged |
            BehaviorStateBlockers.Traveling |
            BehaviorStateBlockers.Petrified
    };
    var ordinary = new BehaviorArbitrationContext
    {
        CurrentBehaviorId = "play.wand",
        CurrentPriority = BehaviorPriority.ExplicitCommand,
        CurrentStartedAt = now.AddSeconds(-1),
        CurrentMinimumDuration = TimeSpan.FromMinutes(1),
        CurrentInterruptible = false,
        ActiveStates = BehaviorStateBlockers.Playing
    };
    Assert(arbitrator.Evaluate(request, ordinary).Accepted,
        "owner-forced magic did not interrupt ordinary protected play");

    var blocked = new BehaviorArbitrationContext
    {
        CurrentBehaviorId = "owner.cage",
        CurrentPriority = BehaviorPriority.OwnerForced,
        CurrentStartedAt = now,
        CurrentInterruptible = false,
        ActiveStates = BehaviorStateBlockers.Caged
    };
    var blockedRequest = new BehaviorArbitrationRequest
    {
        BehaviorId = request.BehaviorId,
        Source = request.Source,
        Priority = request.Priority,
        RequestedAt = now.AddSeconds(1),
        ForceInterrupt = request.ForceInterrupt,
        Interruptible = request.Interruptible,
        ForbiddenStates = request.ForbiddenStates
    };
    var result = new BehaviorArbitrator().Evaluate(blockedRequest, blocked);
    Assert(!result.Accepted && result.ReasonCode == "state_forbidden",
        "owner-forced magic bypassed the cage hard-state gate");
    return Task.CompletedTask;
}

static Task TestCoinPointerGestures()
{
    Assert(CoinPointerGestureClassifier.Classify(dragged: true, clickCount: 2) ==
           CoinPointerAction.None,
        "dragged coin still produced a click action");
    Assert(CoinPointerGestureClassifier.Classify(dragged: false, clickCount: 1) ==
           CoinPointerAction.RefreshColor,
        "single coin click did not refresh color");
    Assert(CoinPointerGestureClassifier.Classify(dragged: false, clickCount: 2) ==
           CoinPointerAction.Flip,
        "double coin click did not flip");
    return Task.CompletedTask;
}

static Task TestAskWalkBehavior()
{
    var definition = BehaviorCatalog.Find("social.ask_walk");
    Assert(definition is not null, "social.ask_walk is missing from the behavior catalog");
    Assert(definition!.IsOwnerInitiative, "ask-walk must remain a bounded owner initiative");
    Assert(!definition.RequiresMovement, "showing the leash must not move the desktop window");
    Assert(definition.Cooldown >= TimeSpan.FromMinutes(20),
        "ask-walk can repeat too frequently");
    return Task.CompletedTask;
}

static Task TestV19RuntimeAssetContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var path = Path.Combine(root, "Pupu.Desktop", "Assets", "pupu-assets.json");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var manifest = document.RootElement;
    Assert(manifest.GetProperty("version").GetString()!.Contains("v19", StringComparison.OrdinalIgnoreCase),
        "runtime manifest is not V19");
    foreach (var atlas in manifest.GetProperty("atlases").EnumerateObject())
    {
        var file = atlas.Value.GetProperty("file").GetString() ?? string.Empty;
        Assert(file.Contains("v19", StringComparison.OrdinalIgnoreCase),
            $"cat atlas {atlas.Name} still references an inherited file: {file}");
    }
    foreach (var id in new[]
             {
                 "laser-chase-8",
                 "snack-chase-8",
                 "magic-accio-broom-flight-8dir"
             })
    {
        var group = manifest.GetProperty("actionGroups").GetProperty(id);
        Assert(group.GetProperty("frameCount").GetInt32() == 64,
            $"{id} is not eight directions times eight phases");
        foreach (var direction in group.GetProperty("directions").EnumerateObject())
            Assert(direction.Value.GetProperty("frames").GetArrayLength() == 8,
                $"{id}/{direction.Name} does not expose eight phases");
    }
    var askWalk = manifest.GetProperty("actionGroups").GetProperty("ask-walk");
    Assert(askWalk.GetProperty("behaviorId").GetString() == "social.ask_walk",
        "ask-walk still aliases another behavior");
    var cage = manifest.GetProperty("actionGroups").GetProperty("cage-rest-12");
    Assert(cage.GetProperty("source").GetProperty("file").GetString() ==
           "Actions/pupu-cage-rest-youthful-v19.png",
        "cage rest does not use the V19 closed-carrier strip");
    var coinStates = manifest.GetProperty("coinStates");
    Assert(coinStates.TryGetProperty("normalEdge", out _) &&
           coinStates.TryGetProperty("backEdge", out _),
        "coin flip is missing real front/back edge frames");
    return Task.CompletedTask;
}

static Task TestModelContextPrivacy()
{
    var filter = new ModelContextPrivacyFilter();
    var privateContext =
        """
        今天在 C:\Users\yasha\Pictures\Pupu\trip.jpg 看到了旧照片。
        索引来自 /Users/yasha/Pictures/Pupu/album.json。
        临时文件是 file:///workspace/scratch/private/events.md。
        这段可保留：朴朴第一次坐车时很安静。
        """;
    var filtered = filter.Prepare(privateContext);
    Assert(!filtered.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase),
        "Windows private path leaked into model context");
    Assert(!filtered.Contains("/Users/yasha", StringComparison.OrdinalIgnoreCase),
        "macOS private path leaked into model context");
    Assert(!filtered.Contains("file://", StringComparison.OrdinalIgnoreCase),
        "file URI leaked into model context");
    Assert(filtered.Contains(ModelContextPrivacyFilter.PrivatePathPlaceholder),
        "path removal was not made explicit");
    Assert(filtered.Contains("朴朴第一次坐车时很安静", StringComparison.Ordinal),
        "non-private memory text was removed");

    var longContext = string.Concat(Enumerable.Repeat("一段主人可读的长期记忆。", 1000));
    var bounded = filter.Prepare(longContext, 320);
    Assert(bounded.Length < 360, $"bounded context is unexpectedly long: {bounded.Length}");
    Assert(bounded.EndsWith("【其余本地记忆已省略】", StringComparison.Ordinal),
        "truncated context lacks an explicit omission marker");
    return Task.CompletedTask;
}

static Task TestPlayfulDistribution()
{
    var high = NewState();
    high.Temperament.Playful = 0.95;
    high.Runtime.PlayDesire = 0.82;
    high.Runtime.Curiosity = 0.78;
    high.Runtime.Fatigue = 0.12;
    var low = CloneForDistribution(high);
    low.Temperament.Playful = 0.05;
    var target = new Func<string, bool>(x =>
        x.StartsWith("play.", StringComparison.Ordinal) ||
        x.StartsWith("explore.", StringComparison.Ordinal));
    var highCount = RunDistribution(high, 7101, 800, target);
    var lowCount = RunDistribution(low, 7101, 800, target);
    Assert(highCount >= lowCount + 120,
        $"expected high playful to exceed low by >=120, got {highCount} vs {lowCount}");
    return Task.CompletedTask;
}

static Task TestAffectionateInitiative()
{
    var high = NewState();
    high.Temperament.Affectionate = 0.95;
    high.Temperament.Independent = 0.10;
    high.Runtime.SocialDesire = 0.90;
    high.Relationship.Trust = 0.82;
    high.Relationship.InitiativeAcceptance = 0.82;
    var low = CloneForDistribution(high);
    low.Temperament.Affectionate = 0.05;
    low.Temperament.Independent = 0.75;
    var social = new Func<string, bool>(x =>
        x is "social.approach" or "social.purr" or "social.knead" or "social.ask_attention");
    var highCount = RunDistribution(high, 991, 600, social);
    var lowCount = RunDistribution(low, 991, 600, social);
    Assert(highCount > lowCount + 80, $"expected more social initiative, got {highCount} vs {lowCount}");

    var selector = new BehaviorSelector(new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(42)));
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    for (var i = 0; i < 60; i++)
    {
        var decision = selector.Select(
            BehaviorCatalog.Autonomous,
            high,
            Context(clock.Now, userResponded: false));
        var selected = BehaviorCatalog.Find(decision.SelectedBehaviorId)!;
        Assert(!selected.IsOwnerInitiative,
            $"unanswered initiative repeated as {decision.SelectedBehaviorId}");
        clock.Advance(TimeSpan.FromMinutes(2));
    }
    return Task.CompletedTask;
}

static Task TestSensitiveRapidTap()
{
    var updater = new GestureStateUpdater();
    var at = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    var rapid = new GestureEvent
    {
        Kind = GestureKind.RapidTap,
        At = at,
        ClicksPerSecond = 5.5,
        RecentTapCount = 6
    };
    var high = NewState();
    high.Temperament.Sensitive = 0.95;
    high.Relationship.Trust = 0.20;
    high.Relationship.TouchAcceptance = 0.25;
    var low = CloneForDistribution(high);
    low.Temperament.Sensitive = 0.05;
    var trusted = CloneForDistribution(high);
    trusted.Relationship.Trust = 0.95;
    trusted.Relationship.TouchAcceptance = 0.90;
    var baseline = high.Runtime.Stress;
    updater.Apply(high, rapid);
    updater.Apply(low, rapid);
    updater.Apply(trusted, rapid);
    var highDelta = high.Runtime.Stress - baseline;
    var lowDelta = low.Runtime.Stress - baseline;
    var trustedDelta = trusted.Runtime.Stress - baseline;
    Assert(highDelta > lowDelta + 0.06, $"sensitivity delta too small: {highDelta:0.000} vs {lowDelta:0.000}");
    Assert(trustedDelta < highDelta && trustedDelta >= 0.025,
        $"trust buffer must be finite: trusted={trustedDelta:0.000}, high={highDelta:0.000}");
    Assert(updater.BoundedRapidTapTolerance(trusted) <= 10, "trust created unbounded tolerance");
    return Task.CompletedTask;
}

static Task TestIndependentDistribution()
{
    var high = NewState();
    high.Temperament.Independent = 0.95;
    high.Temperament.Affectionate = 0.10;
    high.Runtime.Curiosity = 0.75;
    var low = CloneForDistribution(high);
    low.Temperament.Independent = 0.05;
    low.Temperament.Affectionate = 0.75;
    var solo = new Func<string, bool>(x =>
        x is "independent.patrol" or "self.groom" or "rest.far");
    var trustBefore = high.Relationship.Trust;
    var highCount = RunDistribution(high, 414, 650, solo);
    var lowCount = RunDistribution(low, 414, 650, solo);
    Assert(highCount > lowCount + 90, $"expected more solo actions, got {highCount} vs {lowCount}");
    Assert(Math.Abs(high.Relationship.Trust - trustBefore) < 1e-12, "independence changed trust");
    Assert(!BehaviorCatalog.TouchResponses
        .Where(x => x.BehaviorId == "touch.run_away")
        .Any(x => x.TemperamentAffinity.ContainsKey(TemperamentDimension.Independent)),
        "independence is still coupled to run-away duration/anger");
    return Task.CompletedTask;
}

static Task TestMischiefWithoutIgnore()
{
    var high = NewState();
    high.Temperament.Mischievous = 0.98;
    high.Temperament.Playful = 0.75;
    high.Runtime.PlayDesire = 0.82;
    high.Runtime.Curiosity = 0.80;
    high.Runtime.Stress = 0.04;
    high.Runtime.Fatigue = 0.08;
    var low = CloneForDistribution(high);
    low.Temperament.Mischievous = 0.02;
    var mischievous = new Func<string, bool>(x => x.StartsWith("mischief.", StringComparison.Ordinal));
    var highCount = RunDistribution(high, 820, 700, mischievous);
    var lowCount = RunDistribution(low, 820, 700, mischievous);
    Assert(highCount > 0, "high mischievous produced no natural mischief");
    Assert(highCount > lowCount + 70, $"mischief distribution did not change: {highCount} vs {lowCount}");
    Assert(typeof(BehaviorContext).GetProperty("IgnoredFor") is null,
        "mischief still exposes ignored-owner prerequisite");
    return Task.CompletedTask;
}

static Task TestContextOverrides()
{
    var state = NewState();
    state.Temperament.Playful = 1;
    state.Temperament.Mischievous = 1;
    state.Runtime.PlayDesire = 1;
    state.Runtime.Curiosity = 1;
    state.Runtime.Fatigue = 0.98;
    state.Runtime.Stress = 0.88;
    state.Runtime.Safety = 0.18;
    var selector = new BehaviorSelector(new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(73)));
    for (var i = 0; i < 80; i++)
    {
        var context = Context(
            new DateTimeOffset(2026, 7, 23, 1, 0, 0, TimeSpan.Zero),
            doNotDisturb: true,
            deepNight: true);
        var decision = selector.Select(BehaviorCatalog.Autonomous, state, context);
        var chosen = BehaviorCatalog.Find(decision.SelectedBehaviorId)!;
        Assert(!chosen.IsHighDisruption, $"quiet context selected high disruption {chosen.BehaviorId}");
    }
    return Task.CompletedTask;
}

static Task TestBaselineInvariant()
{
    var state = NewState();
    var before = Snapshot(state.Temperament);
    new GestureStateUpdater().Apply(state, new GestureEvent
    {
        Kind = GestureKind.RapidTap,
        At = DateTimeOffset.Now,
        ClicksPerSecond = 6,
        RecentTapCount = 7
    });
    new RelationshipUpdater().Apply(state, DateTimeOffset.Now, trust: 0.01, touchAcceptance: -0.01);
    new PreferenceLearningEngine().Observe(
        state, "touch.warning", "touch", "rapid", -0.8, DateTimeOffset.Now);
    Assert(Snapshot(state.Temperament) == before, "ordinary interaction mutated temperament");
    return Task.CompletedTask;
}

static Task TestHabitEvidenceWindow()
{
    var state = NewState();
    var learning = new PreferenceLearningEngine();
    var start = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    for (var i = 0; i < 8; i++)
        learning.Observe(state, "care.groom", "groom", "desk", 0.8, start.AddMinutes(i));
    Assert(state.HabitMemories.Count == 0, "same-day evidence formed a permanent habit");
    learning.Observe(state, "care.groom", "groom", "desk", 0.7, start.AddDays(1));
    learning.Observe(state, "care.groom", "groom", "desk", 0.7, start.AddDays(2));
    Assert(state.HabitMemories.Count == 1, "three-date evidence did not form a habit");
    return Task.CompletedTask;
}

static Task TestEveryBehaviorReadsPreference()
{
    var scorer = new BehaviorScorer();
    var random = new ConstantRandomSource(0.5);
    var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    foreach (var definition in BehaviorCatalog.Autonomous)
    {
        var state = NewState();
        var context = Context(now);
        var baseline = scorer.Score(definition, state, context, Array.Empty<BehaviorHistoryEntry>(), random);
        var preference = new LearnedPreference
        {
            BehaviorId = definition.BehaviorId,
            InteractionType = definition.InteractionType,
            Context = "general",
            Weight = 0.50,
            Confidence = 1,
            LastReinforcedAt = now
        };
        state.LearnedPreferences[preference.Key] = preference;
        var learned = scorer.Score(definition, state, context, Array.Empty<BehaviorHistoryEntry>(), random);
        Assert(learned.LearnedPreference > 0.49,
            $"{definition.BehaviorId} did not read LearnedPreference");
        Assert(learned.FinalScore > baseline.FinalScore + 0.49,
            $"{definition.BehaviorId} preference did not affect final score");
    }
    return Task.CompletedTask;
}

static async Task TestInterruptedLifecycle()
{
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var records = new List<InteractionRecord>();
    var lifecycle = new InteractionLifecycle(clock, record =>
    {
        records.Add(record);
        return Task.CompletedTask;
    });
    var appliedFullness = 0d;
    var session = await lifecycle.StartAsync("care.feed_kibble", "feed", "desk", "routines:kibble");
    appliedFullness += 5;
    await lifecycle.ProgressAsync(
        session,
        0.25,
        new[] { new AppliedEffect("fullness", 5, "points") });
    await lifecycle.InterruptAsync(session, "user_stop");
    Assert(Math.Abs(appliedFullness - 5) < 1e-12, "applied effect was rolled back");
    Assert(records.Select(x => x.Stage).SequenceEqual(new[]
        {
            InteractionLifecycleStage.InteractionStarted,
            InteractionLifecycleStage.InteractionProgressed,
            InteractionLifecycleStage.InteractionInterrupted
        }),
        "lifecycle stages are incomplete or out of order");
    var interrupted = records[^1];
    Assert(Math.Abs(interrupted.CompletionRatio - 0.25) < 1e-12, "completion ratio is inaccurate");
    Assert(interrupted.InterruptReason == "user_stop", "interrupt reason is missing");
    Assert(interrupted.AppliedEffects.Count == 1 && interrupted.AppliedEffects[0].Name == "fullness",
        "interrupted record did not preserve applied effects");
}

static Task TestMigrationIdempotency()
{
    var migrator = new PersonalityBehaviorMigrator();
    var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    var legacy = new LegacyPersonalityData
    {
        Baseline = new TemperamentBaseline { Playful = 0.9 },
        BehaviorWeights = new Dictionary<string, double>
        {
            ["purr"] = 0.8,
            ["unknown_legacy_action"] = 0.7
        },
        LearnedTemperamentDeltas = new Dictionary<string, double> { ["playful"] = 0.18 }
    };
    var first = migrator.Migrate(null, legacy, now);
    var count = first.LearnedPreferences.Count;
    var weight = first.LearnedPreferences.Values.Single().Weight;
    var second = migrator.Migrate(first, legacy, now.AddDays(1));
    Assert(second.LearnedPreferences.Count == count, "migration duplicated preferences");
    Assert(Math.Abs(second.LearnedPreferences.Values.Single().Weight - weight) < 1e-12,
        "migration stacked a weight twice");
    Assert(second.AppliedMigrations.Count(x =>
        x == PersonalityBehaviorSchema.LegacyMigrationId) == 1,
        "migration marker was duplicated");
    Assert(Math.Abs(second.Temperament.Playful - 0.9) < 1e-12,
        "legacy learned delta was merged into temperament");
    Assert(second.LegacyLearningSnapshot?.UnmappedBehaviorWeights.ContainsKey("unknown_legacy_action") == true,
        "unmapped legacy data was not preserved");
    return Task.CompletedTask;
}

static Task TestTwentyFourHourStability()
{
    var state = NewState();
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero));
    var selector = new BehaviorSelector(new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(20260723)));
    var current = string.Empty;
    var started = DateTimeOffset.MinValue;
    var switches = 0;
    for (var i = 0; i < 24 * 60 * 10; i++)
    {
        var context = Context(clock.Now);
        context.CurrentBehaviorId = current;
        context.CurrentBehaviorStartedAt = started;
        var decision = selector.Select(BehaviorCatalog.Autonomous, state, context);
        if (decision.SelectedBehaviorId != current)
        {
            switches++;
            current = decision.SelectedBehaviorId;
            started = clock.Now;
        }
        clock.Advance(TimeSpan.FromSeconds(6));
    }
    Assert(switches < 1200, $"24-hour simulation switched {switches} times");
    return Task.CompletedTask;
}

static Task TestOfflineAbsence()
{
    var state = NewState();
    state.Runtime.Stress = 0.17;
    state.Relationship.Trust = 0.61;
    var beforeRuntime = (
        state.Runtime.Arousal,
        state.Runtime.Stress,
        state.Runtime.SocialDesire,
        state.Runtime.PlayDesire,
        state.Runtime.Curiosity,
        state.Runtime.Fatigue,
        state.Runtime.Safety);
    var beforeRelationship = (
        state.Relationship.Trust,
        state.Relationship.Familiarity,
        state.Relationship.TouchAcceptance,
        state.Relationship.InitiativeAcceptance);
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero));
    clock.Advance(TimeSpan.FromDays(7)); // Application is closed: no active-time tick is called.
    Assert(beforeRuntime == (
        state.Runtime.Arousal,
        state.Runtime.Stress,
        state.Runtime.SocialDesire,
        state.Runtime.PlayDesire,
        state.Runtime.Curiosity,
        state.Runtime.Fatigue,
        state.Runtime.Safety), "offline time changed runtime state");
    Assert(beforeRelationship == (
        state.Relationship.Trust,
        state.Relationship.Familiarity,
        state.Relationship.TouchAcceptance,
        state.Relationship.InitiativeAcceptance), "offline time changed relationship");
    Assert(state.PreferenceEvidence.Count == 0 && state.HabitMemories.Count == 0,
        "absence created blame/debt memory");
    return Task.CompletedTask;
}

static Task TestEligibilityPipeline()
{
    var state = NewState();
    state.Temperament.Playful = 1;
    state.Temperament.Mischievous = 1;
    state.Runtime.PlayDesire = 1;
    var context = Context(
        new DateTimeOffset(2026, 7, 23, 1, 0, 0, TimeSpan.Zero),
        doNotDisturb: true,
        deepNight: true);
    context.RequestSource = BehaviorRequestSource.Autonomous;
    var filter = new EligibilityFilter(new BehaviorArbitrator());
    var pounce = filter.Evaluate(BehaviorCatalog.Find("play.pounce")!, state, context);
    Assert(!pounce.IsEligible &&
           pounce.Reasons.Contains("deep_night_high_disruption") &&
           pounce.Reasons.Contains("quiet_environment"),
        "high disruption was only score-penalized instead of hard-blocked");
    var passive = filter.Evaluate(BehaviorCatalog.Find("idle.side_lie")!, state, context);
    Assert(passive.IsEligible, "quiet passive behavior was incorrectly blocked");
    return Task.CompletedTask;
}

static Task TestSelectionPolicyReproducibility()
{
    static List<string> Run()
    {
        var selector = new BehaviorSelector(
            new BehaviorArbitrator(
                new BehaviorScorer(),
                new SeededRandomSource(20260723),
                new SelectionPolicy()));
        var state = NewState();
        var result = new List<string>();
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 120; i++)
        {
            result.Add(selector.Select(BehaviorCatalog.Autonomous, state, Context(now)).SelectedBehaviorId);
            now = now.AddMinutes(2);
        }
        return result;
    }
    Assert(Run().SequenceEqual(Run()), "fixed seed did not reproduce selection sequence");
    return Task.CompletedTask;
}

static Task TestRuntimeDynamics()
{
    var state = NewState();
    state.Runtime.Stress = 0.90;
    state.Runtime.Fatigue = 0.70;
    state.Runtime.SuspendedAt = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    state.Runtime.LastUpdatedAt = state.Runtime.SuspendedAt.Value;
    var dynamics = new RuntimeStateDynamics();
    var beforeStress = state.Runtime.Stress;
    dynamics.RestoreAfterResume(state, new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero));
    Assert(beforeStress - state.Runtime.Stress <= 0.070001,
        "resume replayed unbounded offline recovery");
    var before = state.Runtime.PlayDesire;
    dynamics.AdvanceActive(state, TimeSpan.FromHours(3), deepNight: false);
    Assert(Math.Abs(state.Runtime.PlayDesire - before) <= RuntimeStateDynamics.MaximumFiveMinuteDelta,
        "one active tick exceeded the unit-time state cap");
    dynamics.ApplyEventDelta(state, RuntimeDimension.Stress, 1);
    Assert(state.Runtime.Stress <= 1, "event delta escaped state bounds");
    return Task.CompletedTask;
}

static Task TestMouseHabituationReplay()
{
    var processor = new PerceptionEventProcessor();
    var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    var intensities = new List<double>();
    for (var i = 0; i < 8; i++)
    {
        var at = now.AddMilliseconds(i * 260);
        var accepted = processor.Accept(new PerceptionEvent
        {
            Timestamp = at,
            Source = "pointer",
            Kind = "mouse_nearby",
            Confidence = 1,
            Ttl = TimeSpan.FromSeconds(5),
            DeduplicationKey = "pointer:mouse_nearby",
            Priority = PerceptionPriority.Background,
            Intensity = 1
        }, at)!;
        intensities.Add(accepted.Intensity);
    }
    Assert(intensities[^1] < intensities[0] * 0.55,
        "repeated mouse passes did not habituate");
    return Task.CompletedTask;
}

static Task TestSystemPerceptionReplay()
{
    var processor = new PerceptionEventProcessor();
    var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    foreach (var (kind, seconds) in new[]
             {
                 ("system_suspend", 0),
                 ("system_resume", 3),
                 ("system_time_changed", 5),
                 ("display_changed", 7)
             })
    {
        var at = now.AddSeconds(seconds);
        processor.Accept(new PerceptionEvent
        {
            Timestamp = at,
            Source = "operating_system",
            Kind = kind,
            Confidence = 1,
            Ttl = TimeSpan.FromSeconds(20),
            DeduplicationKey = $"operating_system:{kind}",
            Priority = PerceptionPriority.Important
        }, at);
    }
    var snapshot = processor.Snapshot(now.AddSeconds(8));
    Assert(snapshot.Count == 4, "system perception replay lost a sensor event");
    Assert(snapshot.All(x => x.Kind is not "meeting" and not "emotion" and not "screen_content"),
        "ordinary system events inferred forbidden meeting/emotion/screen content");
    return Task.CompletedTask;
}

static Task TestContinuousTouchSession()
{
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var sessions = new InteractionSessionManager(clock);
    var first = sessions.GetOrCreateTouch("touch.enjoy", "head", "touch:purr");
    clock.Advance(TimeSpan.FromMilliseconds(900));
    var second = sessions.GetOrCreateTouch("touch.curiosity", "head", "touch:curious");
    Assert(first.Id == second.Id, "continuous touch was split into separate sessions");
    clock.Advance(TimeSpan.FromSeconds(3));
    var third = sessions.GetOrCreateTouch("touch.enjoy", "body", "touch:purr");
    Assert(third.Id != first.Id, "separate touch after quiet gap reused old session");

    var state = NewState();
    var memory = new MemoryMaintenanceEngine();
    memory.AddRawEvent(state, first.Id, "touch.enjoy", "touch", "head", 0.8, 1, 0.9, clock.Now);
    memory.AddRawEvent(state, first.Id, "touch.enjoy", "touch", "head", 0.7, 1, 0.8, clock.Now.AddSeconds(1));
    memory.ConsolidateSession(state, first.Id, clock.Now.AddSeconds(2));
    Assert(state.EpisodicMemories.Count == 1, "one continuous touch created multiple episodes");
    return Task.CompletedTask;
}

static Task TestUnansweredInitiative()
{
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var sessions = new InteractionSessionManager(clock);
    var state = NewState();
    var trust = state.Relationship.Trust;
    sessions.StartInitiative("social.ask_play", "desktop", "life:attention");
    clock.Advance(TimeSpan.FromSeconds(12));
    var ended = sessions.EndActive("unanswered_natural_end");
    Assert(ended is { UserResponded: false, Outcome: "unanswered_natural_end" },
        "unanswered initiative did not end naturally");
    Assert(Math.Abs(state.Relationship.Trust - trust) < 1e-12,
        "unanswered initiative reduced trust");
    return Task.CompletedTask;
}

static Task TestMemoryDeletionCascade()
{
    var state = NewState();
    var memory = new MemoryMaintenanceEngine();
    var start = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    var evidence = new List<Guid>();
    for (var day = 0; day < 3; day++)
    for (var sample = 0; sample < 2; sample++)
    {
        var item = memory.AddRawEvent(
            state,
            Guid.NewGuid(),
            "play.laser.paw",
            "play",
            "desk",
            0.9,
            1,
            0.9,
            start.AddDays(day).AddMinutes(sample));
        evidence.Add(item.Id);
    }
    var key = PreferenceKey.Create("play.laser.paw", "play", "desk");
    Assert(state.DerivedHabitPreferences.ContainsKey(key), "valid evidence did not form derived habit");
    foreach (var id in evidence) memory.DeleteEvidence(state, id, start.AddDays(4));
    Assert(!state.DerivedHabitPreferences.ContainsKey(key), "deleting evidence left derived preference");
    memory.Maintain(state, start.AddDays(5));
    Assert(!state.DerivedHabitPreferences.ContainsKey(key), "deleted evidence resurrected during maintenance");
    return Task.CompletedTask;
}

static Task TestConfirmedFactPriority()
{
    var state = NewState();
    state.ConfirmedProfileFacts.Add(new ConfirmedProfileFact
    {
        Key = "favorite_toy",
        Value = "laser",
        ConfirmedAt = DateTimeOffset.Now
    });
    var resolved = new MemoryMaintenanceEngine().ResolveFact(
        state,
        "favorite_toy",
        new[] { new KeyValuePair<string, string>("favorite_toy", "wand") });
    Assert(resolved == "laser", "automatic inference overrode owner-confirmed fact");
    return Task.CompletedTask;
}

static Task TestInteractionRegionMap()
{
    var map = new InteractionRegionMap();
    var right = map.HitTest("sleep-belly-up", 0, "right", 256, 256, 128, 120);
    var scaled = map.HitTest("sleep-belly-up", 0, "right", 512, 512, 256, 240);
    Assert(right.RegionId == scaled.RegionId, "scale changed semantic hit region");
    var mirroredRight = map.HitTest("idle", 0, "right", 256, 256, 58, 110);
    var mirroredLeft = map.HitTest("idle", 0, "left", 256, 256, 198, 110);
    Assert(mirroredRight.RegionId == mirroredLeft.RegionId, "direction transform lost region alignment");
    return Task.CompletedTask;
}

static Task TestActionSchedulerPhases()
{
    using var scheduler = new ActionScheduler();
    var action = scheduler.Start("care.groom");
    Assert(action.Phase == ActionPhase.Entering, "scheduler did not start in entering phase");
    scheduler.EnterLoop(action);
    Assert(action.Phase == ActionPhase.Looping, "scheduler did not enter loop phase");
    scheduler.BeginExit(action);
    Assert(action.Phase == ActionPhase.Exiting, "scheduler did not begin exit");
    scheduler.Complete(action);
    Assert(action.Phase == ActionPhase.Completed, "scheduler did not complete cleanly");
    var interrupted = scheduler.Start("play.laser.paw");
    scheduler.Stop("user_stop");
    Assert(interrupted.Phase == ActionPhase.Interrupted && interrupted.StopReason == "user_stop",
        "scheduler did not expose safe interruption phase");
    return Task.CompletedTask;
}

static Task TestSleepDayNightDistribution()
{
    var state = NewState();
    state.Runtime.Fatigue = 0.72;
    state.Runtime.Arousal = 0.22;
    state.Runtime.Safety = 0.90;
    var sleep = new Func<string, bool>(x => x.StartsWith("rest.sleep", StringComparison.Ordinal));
    var daytime = RunDistributionAt(state, 1208, 420, sleep, 13);
    var night = RunDistributionAt(state, 1208, 420, sleep, 1);
    Assert(daytime > night + 30, $"daytime sleep preference too weak: {daytime} vs {night}");

    state.Runtime.Fatigue = 0.12;
    state.Runtime.Arousal = 0.78;
    state.Runtime.PlayDesire = 0.85;
    var quietActivity = new Func<string, bool>(x =>
        x.StartsWith("explore.", StringComparison.Ordinal) ||
        x is "play.roll" or "independent.patrol");
    var activeNight = RunDistributionAt(state, 509, 260, quietActivity, 1);
    Assert(activeNight > 0, "deep night suppressed all quiet activity");
    return Task.CompletedTask;
}

static Task TestDoubleClickGestureSemantics()
{
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var interpreter = new GestureInterpreter(clock);
    interpreter.PointerDown(120, 110);
    var first = interpreter.PointerUp(120, 110, "idle.side_lie");
    clock.Advance(TimeSpan.FromMilliseconds(180));
    interpreter.PointerDown(121, 110);
    var second = interpreter.PointerUp(121, 110, "idle.side_lie");
    Assert(first[0].Kind == GestureKind.Touch && second[0].Kind == GestureKind.Touch,
        "a deliberate double click was reinterpreted as panel open or rapid tapping");
    clock.Advance(TimeSpan.FromMilliseconds(180));
    interpreter.PointerDown(120, 111);
    var third = interpreter.PointerUp(120, 111, "idle.side_lie");
    Assert(third[0].Kind == GestureKind.RapidTap,
        "three quick taps no longer reached the rapid-tap gesture boundary");
    return Task.CompletedTask;
}

static Task TestFirstTouchBoundaryGate()
{
    var state = NewState();
    state.Runtime.Stress = 0.96;
    state.Runtime.Safety = 0.10;
    var context = Context(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    context.RequestSource = BehaviorRequestSource.Touch;
    context.Signals["touch"] = 1;
    context.Signals["petting_load"] = 0;
    context.Signals["boundary_pressure"] = 0;
    context.Signals["escape_pressure"] = 0;
    var filter = new EligibilityFilter(new BehaviorArbitrator());
    foreach (var behaviorId in new[] { "touch.warning", "touch.avoid", "touch.run_away" })
    {
        var definition = BehaviorCatalog.Find(behaviorId)
                         ?? throw new InvalidOperationException($"missing {behaviorId}");
        var result = filter.Evaluate(definition, state, context);
        Assert(!result.IsEligible,
            $"{behaviorId} bypassed the first-touch boundary gate");
    }
    return Task.CompletedTask;
}

static Task TestPettingLoadQuietGap()
{
    var clock = new ManualClock(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var interpreter = new GestureInterpreter(clock);
    for (var i = 0; i < 5; i++)
    {
        interpreter.PointerDown(120, 110);
        interpreter.PointerMove(126, 110);
        interpreter.PointerUp(126, 110, "idle.side_lie");
        clock.Advance(TimeSpan.FromMilliseconds(450));
    }
    interpreter.PointerDown(120, 110);
    interpreter.PointerMove(126, 110);
    var continuous = interpreter.PointerUp(126, 110, "idle.side_lie")[0];
    Assert(continuous.RecentInteractionHistory.Count >= 5,
        "continuous strokes did not accumulate inside one short petting window");

    clock.Advance(TimeSpan.FromSeconds(7));
    interpreter.PointerDown(120, 110);
    interpreter.PointerMove(126, 110);
    var afterGap = interpreter.PointerUp(126, 110, "idle.side_lie")[0];
    Assert(afterGap.RecentInteractionHistory.Count == 0,
        "occasional petting stayed overloaded after the quiet gap");
    return Task.CompletedTask;
}

static Task TestPetSpeechBoundary()
{
    var composer = new PetSpeechComposer();
    var affectionate = NewState();
    affectionate.Temperament.Affectionate = 0.95;
    affectionate.Temperament.Independent = 0.05;
    var independent = CloneForDistribution(affectionate);
    independent.Temperament.Affectionate = 0.10;
    independent.Temperament.Independent = 0.95;
    var affectionateLine = composer.Compose(PetSpeechIntent.Startup, affectionate);
    var independentLine = composer.Compose(PetSpeechIntent.Startup, independent);
    Assert(affectionateLine != independentLine, "different temperaments produced identical startup speech");
    var blocked = composer.Compose(
        PetSpeechIntent.General,
        affectionate,
        "API 请求失败，behavior_id=play.roll，打开调试日志。");
    Assert(!composer.ContainsTechnicalLanguage(blocked),
        "technical implementation language leaked into the pet speech channel");
    var narrated = composer.Compose(
        PetSpeechIntent.General,
        independent,
        "朴朴正在执行低趴观察动作。");
    Assert(!narrated.Contains("正在执行", StringComparison.Ordinal) &&
           !narrated.Contains("低趴观察动作", StringComparison.Ordinal),
        "formal action narration leaked into the pet speech channel");
    var systemPrompt = composer.BuildSystemPrompt(
        affectionate,
        "圆脸银灰白长毛幼猫",
        "喜欢靠近主人");
    Assert(systemPrompt.Contains("不处处答应主人") &&
           systemPrompt.Contains("绝对禁止提及或泄露") &&
           systemPrompt.Contains("像有主见、爱答不理又会暗中关心人的猫"),
        "model system prompt omitted autonomy or technical-language boundary");
    return Task.CompletedTask;
}

static Task TestWindowEdgeEligibility()
{
    Assert(BehaviorCatalog.Find("environment.window_edge_rest") is null &&
           BehaviorCatalog.Find("environment.window_edge_walk") is null,
        "retired window-edge behaviors are still autonomous candidates");
    return Task.CompletedTask;
}

static Task TestInteractionParticipation()
{
    var evaluator = new OwnerInteractionParticipationEvaluator();
    var active = NewState();
    active.Runtime.PlayDesire = 0.92;
    active.Runtime.Curiosity = 0.86;
    active.Runtime.Fatigue = 0.08;
    active.Runtime.Stress = 0.04;
    active.Temperament.Playful = 0.90;
    var tired = CloneForDistribution(active);
    tired.Runtime.PlayDesire = 0.18;
    tired.Runtime.Curiosity = 0.20;
    tired.Runtime.Fatigue = 0.92;
    tired.Runtime.Stress = 0.66;

    var activeDecision = evaluator.Evaluate(
        active,
        OwnerInteractionKind.WandPlay,
        new OwnerInteractionContext(48, 92, 88, 10, "general"),
        0.50);
    var tiredDecision = evaluator.Evaluate(
        tired,
        OwnerInteractionKind.WandPlay,
        new OwnerInteractionContext(48, 18, 88, 10, "general"),
        0.50);
    Assert(activeDecision.Probability > tiredDecision.Probability + 0.35,
        $"state did not materially change participation: {activeDecision.Probability:0.00} vs {tiredDecision.Probability:0.00}");
    Assert(activeDecision.Accepted && !tiredDecision.Accepted,
        "the same deterministic roll did not produce accept/refuse from state");

    var hungry = evaluator.Evaluate(
        active,
        OwnerInteractionKind.Feeding,
        new OwnerInteractionContext(12, 80, 88, 10, "general"),
        0.50);
    var full = evaluator.Evaluate(
        active,
        OwnerInteractionKind.Feeding,
        new OwnerInteractionContext(96, 80, 88, 10, "general"),
        0.50);
    Assert(hungry.Probability > full.Probability + 0.30,
        "fullness did not reduce feeding participation");
    return Task.CompletedTask;
}

static Task TestDailyMagicLimit()
{
    var now = new DateTimeOffset(2026, 7, 23, 9, 30, 0, TimeSpan.FromHours(8));
    Assert(DailySpecialRules.CanTriggerAutonomousMagic(null, now),
        "a pet with no prior magic could not self-trigger");
    Assert(!DailySpecialRules.CanTriggerAutonomousMagic(now.AddHours(-2), now),
        "a second autonomous spell was allowed on the same local date");
    Assert(DailySpecialRules.CanTriggerAutonomousMagic(now.AddDays(-1), now),
        "the daily autonomous spell allowance did not reset");
    var magic = BehaviorCatalog.Find("magic.accio_broom")
                ?? throw new InvalidOperationException("autonomous magic was not registered");
    var filter = new EligibilityFilter(new BehaviorArbitrator());
    var context = Context(now);
    var blocked = filter.Evaluate(magic, NewState(), context);
    Assert(!blocked.IsEligible,
        "autonomous magic passed the eligibility filter without a daily allowance signal");
    context.Signals["daily_magic_available"] = 1;
    Assert(filter.Evaluate(magic, NewState(), context).IsEligible,
        "autonomous magic stayed blocked with a valid daily allowance");
    return Task.CompletedTask;
}

static Task TestDailyToiletPlan()
{
    var planner = new DailyToiletPlanner(TimeSpan.FromMinutes(30));
    var random = new SeededRandomSource(1407);
    var now = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(8));
    var preparation = planner.EnsurePlan(null, now, random);
    var plan = preparation.Plan;
    Assert(preparation.Rebuilt, "a missing daily toilet plan was not built");
    Assert(plan.TargetCount is 2 or 3 && plan.Slots.Count == plan.TargetCount,
        $"daily toilet target was not 2 or 3: target={plan.TargetCount}, slots={plan.Slots.Count}");
    Assert(plan.Slots.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() ==
           plan.TargetCount,
        "daily toilet slots did not receive unique idempotency keys");
    Assert(plan.Slots.All(x =>
            DateOnly.FromDateTime(x.ScheduledAt.DateTime) == DateOnly.FromDateTime(now.DateTime)),
        "a daily toilet slot escaped the current local date");

    var unchanged = planner.EnsurePlan(plan, now.AddMinutes(10), random);
    Assert(!unchanged.Rebuilt && ReferenceEquals(plan, unchanged.Plan),
        "the same local date rebuilt the toilet plan");

    var firstDue = plan.Slots[0].ScheduledAt.AddSeconds(1);
    Assert(planner.IsDue(plan, firstDue), "the first scheduled toilet slot was not due");
    Assert(planner.TryReserveDueSlot(plan, firstDue, out var slotId) &&
           !string.IsNullOrWhiteSpace(slotId),
        "the due toilet slot could not be reserved");
    Assert(!planner.TryReserveDueSlot(plan, firstDue, out _),
        "the same toilet slot was reserved twice");
    Assert(planner.TryCompleteSlot(plan, slotId, firstDue.AddMinutes(1)),
        "the reserved toilet slot could not be completed");
    Assert(!planner.TryCompleteSlot(plan, slotId, firstDue.AddMinutes(2)),
        "a completed toilet slot was completed twice");

    var resumeAt = plan.Slots.Max(x => x.ScheduledAt).AddMinutes(1);
    planner.SkipPastPending(plan, resumeAt);
    Assert(plan.Slots.All(x => x.Status is not DailyToiletSlotStatus.Pending),
        "offline resume left past toilet slots pending for catch-up");
    Assert(!planner.IsDue(plan, resumeAt),
        "an offline-missed toilet slot was replayed on resume");

    var nextDay = now.AddDays(1);
    var rebuilt = planner.EnsurePlan(plan, nextDay, random);
    Assert(rebuilt.Rebuilt && rebuilt.Plan.LocalDate != plan.LocalDate,
        "crossing the local date did not rebuild the toilet plan");
    return Task.CompletedTask;
}

static Task TestToiletEligibility()
{
    var definition = BehaviorCatalog.Find("routine.toilet")
                     ?? throw new InvalidOperationException("autonomous toilet was not registered");
    Assert(
        definition.ArbitrationPriority == BehaviorPriority.ContinuousEffect &&
        definition.Interruptible == false &&
        definition.Cooldown >= TimeSpan.FromMinutes(20),
        "toilet lifecycle contract was split from its arbitration definition");
    var filter = new EligibilityFilter(new BehaviorArbitrator());
    var context = Context(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var blocked = filter.Evaluate(definition, NewState(), context);
    Assert(!blocked.IsEligible &&
           blocked.Reasons.Contains("missing_signal:toilet_due"),
        "autonomous toilet passed without a due schedule signal");
    context.Signals["toilet_due"] = 1;
    Assert(filter.Evaluate(definition, NewState(), context).IsEligible,
        "autonomous toilet stayed blocked with a due schedule signal");
    return Task.CompletedTask;
}

static Task TestBedRestCandidate()
{
    var definition = BehaviorCatalog.Find("rest.bed")
                     ?? throw new InvalidOperationException("blue bed rest was not registered");
    Assert(definition.IsPassive && !definition.IsHighDisruption && !definition.RequiresMovement,
        "blue bed rest is not a quiet stationary behavior");
    Assert(definition.MinimumDwell >= TimeSpan.FromMinutes(7),
        $"blue bed rest dwell is too short: {definition.MinimumDwell}");
    Assert(definition.Cooldown >= TimeSpan.FromMinutes(15),
        $"blue bed rest cooldown is too short: {definition.Cooldown}");

    var alert = NewState();
    alert.Runtime.Fatigue = 0.08;
    alert.Runtime.Arousal = 0.88;
    alert.Runtime.Safety = 0.90;
    var sleepy = CloneForDistribution(alert);
    sleepy.Runtime.Fatigue = 0.94;
    sleepy.Runtime.Arousal = 0.10;
    sleepy.Runtime.Stress = 0.05;
    var context = Context(new DateTimeOffset(2026, 7, 23, 22, 30, 0, TimeSpan.Zero));
    var scorer = new BehaviorScorer();
    var random = new ConstantRandomSource(0.5);
    var alertScore = scorer.Score(definition, alert, context, Array.Empty<BehaviorHistoryEntry>(), random);
    var sleepyScore = scorer.Score(definition, sleepy, context, Array.Empty<BehaviorHistoryEntry>(), random);
    Assert(sleepyScore.FinalScore > alertScore.FinalScore + 1.7,
        $"sleepiness did not materially favor bed rest: {sleepyScore.FinalScore:0.000} vs {alertScore.FinalScore:0.000}");
    return Task.CompletedTask;
}

static Task TestSelfGroomCadence()
{
    var definition = BehaviorCatalog.Find("self.groom")
                     ?? throw new InvalidOperationException("self grooming was not registered");
    Assert(definition.IsPassive && !definition.IsHighDisruption && !definition.RequiresMovement,
        "self grooming is not modeled as a quiet stationary routine");
    Assert(definition.MinimumDwell >= TimeSpan.FromSeconds(75),
        $"self grooming loop is too short: {definition.MinimumDwell}");
    Assert(definition.Cooldown >= TimeSpan.FromMinutes(10),
        $"self grooming can repeat too frequently: {definition.Cooldown}");

    var state = NewState();
    state.Runtime.Stress = 0.92;
    var context = Context(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
    var scorer = new BehaviorScorer();
    var random = new ConstantRandomSource(0.5);
    var stressed = scorer.Score(definition, state, context, Array.Empty<BehaviorHistoryEntry>(), random);
    state.Runtime.Stress = 0.05;
    var calm = scorer.Score(definition, state, context, Array.Empty<BehaviorHistoryEntry>(), random);
    Assert(calm.FinalScore > stressed.FinalScore + 0.20,
        "calm daily grooming was not preferred over high-stress grooming");
    return Task.CompletedTask;
}

static Task TestHolidayDateGates()
{
    var offset = TimeSpan.FromHours(8);
    Assert(DailySpecialRules.HolidayFor(new DateTimeOffset(2026, 12, 25, 10, 0, 0, offset)) ==
           SeasonalOccasion.Christmas, "Christmas outfit was not enabled on December 25");
    Assert(DailySpecialRules.HolidayFor(new DateTimeOffset(2026, 12, 24, 10, 0, 0, offset)) ==
           SeasonalOccasion.None, "Christmas outfit leaked onto December 24");
    Assert(DailySpecialRules.HolidayFor(new DateTimeOffset(2026, 10, 31, 10, 0, 0, offset)) ==
           SeasonalOccasion.Halloween, "Halloween outfit was not enabled on October 31");
    Assert(DailySpecialRules.HolidayFor(new DateTimeOffset(2026, 2, 17, 10, 0, 0, offset)) ==
           SeasonalOccasion.SpringFestival, "Spring Festival outfit was not enabled on lunar new year");
    var birthday = new DateTime(1994, 7, 23);
    var now = new DateTimeOffset(2026, 7, 23, 10, 0, 0, offset);
    Assert(DailySpecialRules.IsOwnerBirthday(now, birthday), "owner birthday did not match");
    Assert(DailySpecialRules.OwnerAgeOnBirthday(now, birthday) == 32,
        "owner age was not calculated from the saved birthday");
    return Task.CompletedTask;
}

static Task TestProfileSpeechIdentity()
{
    var composer = new PetSpeechComposer();
    var state = NewState();
    var line = composer.Compose(
        PetSpeechIntent.Startup,
        state,
        "主人回来啦。朴朴在这里。",
        "团团",
        "哥哥",
        "本喵");
    Assert(line.Contains("本喵") && line.Contains("哥哥") && !line.Contains("团团"),
        $"profile identity was not applied to authored speech: {line}");
    var generated = composer.Compose(PetSpeechIntent.Startup, state, null, "团团", "哥哥", "本喵");
    Assert(generated.Contains("本喵") && generated.Contains("哥哥"),
        $"profile identity was not applied to generated speech: {generated}");
    var firstPerson = composer.Compose(
        PetSpeechIntent.Rest,
        state,
        "我先睡一会儿，我们晚点再玩。",
        "团团",
        "哥哥",
        "本喵");
    Assert(firstPerson.Contains("本喵先睡", StringComparison.Ordinal) &&
           firstPerson.Contains("我们晚点", StringComparison.Ordinal),
        $"custom self-reference was not applied to local daily speech: {firstPerson}");
    var profile = new PetProfile { SelfReference = "本喵", AvatarFileName = "..\\unsafe.png" };
    profile.Normalize();
    Assert(profile.SelfIdentity.Contains("宠物自称为“本喵”", StringComparison.Ordinal) &&
           profile.AvatarFileName == "unsafe.png",
        "profile normalization lost the self-reference or did not constrain the avatar file name");
    return Task.CompletedTask;
}

static Task TestContinuousDesktopRoutes()
{
    var bounds = new RouteBounds(0, 0, 1684, 844);
    foreach (var profile in new[]
             {
                 DesktopRouteProfile.FullWalk,
                 DesktopRouteProfile.AutonomousRoam
             })
    {
        var planner = new DesktopRoutePlanner(
            profile == DesktopRouteProfile.FullWalk ? 5219 : 8841);
        var current = new RoutePoint(bounds.Left, bounds.Top);
        var distances = new List<double>();
        var visited = new List<RoutePoint> { current };
        for (var index = 0; index < 72; index++)
        {
            Assert(
                planner.TryCreateWalkSegment(bounds, current, profile, out var segment),
                $"{profile} could not create segment {index}");
            Assert(current.DistanceTo(segment.Start) < 0.0001,
                $"{profile} segment {index} did not start at the previous endpoint");
            Assert(segment.Distance >= 1,
                $"{profile} segment {index} was zero length");
            Assert(segment.Duration > TimeSpan.Zero,
                $"{profile} segment {index} had no duration");

            var previous = segment.Sample(0, bounds);
            for (var sample = 1; sample <= 32; sample++)
            {
                var point = segment.Sample(sample / 32d, bounds);
                Assert(IsInside(bounds, point),
                    $"{profile} segment {index} escaped the work area at sample {sample}");
                Assert(previous.DistanceTo(point) > 0.000001,
                    $"{profile} segment {index} stopped while a walking frame was active");
                previous = point;
            }

            Assert(previous.DistanceTo(segment.End) < 0.0001,
                $"{profile} segment {index} did not finish at its declared endpoint");
            distances.Add(segment.Distance);
            current = segment.End;
            visited.Add(current);
        }

        Assert(distances.Max() >= bounds.Diagonal * 0.24,
            $"{profile} never produced a broad route segment");
        Assert(distances.Min() <= distances.Max() * 0.72,
            $"{profile} did not mix smaller and larger route ranges");
        Assert(visited.Max(x => x.X) - visited.Min(x => x.X) >= bounds.Width * 0.62,
            $"{profile} did not cover enough horizontal desktop range");
        Assert(visited.Max(x => x.Y) - visited.Min(x => x.Y) >= bounds.Height * 0.62,
            $"{profile} did not cover enough vertical desktop range");
    }
    return Task.CompletedTask;
}

static Task TestBroomRouteCoverage()
{
    var bounds = new RouteBounds(0, 0, 1684, 844);
    var planner = new DesktopRoutePlanner(27183);
    var current = new RoutePoint(bounds.Width / 2, bounds.Height / 2);
    for (var bucket = 0; bucket < 3; bucket++)
    {
        var directions = new HashSet<RouteDirection>();
        var visited = new List<RoutePoint> { current };
        for (var index = 0; index < 8; index++)
        {
            Assert(
                planner.TryCreateBroomSegment(bounds, current, out var segment),
                $"broom bucket {bucket} could not create segment {index}");
            Assert(current.DistanceTo(segment.Start) < 0.0001,
                $"broom segment {bucket}:{index} was disconnected");
            Assert(segment.Distance >= 24,
                $"broom segment {bucket}:{index} was too small");
            Assert(segment.Duration >= TimeSpan.FromMilliseconds(850) &&
                   segment.Duration <= TimeSpan.FromMilliseconds(1550),
                $"broom segment {bucket}:{index} was outside the 850-1550ms cruise window");
            Assert(Math.Abs(segment.Bend) <= 48.001 && Math.Abs(segment.Flutter) <= 1.501,
                $"broom segment {bucket}:{index} used an excessive arc or flutter");
            Assert(directions.Add(segment.Direction),
                $"broom bucket {bucket} repeated {segment.Direction}");

            var previous = segment.Sample(0, bounds);
            for (var sample = 1; sample <= 24; sample++)
            {
                var point = segment.Sample(sample / 24d, bounds);
                Assert(IsInside(bounds, point),
                    $"broom segment {bucket}:{index} escaped the work area");
                Assert(previous.DistanceTo(point) > 0.000001,
                    $"broom segment {bucket}:{index} stopped between endpoints");
                previous = point;
            }

            current = segment.End;
            visited.Add(current);
        }

        Assert(directions.SetEquals(Enum.GetValues<RouteDirection>()),
            $"broom bucket {bucket} did not consume all eight directions");
        Assert(visited.Max(x => x.X) - visited.Min(x => x.X) >= bounds.Width * 0.60,
            $"broom bucket {bucket} lacked large horizontal coverage");
        Assert(visited.Max(x => x.Y) - visited.Min(x => x.Y) >= bounds.Height * 0.60,
            $"broom bucket {bucket} lacked large vertical coverage");
    }
    return Task.CompletedTask;
}

static Task TestModelProviderDefaults()
{
    var settings = new ModelApiSettings
    {
        Provider = ModelProvider.DeepSeek,
        ApiFormat = ModelApiFormat.OpenAiChat,
        Endpoint = "https://api.deepseek.com",
        Model = string.Empty
    };
    ModelProtocolAdapter.ApplyProviderDefaults(settings);
    Assert(
        settings.Endpoint == "https://api.deepseek.com/chat/completions",
        $"unexpected DeepSeek endpoint: {settings.Endpoint}");
    Assert(
        settings.Model == "deepseek-v4-flash",
        $"unexpected DeepSeek default model: {settings.Model}");
    Assert(
        ModelProtocolAdapter.NormalizeEndpoint(
            ModelProvider.DeepSeek,
            ModelApiFormat.OpenAiChat,
            "https://api.deepseek.com/v1") ==
        "https://api.deepseek.com/v1/chat/completions",
        "DeepSeek /v1 base URL was not completed");
    return Task.CompletedTask;
}

static Task TestPetSystemPromptMarkdown()
{
    var source =
        "# pupu 记忆\n\n" +
        "## 宠物系统提示词\n" +
        "- 你是有主见的朴朴。\n" +
        "回答时保持幼猫视角，不解释技术细节。\n\n" +
        "## 主人手动记忆\n" +
        "- 喜欢窗边。\n";
    Assert(
        PetSystemPromptMarkdown.TryExtract(source, out var prompt),
        "pet system prompt section was not detected");
    Assert(
        prompt.Contains("你是有主见的朴朴。", StringComparison.Ordinal) &&
        prompt.Contains("回答时保持幼猫视角", StringComparison.Ordinal),
        $"pet system prompt lost content: {prompt}");

    var builder = new System.Text.StringBuilder();
    PetSystemPromptMarkdown.AppendSection(builder, prompt);
    Assert(
        PetSystemPromptMarkdown.TryExtract(builder.ToString(), out var roundTrip) &&
        roundTrip == prompt,
        "pet system prompt did not round-trip");
    return Task.CompletedTask;
}

static async Task TestAlbumExperienceParsing()
{
    var tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "pupu-experience-parse-" + Guid.NewGuid().ToString("N"));
    var root = Path.Combine(tempDirectory, "album");
    Directory.CreateDirectory(root);
    var image = Path.Combine(root, "birthday.png");
    var outside = Path.Combine(tempDirectory, "outside.jpg");
    await File.WriteAllBytesAsync(image, new byte[] { 1, 2, 3 });
    await File.WriteAllBytesAsync(outside, new byte[] { 4, 5, 6 });
    try
    {
        var markdownPath = Path.Combine(root, "birthday.md");
        await File.WriteAllTextAsync(
            markdownPath,
            """
            ---
            date: 2026-07-23
            tags: [生日, 开心, 主人陪伴]
            mood: happy
            behavior: celebrate.idle
            visibility: both
            importance: 0.8
            images:
              - birthday.png
              - ../outside.jpg
            ---

            今天主人给我过生日。
            我很开心，但是假装不在意。
            """);
        var markdown = AlbumExperienceService.ParseMarkdownRecord(root, markdownPath);
        Assert(markdown is not null, "markdown frontmatter did not produce an experience");
        Assert(markdown!.Date?.Date == new DateTime(2026, 7, 23),
            "markdown date was not parsed");
        Assert(markdown.Tags.Contains("生日") &&
               markdown.Mood == "happy" &&
               markdown.BehaviorId == "celebrate.idle" &&
               markdown.Importance == 0.8,
            "markdown metadata was not parsed");
        Assert(markdown.IncludeInConversation && markdown.IncludeInBehaviorDecision,
            "markdown visibility both was not applied");
        Assert(markdown.ImageRelativePaths.SequenceEqual(new[] { "birthday.png" }),
            "missing or escaping markdown image was not safely skipped");
        Assert(markdown.SourceStatus.StartsWith("partial:", StringComparison.Ordinal),
            "skipped markdown image did not leave a local status");

        var plainPath = Path.Combine(root, "窗边午睡.md");
        await File.WriteAllTextAsync(plainPath, "趴在窗边睡了一小会儿。");
        var plain = AlbumExperienceService.ParseMarkdownRecord(root, plainPath);
        Assert(plain is not null &&
               plain.Title == "窗边午睡" &&
               plain.Summary.Contains("窗边", StringComparison.Ordinal),
            "plain markdown did not receive basic metadata");

        var jsonPath = Path.Combine(root, "post.json");
        await File.WriteAllTextAsync(
            jsonPath,
            """
            {
              "title": "生日发帖",
              "body": "主人陪我过生日。",
              "date": "2026-07-23",
              "tags": ["生日", "陪伴"],
              "mood": "happy",
              "behavior": "celebrate.idle",
              "images": ["birthday.png", "../outside.jpg"],
              "allowLlm": true,
              "allowRules": true
            }
            """);
        var json = AlbumExperienceService.ParseJsonRecord(root, jsonPath);
        Assert(json is not null &&
               json.SourceType == AlbumExperienceSourceTypes.JsonPost &&
               json.BehaviorId == "celebrate.idle" &&
               json.ImageRelativePaths.SequenceEqual(new[] { "birthday.png" }),
            "json experience was not parsed or path escape was accepted");
    }
    finally
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }
}

static async Task TestAlbumExperienceIndexAndSearch()
{
    var tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "pupu-experience-index-" + Guid.NewGuid().ToString("N"));
    var root = Path.Combine(tempDirectory, "photos");
    var album = Path.Combine(root, "2026-07-23_生日");
    var image = Path.Combine(album, "cake.jpg");
    var catalogPath = Path.Combine(tempDirectory, "index", "albums.json");
    var experiencePath = Path.Combine(tempDirectory, "index", "album-experiences.json");
    Directory.CreateDirectory(album);
    await File.WriteAllBytesAsync(image, new byte[] { 1, 2, 3, 4 });
    var markdownPath = Path.Combine(album, "birthday.md");
    await File.WriteAllTextAsync(
        markdownPath,
        """
        ---
        date: 2026-07-23
        tags: [生日, 开心]
        mood: happy
        behavior: celebrate.idle
        visibility: both
        ---
        主人陪朴朴过生日。
        """);
    try
    {
        var albums = new PhotoAlbumService(catalogPath);
        await albums.LinkRootAsync(root);
        await albums.SavePhotoDescriptionAsync(image, "蛋糕旁边的生日记录");
        var catalog = await albums.LoadAsync();
        var experiences = new AlbumExperienceService(experiencePath);
        var index = await experiences.RebuildAsync(catalog);

        Assert(index.SchemaVersion == AlbumExperienceService.CurrentSchemaVersion,
            "experience index schema version was not saved");
        Assert(index.Records.Any(x =>
                x.SourceType == AlbumExperienceSourceTypes.PhotoDescription &&
                x.Summary.Contains("生日记录", StringComparison.Ordinal)),
            "old photo description did not become an experience");
        Assert(index.BuildStatus.ScannedFileCount >= 2 &&
               index.BuildStatus.State == "ready" &&
               index.BuildStatus.UsedBackgroundWorker,
            "index build status did not capture background scan statistics");

        var keyword = await experiences.SearchAsync(
            catalog,
            new AlbumExperienceSearchQuery
            {
                Text = "生日",
                MaximumResults = 5
            });
        Assert(keyword.Count >= 1, "keyword search did not match an experience");
        var tagAndDate = await experiences.SearchAsync(
            catalog,
            new AlbumExperienceSearchQuery
            {
                Tags = new[] { "生日" },
                StartDate = new DateTime(2026, 7, 23),
                EndDate = new DateTime(2026, 7, 23),
                MaximumResults = 5
            });
        Assert(tagAndDate.Any(x =>
                x.Record.SourceType == AlbumExperienceSourceTypes.MarkdownPost),
            "tag and date search did not match markdown experience");

        await File.AppendAllTextAsync(markdownPath, "\n后来还戴了一顶小帽子。");
        var refreshed = await experiences.SearchAsync(
            catalog,
            new AlbumExperienceSearchQuery
            {
                Text = "小帽子",
                MaximumResults = 5
            });
        Assert(refreshed.Any(x =>
                x.Record.SourceType == AlbumExperienceSourceTypes.MarkdownPost),
            "markdown modification did not refresh the experience index");
    }
    finally
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }
}

static Task TestAlbumExperienceModelBoundary()
{
    var records = Enumerable.Range(1, 5)
        .Select(index => new AlbumExperienceSearchResult(
            new AlbumExperienceRecord
            {
                Id = index.ToString(),
                Title = $"经历{index}",
                Summary = index == 1
                    ? @"记录来自 C:\Users\owner\Pictures\Pupu\private.jpg，但正文仍可摘要。"
                    : $"第{index}条安全摘要",
                Body = $"有限正文{index}",
                IncludeInConversation = true,
                AllowLlm = true,
                SourceType = AlbumExperienceSourceTypes.MarkdownPost
            },
            10 - index))
        .ToList();
    var context = AlbumExperienceService.BuildLlmContext(records, 3);
    Assert(!context.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase),
        "absolute local path leaked from an experience summary");
    Assert(context.Contains("经历1", StringComparison.Ordinal) &&
           context.Contains("经历3", StringComparison.Ordinal) &&
           !context.Contains("经历4", StringComparison.Ordinal),
        "LLM experience count was not limited to three");
    var protocolJson = new ModelProtocolAdapter().BuildRequestJson(
        new ModelApiSettings
        {
            Model = "test",
            VisionEnabled = true,
            SendAlbumImages = true
        },
        "system",
        "owner",
        images: new[]
        {
            new ModelImageInput { DataUrl = "data:image/png;base64,AQ==" },
            new ModelImageInput { DataUrl = "data:image/png;base64,Ag==" },
            new ModelImageInput { DataUrl = "data:image/png;base64,Aw==" }
        });
    using (var document = System.Text.Json.JsonDocument.Parse(protocolJson))
    {
        var content = document.RootElement
            .GetProperty("messages")[1]
            .GetProperty("content");
        Assert(content.GetArrayLength() == 3,
            "protocol boundary did not limit the request to text plus two images");
    }

    var tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "pupu-experience-images-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    try
    {
        foreach (var name in new[] { "1.png", "2.jpg", "3.webp" })
            File.WriteAllBytes(Path.Combine(tempDirectory, name), new byte[] { 1, 2 });
        records[0].Record.ImageRelativePaths =
            new List<string> { "1.png", "2.jpg", "3.webp", "../escape.png" };
        var paths = AlbumExperienceService.ResolveAuthorizedImagePaths(
            tempDirectory,
            records,
            2);
        Assert(paths.Count == 2 &&
               paths.All(x => Path.GetFullPath(x).StartsWith(
                   Path.GetFullPath(tempDirectory),
                   StringComparison.OrdinalIgnoreCase)),
            "authorized images were not root-confined and limited to two");
    }
    finally
    {
        Directory.Delete(tempDirectory, recursive: true);
    }
    return Task.CompletedTask;
}

static Task TestAlbumExperienceRuleAndArbitration()
{
    var record = new AlbumExperienceRecord
    {
        Title = "生日",
        Summary = "主人陪朴朴过生日",
        Date = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.FromHours(8)),
        Tags = new List<string> { "生日", "开心" },
        Mood = "happy",
        BehaviorId = "celebrate.idle",
        IncludeInConversation = true,
        IncludeInBehaviorDecision = true,
        AllowRules = true
    };
    var reply = AlbumExperienceService.ComposeRuleReply(record);
    Assert(reply.Contains("2026年7月23日", StringComparison.Ordinal) &&
           reply.Contains("主人陪朴朴过生日", StringComparison.Ordinal) &&
           !reply.Contains("画面里", StringComparison.Ordinal),
        "rule mode did not create a bounded metadata-based memory reply");

    var result = new BehaviorArbitrator().Evaluate(
        new BehaviorArbitrationRequest
        {
            BehaviorId = record.BehaviorId,
            Source = BehaviorArbitrationSource.MemoryRecall,
            Priority = BehaviorPriority.MemoryRecall,
            ObservationOnly = true,
            ForbiddenStates = BehaviorStateBlockers.Magic
        },
        new BehaviorArbitrationContext
        {
            CurrentBehaviorId = "magic.scourgify",
            CurrentPriority = BehaviorPriority.ContinuousEffect,
            CurrentStartedAt = DateTimeOffset.Now,
            CurrentInterruptible = false,
            ActiveStates = BehaviorStateBlockers.Magic
        });
    Assert(!result.Accepted &&
           result.ReasonCode == "state_forbidden" &&
           result.Request.Source == BehaviorArbitrationSource.MemoryRecall,
        "album behavior suggestion bypassed behavior arbitration");
    return Task.CompletedTask;
}

static async Task TestTravelExperienceAppend()
{
    var tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "pupu-travel-experience-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    try
    {
        var service = new AlbumExperienceService(
            Path.Combine(tempDirectory, "album-experiences.json"));
        await service.AddTravelExperienceAsync(
            "海边",
            "朴朴从海边回来，记住了风的味道。",
            new DateTimeOffset(2026, 7, 27, 15, 0, 0, TimeSpan.FromHours(8)),
            recalled: false);
        var index = await service.LoadAsync();
        var travel = index.Records.SingleOrDefault(x =>
            x.SourceType == AlbumExperienceSourceTypes.TravelEvent);
        Assert(travel is not null &&
               travel.Tags.Contains("旅行") &&
               travel.IncludeInConversation &&
               travel.IncludeInBehaviorDecision,
            "travel return did not append a lightweight experience candidate");
    }
    finally
    {
        Directory.Delete(tempDirectory, recursive: true);
    }
}

static async Task TestAlbumDiscoveryAndDescription()
{
    var tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "pupu-album-test-" + Guid.NewGuid().ToString("N"));
    var tempRoot = Path.Combine(tempDirectory, "photos");
    var albumDirectory = Path.Combine(tempRoot, "2026-07-24_窗边午睡");
    var photoPath = Path.Combine(albumDirectory, "pupu.jpg");
    var catalogPath = Path.Combine(tempDirectory, "index", "albums.json");
    Directory.CreateDirectory(albumDirectory);
    await File.WriteAllBytesAsync(photoPath, new byte[] { 1, 2, 3, 4 });
    try
    {
        var service = new PhotoAlbumService(catalogPath);
        await service.LinkRootAsync(tempRoot);
        var snapshots = await service.GetSnapshotsAsync();
        var discovered = snapshots.SingleOrDefault(x =>
            x.IsDiscovered &&
            x.Name == "2026-07-24_窗边午睡");
        Assert(discovered is not null, "real child folder was not auto-discovered");
        Assert(
            discovered!.Theme == "窗边午睡" &&
            discovered.StartDate == new DateTime(2026, 7, 24) &&
            discovered.EndDate == new DateTime(2026, 7, 24),
            "folder date/theme metadata was not parsed");

        await service.SavePhotoDescriptionAsync(
            photoPath,
            "朴朴第一次在新家的窗边睡着");
        var matches = await service.SearchAsync(new PhotoAlbumSearchQuery
        {
            Keyword = "新家的窗边",
            AlbumId = discovered.AlbumId
        });
        Assert(matches.Count == 1, "saved description was not searchable");
        Assert(
            matches[0].Description == "朴朴第一次在新家的窗边睡着",
            "saved description was not returned with the photo");
        Assert(
            matches[0].CapturedAt.Date == new DateTime(2026, 7, 24),
            "folder date was not reused by child photo");
        var reloadedCatalog = await new PhotoAlbumService(catalogPath).LoadAsync();
        Assert(reloadedCatalog.SchemaVersion == 2 &&
               reloadedCatalog.PhotoDescriptions.Single().Description ==
               "朴朴第一次在新家的窗边睡着",
            "existing albums.json or its photo description did not reload");
    }
    finally
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }
}

static bool IsInside(RouteBounds bounds, RoutePoint point) =>
    point.X >= bounds.Left - 0.0001 &&
    point.X <= bounds.Right + 0.0001 &&
    point.Y >= bounds.Top - 0.0001 &&
    point.Y <= bounds.Bottom + 0.0001;

static int RunDistribution(
    PersonalityBehaviorState state,
    int seed,
    int samples,
    Func<string, bool> target)
{
    var selector = new BehaviorSelector(new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(seed)));
    var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    var count = 0;
    for (var i = 0; i < samples; i++)
    {
        var decision = selector.Select(BehaviorCatalog.Autonomous, state, Context(now));
        if (target(decision.SelectedBehaviorId)) count++;
        now = now.AddMinutes(2);
    }
    return count;
}

static int RunDistributionAt(
    PersonalityBehaviorState state,
    int seed,
    int samples,
    Func<string, bool> target,
    int hour)
{
    var selector = new BehaviorSelector(new BehaviorArbitrator(
        new BehaviorScorer(),
        new SeededRandomSource(seed)));
    var now = new DateTimeOffset(2026, 7, 23, hour, 0, 0, TimeSpan.Zero);
    var count = 0;
    for (var i = 0; i < samples; i++)
    {
        var context = Context(now, deepNight: hour is < 7 or >= 23);
        context.Signals["daytime"] = hour is >= 7 and < 19 ? 1 : 0;
        context.Signals["deep_night"] = hour is < 7 or >= 23 ? 1 : 0;
        var decision = selector.Select(BehaviorCatalog.Autonomous, state, context);
        if (target(decision.SelectedBehaviorId)) count++;
        now = now.AddMinutes(2);
    }
    return count;
}

static PersonalityBehaviorState NewState() => new()
{
    Temperament = new TemperamentBaseline
    {
        Playful = 0.5,
        Affectionate = 0.5,
        Sensitive = 0.5,
        Independent = 0.5,
        Mischievous = 0.5
    },
    Runtime = new RuntimeState
    {
        Arousal = 0.55,
        Stress = 0.10,
        SocialDesire = 0.55,
        PlayDesire = 0.60,
        Curiosity = 0.60,
        Fatigue = 0.20,
        Safety = 0.80
    },
    Relationship = new RelationshipState
    {
        Trust = 0.60,
        Familiarity = 0.50,
        TouchAcceptance = 0.60,
        InitiativeAcceptance = 0.60
    }
};

static PersonalityBehaviorState CloneForDistribution(PersonalityBehaviorState source) => new()
{
    Temperament = source.Temperament.Clone(),
    Runtime = new RuntimeState
    {
        Arousal = source.Runtime.Arousal,
        Stress = source.Runtime.Stress,
        SocialDesire = source.Runtime.SocialDesire,
        PlayDesire = source.Runtime.PlayDesire,
        Curiosity = source.Runtime.Curiosity,
        Fatigue = source.Runtime.Fatigue,
        Safety = source.Runtime.Safety
    },
    Relationship = new RelationshipState
    {
        Trust = source.Relationship.Trust,
        Familiarity = source.Relationship.Familiarity,
        TouchAcceptance = source.Relationship.TouchAcceptance,
        InitiativeAcceptance = source.Relationship.InitiativeAcceptance
    }
};

static BehaviorContext Context(
    DateTimeOffset now,
    bool userResponded = true,
    bool doNotDisturb = false,
    bool deepNight = false) => new()
{
    Now = now,
    ContextKey = "general",
    TimeBucket = deepNight ? "deep_night" : "day",
    LocationKey = "desktop",
    UserRespondedToLastInitiative = userResponded,
    DoNotDisturb = doNotDisturb,
    IsDeepNight = deepNight
};

static string Snapshot(TemperamentBaseline value) =>
    $"{value.Playful:R}|{value.Affectionate:R}|{value.Sensitive:R}|{value.Independent:R}|{value.Mischievous:R}";

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ManualClock : IClock
{
    public ManualClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; private set; }
    public void Advance(TimeSpan duration) => Now = Now.Add(duration);
}

sealed class ConstantRandomSource : IRandomSource
{
    private readonly double _value;
    public ConstantRandomSource(double value) => _value = value;
    public double NextDouble() => _value;
    public int Next(int minInclusive, int maxExclusive) => minInclusive;
}

sealed class TestAgentMemoryPort : IAgentDecisionStatePort, IAgentMemoryPort
{
    public PersonalityBehaviorState Personality { get; } =
        PersonalityBehaviorState.SafeCompanionDefault();

    public PersonalityBehaviorState ReadDecisionState() =>
        Personality.CreateDecisionSnapshot();

    public AgentMemorySnapshot ReadAgentMemory() => new()
    {
        RecentEpisodes = new[] { "昨天一起玩过逗猫棒" },
        RelationshipFacts = new[] { "主人喜欢轻轻摸头" },
        HabitSummaries = new[] { "午后更愿意靠近主人" }
    };
}

sealed class EchoMemoryAgent : IPetAgent
{
    public PetAgentResult Handle(PetAgentEvent agentEvent, PetAgentContext context) =>
        new()
        {
            Debug = context.LongTermMemorySummaries.ToList()
        };
}
