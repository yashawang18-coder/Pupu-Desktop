using System.Globalization;
using System.Text;
using Pupu.Behavior;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed class MemoryEngine : IAgentDecisionStatePort, IAgentMemoryPort
{
    private readonly LocalPetStore _store;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PreferenceLearningEngine _learning = new();
    private readonly RelationshipUpdater _relationship = new();
    private readonly GestureStateUpdater _gestureStateUpdater = new();
    private readonly RuntimeStateDynamics _runtimeDynamics = new();
    private readonly MemoryMaintenanceEngine _memoryLayers = new();
    private string _lastNotebookText = string.Empty;

    public PetProfile Profile { get; private set; } = new();
    public PetState State { get; private set; } = new();
    public MemorySummary Summary { get; private set; } = new();
    public List<BehaviorCorrection> Corrections { get; private set; } = new();
    public BehaviorPolicy BehaviorPolicy { get; private set; } = new();
    public PersonalityBehaviorState Personality { get; private set; } =
        PersonalityBehaviorState.SafeCompanionDefault();

    public PersonalityBehaviorState ReadDecisionState() =>
        Personality.CreateDecisionSnapshot();

    public AgentMemorySnapshot ReadAgentMemory() => new()
    {
        RecentEpisodes = Personality.EpisodicMemories
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.EndedAt)
            .Take(8)
            .Select(item =>
                $"{item.EndedAt.LocalDateTime:M月d日} · {item.BehaviorId} · " +
                $"{item.InteractionType}/{item.Context} · 结果 {item.OutcomeQuality:P0}")
            .ToList(),
        RelationshipFacts = Personality.ConfirmedProfileFacts
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.ConfirmedAt)
            .Take(8)
            .Select(item => $"{item.Key}：{item.Value}")
            .ToList(),
        HabitSummaries = Personality.DerivedHabitPreferences.Values
            .OrderByDescending(item => Math.Abs(item.EffectiveWeight))
            .Take(8)
            .Select(item =>
                $"{item.BehaviorId} · {item.Context} · " +
                $"{item.EffectiveWeight:+0.00;-0.00;0.00} · {item.DistinctDays} 天")
            .ToList()
    };

    public MemoryEngine(LocalPetStore store, IClock? clock = null)
    {
        _store = store;
        _clock = clock ?? new SystemClock();
    }

    public async Task InitializeAsync()
    {
        Profile = await _store.LoadProfileAsync();
        Profile.Normalize();
        State = await _store.LoadStateAsync();
        Summary = await _store.LoadSummaryAsync();
        Corrections = await _store.LoadCorrectionsAsync();
        BehaviorPolicy = await _store.LoadBehaviorPolicyAsync();

        try
        {
            var existing = _store.PersonalityBehaviorV2Exists
                ? await _store.LoadPersonalityBehaviorV2Async()
                : null;
            var legacy = new LegacyPersonalityData
            {
                Baseline = ToTemperament(Profile.Baseline),
                LearnedTemperamentDeltas = new Dictionary<string, double>
                {
                    ["playful"] = Profile.LearnedDelta.Playfulness,
                    ["affectionate"] = Profile.LearnedDelta.Clinginess,
                    ["sensitive"] = Profile.LearnedDelta.Sensitivity,
                    ["independent"] = Profile.LearnedDelta.Independence,
                    ["mischievous"] = Profile.LearnedDelta.Mischief
                },
                BehaviorWeights = new Dictionary<string, double>(
                    Summary.BehaviorWeights,
                    StringComparer.OrdinalIgnoreCase),
                Trust = Math.Clamp(State.Trust / 100, 0, 1)
            };
            Personality = new PersonalityBehaviorMigrator().Migrate(existing, legacy, _clock.Now);
        }
        catch (Exception)
        {
            // Migration failure must not remove or rewrite any legacy source.
            Personality = PersonalityBehaviorState.SafeCompanionDefault();
        }

        var notebook = await _store.LoadEditableMemoryAsync();
        if (!string.IsNullOrWhiteSpace(notebook))
        {
            ApplyEditableNotebook(notebook);
            _lastNotebookText = notebook;
        }

        BehaviorPolicy.Clamp();
        _runtimeDynamics.RestoreAfterResume(Personality, _clock.Now);
        Personality.Clamp();
        SyncCompatibilityCaches();
        await RunMaintenanceAsync();
        await PersistAsync(importNotebook: false);
        await ExportEditableNotebookAsync();
    }

    public TouchReactionProfile GetTouchReactionProfile()
    {
        var tolerance = _gestureStateUpdater.BoundedRapidTapTolerance(Personality);
        var annoyedAt = Math.Clamp(tolerance - 1, 3, 8);
        var angryAt = Math.Clamp(tolerance + 2, 5, 12);
        var enjoyScore =
            Personality.Temperament.Affectionate * 0.28
            + Personality.Relationship.Trust * 0.24
            + Personality.Relationship.TouchAcceptance * 0.24
            + Personality.Runtime.Safety * 0.16
            - Personality.Runtime.Stress * 0.32;
        var curiousScore =
            Personality.Temperament.Playful * 0.24
            + Personality.Runtime.Curiosity * 0.34
            - Personality.Runtime.Stress * 0.20;
        var purrChance = Math.Clamp(0.22 + enjoyScore, 0.08, 0.82);
        var curiousChance = Math.Clamp(0.18 + curiousScore, 0.08, 0.68);
        var escapeSeconds = Math.Clamp(
            5 + (int)Math.Round(Personality.Runtime.Stress * 10 + (1 - Personality.Runtime.Safety) * 6),
            4,
            24);
        var explanation =
            $"触摸先解释为手势并更新状态，再对享受、好奇、忍耐、警告、回避、跑开统一评分；" +
            $"当前压力 {Personality.Runtime.Stress:P0}、信任 {Personality.Relationship.Trust:P0}、" +
            $"触摸接受 {Personality.Relationship.TouchAcceptance:P0}，rapid_tap 容忍范围约 {annoyedAt}–{angryAt} 次（硬上限 12）。";
        return new TouchReactionProfile(
            annoyedAt, angryAt, purrChance, curiousChance, escapeSeconds, explanation);
    }

    public string GetPersonalityMemoryMatchSummary()
    {
        var habits = Personality.LearnedPreferences.Values.Count(x => x.IsHabitMemory);
        return $"{GetTouchReactionProfile().Explanation} 当前有 {habits} 项跨天习惯；" +
               "五维天生性格只作为长期行为亲和度，单次互动和“像／不像”只更新状态、关系或具体行为偏好。";
    }

    public string GetLearnedPreferenceSummary()
    {
        var preferences = Personality.LearnedPreferences.Values
            .OrderByDescending(x => Math.Abs(x.EffectiveWeight(_clock.Now)))
            .Take(12)
            .Select(x =>
                $"{x.BehaviorId} · {x.InteractionType}/{x.Context} · " +
                $"{x.EffectiveWeight(_clock.Now):+0.00;-0.00;0.00}" +
                (x.IsHabitMemory ? $" · {x.EvidenceDates.Count}天习惯" : " · 主人即时纠正"))
            .ToList();
        return preferences.Count == 0 ? "尚未形成习惯或具体行为偏好。" : string.Join(Environment.NewLine, preferences);
    }

    public async Task<string> GetEditableNotebookAsync()
    {
        await ImportEditableNotebookIfChangedAsync();
        var current = await _store.LoadEditableMemoryAsync();
        return string.IsNullOrWhiteSpace(current) ? BuildEditableNotebook() : current;
    }

    public async Task SaveEditableNotebookAsync(string text)
    {
        await _store.SaveEditableMemoryAsync(text);
        ApplyEditableNotebook(text);
        _lastNotebookText = text;
        await PersistAsync(importNotebook: false);
    }

    public async Task RecordAsync(
        string kind,
        string summary,
        string behaviorKey,
        double importance = 0.4,
        double sentiment = 0,
        bool ownerInteraction = true,
        string interactionType = "event",
        string context = "general",
        string animationSource = "",
        Guid? sessionId = null)
    {
        var at = _clock.Now;
        var item = new MemoryEvent
        {
            At = at,
            Kind = kind,
            Summary = summary,
            BehaviorKey = behaviorKey,
            InteractionType = interactionType,
            Context = context,
            AnimationSource = animationSource,
            Importance = Math.Clamp(importance, 0, 1),
            Sentiment = Math.Clamp(sentiment, -1, 1)
        };

        await _store.AppendEventAsync(item);
        Summary.TotalEvents++;
        if (importance >= 0.78 && !string.IsNullOrWhiteSpace(summary))
        {
            Summary.Highlights.Insert(0, $"{at.LocalDateTime:M月d日}：{summary}");
            Summary.Highlights = Summary.Highlights.Distinct().Take(12).ToList();
        }

        _learning.Observe(
            Personality,
            behaviorKey,
            interactionType,
            context,
            sentiment * importance,
            at);
        _memoryLayers.AddRawEvent(
            Personality,
            sessionId ?? item.Id,
            behaviorKey,
            interactionType,
            context,
            sentiment * importance,
            1,
            Math.Clamp((sentiment + 1) / 2, 0, 1),
            at);
        if (ownerInteraction)
        {
            _relationship.Apply(Personality, at, familiarity: 0.0008);
            State.LastOwnerInteractionAt = at;
        }
        State.LastUpdatedAt = at;
        await PersistAsync();

        if (Summary.TotalEvents % 25 == 0)
            await RunMaintenanceAsync();
    }

    /// <summary>
    /// Appends a local timeline event without feeding behavior preference,
    /// relationship or long-term memory consolidation. This is used by
    /// lightweight travel until the album experience library is introduced.
    /// </summary>
    public async Task RecordLightweightEventAsync(
        string kind,
        string summary,
        string behaviorKey,
        double importance,
        double sentiment,
        string context = "general",
        string animationSource = "")
    {
        var at = _clock.Now;
        await _store.AppendEventAsync(new MemoryEvent
        {
            At = at,
            Kind = kind,
            Summary = summary,
            BehaviorKey = behaviorKey,
            InteractionType = "local_event",
            Context = context,
            AnimationSource = animationSource,
            Importance = Math.Clamp(importance, 0, 0.70),
            Sentiment = Math.Clamp(sentiment, -1, 1)
        });
        Summary.TotalEvents++;
        State.LastUpdatedAt = at;
        await PersistAsync(importNotebook: false);
    }

    public async Task RecordInteractionAsync(InteractionRecord record)
    {
        var effects = record.AppliedEffects
            .Select(x => $"{x.Name}:{x.Delta.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture)}{x.Unit}")
            .ToList();
        var reason = record.InterruptReason ?? record.FailureReason ?? string.Empty;
        await _store.AppendEventAsync(new MemoryEvent
        {
            At = record.At,
            Kind = "interaction_lifecycle",
            Summary = $"{record.Stage} · {record.BehaviorId}",
            BehaviorKey = record.BehaviorId,
            InteractionType = record.InteractionType,
            Context = record.Context,
            Lifecycle = record.Stage.ToString(),
            InteractionId = record.InteractionId,
            CompletionRatio = record.CompletionRatio,
            InterruptReason = reason,
            AppliedEffects = effects,
            AnimationSource = record.AnimationSource,
            Importance = record.Stage is InteractionLifecycleStage.InteractionCompleted
                or InteractionLifecycleStage.InteractionInterrupted
                or InteractionLifecycleStage.InteractionFailed ? 0.65 : 0.30,
            Sentiment = record.Stage switch
            {
                InteractionLifecycleStage.InteractionCompleted => 0.55,
                InteractionLifecycleStage.InteractionFailed => -0.20,
                _ => 0
            }
        });
        Summary.TotalEvents++;

        if (record.Stage is InteractionLifecycleStage.InteractionCompleted
            or InteractionLifecycleStage.InteractionInterrupted
            or InteractionLifecycleStage.InteractionFailed)
        {
            var signal = record.Stage == InteractionLifecycleStage.InteractionCompleted
                ? 0.55
                : record.Stage == InteractionLifecycleStage.InteractionFailed ? -0.20 : 0;
            _learning.Observe(
                Personality,
                record.BehaviorId,
                record.InteractionType,
                record.Context,
                signal * Math.Max(0.15, record.CompletionRatio),
                record.At,
                "interaction_lifecycle");
            _memoryLayers.AddRawEvent(
                Personality,
                record.InteractionId,
                record.BehaviorId,
                record.InteractionType,
                record.Context,
                signal * Math.Max(0.15, record.CompletionRatio),
                1,
                record.Stage == InteractionLifecycleStage.InteractionCompleted ? 0.85 :
                record.Stage == InteractionLifecycleStage.InteractionFailed ? 0.20 : 0.50,
                record.At,
                "interaction_lifecycle");
            _memoryLayers.ConsolidateSession(Personality, record.InteractionId, record.At);
        }
        await PersistAsync();
    }

    public void ApplyRelationshipDelta(
        double trust = 0,
        double familiarity = 0,
        double touchAcceptance = 0,
        double initiativeAcceptance = 0)
    {
        _relationship.Apply(
            Personality,
            _clock.Now,
            trust,
            familiarity,
            touchAcceptance,
            initiativeAcceptance);
        SyncCompatibilityCaches();
    }

    public async Task<BehaviorCorrection> CorrectAsync(
        string behaviorKey,
        int feedback,
        string note,
        string interactionType = "autonomous",
        string context = "general",
        string animationSource = "")
    {
        feedback = Math.Sign(feedback);
        var correction = new BehaviorCorrection
        {
            At = _clock.Now,
            BehaviorKey = behaviorKey,
            InteractionType = interactionType,
            Context = context,
            AnimationSource = animationSource,
            Feedback = feedback,
            Note = string.IsNullOrWhiteSpace(note)
                ? (feedback > 0 ? "这个具体表现很像真实的朴朴" : "这个具体表现不像真实的朴朴")
                : note.Trim()
        };
        Corrections.Add(correction);
        _learning.Correct(
            Personality,
            behaviorKey,
            interactionType,
            context,
            feedback,
            correction.At);

        await _store.SaveCorrectionsAsync(Corrections);
        await _store.AppendEventAsync(new MemoryEvent
        {
            At = correction.At,
            Kind = "owner_correction",
            Summary = correction.Note,
            BehaviorKey = behaviorKey,
            InteractionType = interactionType,
            Context = context,
            AnimationSource = animationSource,
            Importance = 1,
            Sentiment = feedback
        });
        Summary.TotalEvents++;
        await PersistAsync();
        return correction;
    }

    public async Task<bool> UndoLastCorrectionAsync()
    {
        var correction = Corrections.LastOrDefault(x => !x.IsReverted);
        if (correction is null) return false;
        correction.IsReverted = true;
        _learning.UndoCorrection(
            Personality,
            correction.BehaviorKey,
            correction.InteractionType,
            correction.Context,
            correction.Feedback,
            _clock.Now);
        await _store.SaveCorrectionsAsync(Corrections);
        await PersistAsync();
        return true;
    }

    public async Task<string> BuildChatContextAsync()
    {
        var recent = await _store.ReadRecentEventsAsync(18);
        var traits = Personality.Temperament;
        var activeCorrections = Corrections.Where(x => !x.IsReverted).TakeLast(10).ToList();
        var builder = new StringBuilder();

        builder.AppendLine("【本地宠物档案】");
        builder.AppendLine(Profile.SelfIdentity);
        if (Profile.OwnerBirthday is { } ownerBirthday)
            builder.AppendLine($"主人的生日：{ownerBirthday:yyyy年M月d日}。");
        builder.AppendLine(
            $"天生性格（主人设定）：活泼{traits.Playful:0.00}，黏人{traits.Affectionate:0.00}，敏感{traits.Sensitive:0.00}，独立{traits.Independent:0.00}，淘气{traits.Mischievous:0.00}。");
        builder.AppendLine(
            $"当前状态：唤醒{Personality.Runtime.Arousal:0.00}，压力{Personality.Runtime.Stress:0.00}，社交意愿{Personality.Runtime.SocialDesire:0.00}，玩耍意愿{Personality.Runtime.PlayDesire:0.00}，好奇{Personality.Runtime.Curiosity:0.00}，疲劳{Personality.Runtime.Fatigue:0.00}，安全感{Personality.Runtime.Safety:0.00}。");
        builder.AppendLine(
            $"关系：信任{Personality.Relationship.Trust:0.00}，熟悉{Personality.Relationship.Familiarity:0.00}，触摸接受{Personality.Relationship.TouchAcceptance:0.00}，主动行为接受{Personality.Relationship.InitiativeAcceptance:0.00}。");
        builder.AppendLine($"性格—状态—关系—偏好：{GetPersonalityMemoryMatchSummary()}");

        if (Personality.HabitMemories.Count > 0)
        {
            builder.AppendLine("跨天形成的习惯：");
            foreach (var habit in Personality.HabitMemories.TakeLast(10))
                builder.AppendLine($"- {habit.BehaviorId} / {habit.InteractionType} / {habit.Context}: {habit.LearnedWeight:+0.00;-0.00;0.00}");
        }

        if (BehaviorPolicy.NaturalLanguageRules.Count > 0)
        {
            builder.AppendLine("主人用自然语言设定的角色规则（优先遵守）：");
            foreach (var rule in BehaviorPolicy.NaturalLanguageRules.TakeLast(12))
                builder.AppendLine($"- {rule}");
        }

        if (!string.IsNullOrWhiteSpace(Profile.SystemPrompt))
        {
            builder.AppendLine("主人在 pupu-memory.md 保存的宠物系统提示词（在固定角色与安全边界内优先遵守）：");
            builder.AppendLine(Profile.SystemPrompt);
        }

        if (Profile.ManualMemories.Count > 0)
        {
            builder.AppendLine("主人手动加入的长期记忆：");
            foreach (var memory in Profile.ManualMemories.TakeLast(10))
                builder.AppendLine($"- {memory}");
        }

        if (activeCorrections.Count > 0)
        {
            builder.AppendLine("主人对具体行为的纠正（不会修改天生性格）：");
            foreach (var correction in activeCorrections)
            {
                var label = correction.Feedback > 0 ? "像朴朴" : "不像朴朴";
                builder.AppendLine(
                    $"- [{label}/{correction.BehaviorKey}/{correction.InteractionType}/{correction.Context}] {correction.Note}");
            }
        }

        if (recent.Count > 0)
        {
            builder.AppendLine("最近相处摘要：");
            foreach (var memory in recent.TakeLast(10)) builder.AppendLine($"- {memory.Summary}");
        }
        return builder.ToString();
    }

    public async Task<NaturalLanguageApplyResult> ApplyNaturalLanguageAsync(string input)
    {
        var parser = new NaturalLanguageRuleService();
        var result = parser.Apply(input, Profile, BehaviorPolicy, Summary);
        if (!result.Changed) return result;

        // Natural-language trait changes are explicit owner edits. They update
        // the baseline once here; ordinary interaction and feedback never call
        // this path.
        Personality.Temperament = ToTemperament(Profile.Baseline);
        await _store.AppendEventAsync(new MemoryEvent
        {
            At = _clock.Now,
            Kind = "owner_rule",
            Summary = $"主人用自然语言维护pupu：{NormalizeForMemory(input)}",
            BehaviorKey = "owner.rule",
            InteractionType = "owner_configuration",
            Context = "control_panel",
            Importance = 0.92,
            Sentiment = 0.35
        });
        Summary.TotalEvents++;
        Summary.Highlights.Insert(0, $"{_clock.Now.LocalDateTime:M月d日}：主人更新了角色规则与记忆");
        Summary.Highlights = Summary.Highlights.Distinct().Take(12).ToList();
        State.LastOwnerInteractionAt = _clock.Now;
        await PersistAsync();
        return result;
    }

    public async Task RunMaintenanceAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var recent = await _store.ReadRecentEventsAsync(200);
            Summary.TotalEvents = Math.Max(Summary.TotalEvents, recent.Count);
            _learning.ConsolidateAll(Personality, _clock.Now);
            _memoryLayers.Maintain(Personality, _clock.Now);
            Summary.Highlights = recent
                .Where(x => x.Importance >= 0.78)
                .OrderByDescending(x => x.At)
                .Select(x => $"{x.At.LocalDateTime:M月d日}：{x.Summary}")
                .Concat(Summary.Highlights)
                .Distinct()
                .Take(12)
                .ToList();
            Summary.LastConsolidatedAt = _clock.Now;
            await PersistAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveStateAsync()
    {
        Personality.Clamp();
        SyncCompatibilityCaches();
        State.Clamp();
        State.LastUpdatedAt = _clock.Now;
        await _store.SaveStateAsync(State);
        await _store.SavePersonalityBehaviorV2Async(Personality);
    }

    public async Task SaveBaselineAsync(PersonalityTraits baseline)
    {
        baseline.Clamp();
        Profile.Baseline = baseline.Clone();
        Personality.Temperament = ToTemperament(baseline);
        await PersistAsync();
    }

    public async Task SaveProfileAsync(PetProfile profile)
    {
        profile.Normalize();
        Profile.Name = profile.Name;
        Profile.ChineseName = profile.ChineseName;
        Profile.EnglishName = profile.EnglishName;
        Profile.Breed = profile.Breed;
        Profile.Sex = profile.Sex;
        Profile.SelfReference = profile.SelfReference;
        Profile.Birthday = profile.Birthday;
        Profile.OwnerNickname = profile.OwnerNickname;
        Profile.RelationshipToOwner = profile.RelationshipToOwner;
        Profile.OwnerBirthday = profile.OwnerBirthday;
        Profile.SystemPrompt = profile.SystemPrompt;
        Profile.AvatarFileName = profile.AvatarFileName;
        Profile.Description = profile.Description;
        UpsertConfirmedProfileFact("pet.chinese_name", Profile.ChineseName);
        UpsertConfirmedProfileFact("pet.english_name", Profile.EnglishName);
        UpsertConfirmedProfileFact("pet.breed", Profile.Breed);
        UpsertConfirmedProfileFact("pet.sex", Profile.Sex);
        UpsertConfirmedProfileFact("pet.self_reference", Profile.SelfReference);
        UpsertConfirmedProfileFact(
            "pet.birthday",
            Profile.Birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "未填写");
        UpsertConfirmedProfileFact("owner.nickname", Profile.OwnerAddress);
        UpsertConfirmedProfileFact("owner.relationship", Profile.RelationshipToOwner);
        UpsertConfirmedProfileFact(
            "owner.birthday",
            Profile.OwnerBirthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "未填写");
        await PersistAsync();
    }

    public async Task ResetLearningAsync()
    {
        Personality.LearnedPreferences.Clear();
        Personality.PreferenceEvidence.Clear();
        Personality.HabitMemories.Clear();
        Personality.RawInteractionEvents.Clear();
        Personality.EpisodicMemories.Clear();
        Personality.DerivedHabitPreferences.Clear();
        Personality.DeletedEvidenceIds.Clear();
        foreach (var correction in Corrections.Where(x => !x.IsReverted))
        {
            _learning.Correct(
                Personality,
                correction.BehaviorKey,
                correction.InteractionType,
                correction.Context,
                correction.Feedback,
                correction.At);
        }
        await PersistAsync();
    }

    public async Task<bool> DeleteMemoryEvidenceAsync(Guid evidenceId)
    {
        if (!_memoryLayers.DeleteEvidence(Personality, evidenceId, _clock.Now)) return false;
        await PersistAsync();
        return true;
    }

    public async Task ConfirmProfileFactAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
        Personality.ConfirmedProfileFacts.RemoveAll(x =>
            string.Equals(x.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        Personality.ConfirmedProfileFacts.Add(new ConfirmedProfileFact
        {
            Key = key.Trim(),
            Value = value.Trim(),
            ConfirmedAt = _clock.Now,
            Source = "owner"
        });
        await PersistAsync();
    }

    private void UpsertConfirmedProfileFact(string key, string value)
    {
        Personality.ConfirmedProfileFacts.RemoveAll(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        Personality.ConfirmedProfileFacts.Add(new ConfirmedProfileFact
        {
            Key = key,
            Value = value,
            ConfirmedAt = _clock.Now,
            Source = "owner_profile"
        });
    }

    public void AdvanceActiveRuntime(TimeSpan elapsed, bool deepNight) =>
        _runtimeDynamics.AdvanceActive(Personality, elapsed, deepNight);

    public void MarkSuspended() => _runtimeDynamics.MarkSuspended(Personality, _clock.Now);

    public void RestoreAfterResume() => _runtimeDynamics.RestoreAfterResume(Personality, _clock.Now);

    private async Task PersistAsync(bool importNotebook = true)
    {
        if (importNotebook) await ImportEditableNotebookIfChangedAsync();
        Personality.Clamp();
        SyncCompatibilityCaches();
        State.Clamp();
        BehaviorPolicy.Clamp();
        await _store.SaveProfileAsync(Profile);
        await _store.SaveSummaryAsync(Summary);
        await _store.SaveStateAsync(State);
        await _store.SaveBehaviorPolicyAsync(BehaviorPolicy);
        await _store.SavePersonalityBehaviorV2Async(Personality);
        await ExportEditableNotebookAsync();
    }

    private void SyncCompatibilityCaches()
    {
        Profile.Baseline = FromTemperament(Personality.Temperament);
        State.Trust = Personality.Relationship.Trust * 100;
        // Keep the legacy delta for viewing/migration evidence only.
        Profile.ClampLearning();
    }

    private async Task ImportEditableNotebookIfChangedAsync()
    {
        var text = await _store.LoadEditableMemoryAsync();
        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(text, _lastNotebookText, StringComparison.Ordinal))
            return;
        ApplyEditableNotebook(text);
        _lastNotebookText = text;
    }

    private void ApplyEditableNotebook(string text)
    {
        var sections = ParseNotebookSections(text);
        if (PetSystemPromptMarkdown.TryExtract(text, out var systemPrompt))
            Profile.SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? PetProfile.DefaultSystemPrompt
                : systemPrompt;
        if (sections.TryGetValue("主人自由编辑的长期记忆", out var memories) ||
            sections.TryGetValue("主人手动记忆", out memories))
            Profile.ManualMemories = memories.Distinct().TakeLast(80).ToList();
        if (sections.TryGetValue("重要回忆", out var highlights))
            Summary.Highlights = highlights.Distinct().Take(20).ToList();
        if (sections.TryGetValue("主人确认事实", out var confirmedFacts))
        {
            var parsed = ParseKeyValues(confirmedFacts);
            Personality.ConfirmedProfileFacts = parsed.Select(pair => new ConfirmedProfileFact
            {
                Key = pair.Key,
                Value = pair.Value,
                ConfirmedAt = _clock.Now,
                Source = "owner_notebook"
            }).ToList();
        }
        if (sections.TryGetValue("自然语言角色规则", out var rules))
            BehaviorPolicy.NaturalLanguageRules = rules.Distinct().TakeLast(60).ToList();

        if (sections.TryGetValue("宠物档案／主人设定", out var profileLines))
        {
            var values = ParseKeyValues(profileLines);
            if (values.TryGetValue("中文名", out var chineseName)) Profile.ChineseName = chineseName;
            if (values.TryGetValue("英文名", out var englishName)) Profile.EnglishName = englishName;
            if (values.TryGetValue("品种", out var breed)) Profile.Breed = breed;
            if (values.TryGetValue("性别", out var sex)) Profile.Sex = sex;
            if (values.TryGetValue("宠物自称", out var selfReference))
                Profile.SelfReference = selfReference;
            if (values.TryGetValue("对主人昵称", out var ownerNickname))
                Profile.OwnerNickname = ownerNickname is "无" or "未填写" ? string.Empty : ownerNickname;
            if (values.TryGetValue("和主人关系", out var relationship))
                Profile.RelationshipToOwner = relationship;
            if (values.TryGetValue("宠物生日", out var petBirthday) &&
                DateTime.TryParse(petBirthday, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedPetBirthday))
                Profile.Birthday = parsedPetBirthday.Date;
            if (values.TryGetValue("主人生日", out var ownerBirthday) &&
                DateTime.TryParse(ownerBirthday, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedOwnerBirthday))
                Profile.OwnerBirthday = parsedOwnerBirthday.Date;
            Profile.Normalize();
        }

        if (sections.TryGetValue("天生性格／主人设定（0-100）", out var traits) ||
            sections.TryGetValue("性格底色（0-100）", out traits))
        {
            var values = ParseKeyValues(traits);
            Personality.Temperament.Playful =
                ReadPercent(values, "活泼", Personality.Temperament.Playful);
            Personality.Temperament.Affectionate =
                ReadPercent(values, "黏人", Personality.Temperament.Affectionate);
            Personality.Temperament.Sensitive =
                ReadPercent(values, "敏感", Personality.Temperament.Sensitive);
            Personality.Temperament.Independent =
                ReadPercent(values, "独立", Personality.Temperament.Independent);
            Personality.Temperament.Mischievous =
                ReadPercent(values, "淘气", Personality.Temperament.Mischievous);
        }

        if (sections.TryGetValue("逐渐形成的习惯和互动偏好（-100 到 100）", out var preferences))
        {
            foreach (var pair in ParseKeyValues(preferences))
            {
                if (!double.TryParse(pair.Value, out var value)) continue;
                var parts = pair.Key.Split('|');
                if (parts.Length != 3) continue;
                var preference = new LearnedPreference
                {
                    BehaviorId = parts[0].Trim(),
                    InteractionType = parts[1].Trim(),
                    Context = parts[2].Trim(),
                    Weight = Math.Clamp(value / 100, -0.65, 0.65),
                    Confidence = 1,
                    UpdatedAt = _clock.Now,
                    LastReinforcedAt = _clock.Now
                };
                Personality.LearnedPreferences[preference.Key] = preference;
            }
        }

        Profile.Normalize();
        Personality.Temperament.Clamp();
        Profile.Baseline = FromTemperament(Personality.Temperament);
        BehaviorPolicy.Clamp();
    }

    private async Task ExportEditableNotebookAsync()
    {
        var text = BuildEditableNotebook();
        await _store.SaveEditableMemoryAsync(text);
        _lastNotebookText = text;
    }

    private string BuildEditableNotebook()
    {
        var traits = Personality.Temperament;
        var builder = new StringBuilder();
        builder.AppendLine("# pupu 长期记忆、天生性格与习惯");
        builder.AppendLine();
        builder.AppendLine("> 天生性格只由主人明确修改；普通互动和“像／不像”不会改变它。");
        builder.AppendLine("> 习惯与互动偏好精确关联 behavior_id、interaction_type 和 context，并会随时间衰减。");
        builder.AppendLine();
        builder.AppendLine("## 宠物档案／主人设定");
        builder.AppendLine($"- 中文名: {Profile.ChineseName}");
        builder.AppendLine($"- 英文名: {Profile.EnglishName}");
        builder.AppendLine($"- 品种: {Profile.Breed}");
        builder.AppendLine($"- 性别: {Profile.Sex}");
        builder.AppendLine($"- 宠物自称: {Profile.SelfReference}");
        builder.AppendLine($"- 宠物生日: {Profile.Birthday?.ToString("yyyy-MM-dd") ?? "未填写"}");
        builder.AppendLine($"- 对主人昵称: {(string.IsNullOrWhiteSpace(Profile.OwnerNickname) ? "无" : Profile.OwnerNickname)}");
        builder.AppendLine($"- 和主人关系: {Profile.RelationshipToOwner}");
        builder.AppendLine($"- 主人生日: {Profile.OwnerBirthday?.ToString("yyyy-MM-dd") ?? "未填写"}");
        builder.AppendLine();
        PetSystemPromptMarkdown.AppendSection(builder, Profile.SystemPrompt);
        builder.AppendLine("## 主人自由编辑的长期记忆");
        builder.AppendLine("> 在这里逐行填写希望宠物长期记住的事情；保存后会立即进入下一次对话背景。");
        foreach (var item in Profile.ManualMemories) builder.AppendLine($"- {item}");
        builder.AppendLine();
        builder.AppendLine("## 重要回忆");
        foreach (var item in Summary.Highlights) builder.AppendLine($"- {item}");
        builder.AppendLine();
        builder.AppendLine("## 主人确认事实");
        foreach (var fact in Personality.ConfirmedProfileFacts.OrderBy(x => x.Key))
            builder.AppendLine($"- {fact.Key}: {fact.Value}");
        builder.AppendLine();
        builder.AppendLine("## 天生性格／主人设定（0-100）");
        builder.AppendLine($"- 活泼: {traits.Playful * 100:0}");
        builder.AppendLine($"- 黏人: {traits.Affectionate * 100:0}");
        builder.AppendLine($"- 敏感: {traits.Sensitive * 100:0}");
        builder.AppendLine($"- 独立: {traits.Independent * 100:0}");
        builder.AppendLine($"- 淘气: {traits.Mischievous * 100:0}");
        builder.AppendLine();
        builder.AppendLine("## 逐渐形成的习惯和互动偏好（-100 到 100）");
        foreach (var preference in Personality.LearnedPreferences.Values
                     .OrderBy(x => x.BehaviorId)
                     .ThenBy(x => x.InteractionType)
                     .ThenBy(x => x.Context))
        {
            builder.AppendLine(
                $"- {preference.BehaviorId}|{preference.InteractionType}|{preference.Context}: " +
                $"{preference.EffectiveWeight(_clock.Now) * 100:0}");
        }
        builder.AppendLine();
        builder.AppendLine("## 自然语言角色规则");
        foreach (var item in BehaviorPolicy.NaturalLanguageRules) builder.AppendLine($"- {item}");
        builder.AppendLine();
        builder.AppendLine("## 性格、状态、关系与偏好说明（自动生成）");
        builder.AppendLine($"- {GetPersonalityMemoryMatchSummary()}");
        builder.AppendLine();
        builder.AppendLine("## 旧学习快照");
        builder.AppendLine("- 旧自动性格偏移保存在 personality-behavior-v2.json 的 LegacyLearningSnapshot，仅供查看，不参与运行决策。");
        builder.AppendLine();
        builder.AppendLine("## 编辑说明");
        builder.AppendLine("- `events.md` 记录 InteractionStarted/Progressed/Completed/Interrupted/Failed 及已应用效果。");
        builder.AppendLine("- 四层记忆为 ConfirmedProfileFact、RawInteractionEvent、EpisodicMemory、DerivedHabitPreference；行为只读取索引化派生偏好。");
        builder.AppendLine("- JSON 文件是版本化运行缓存；本 Markdown 文件是主人可维护入口。");
        return builder.ToString();
    }

    private static Dictionary<string, List<string>> ParseNotebookSections(string text)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = line[3..].Trim();
                result.TryAdd(current, new List<string>());
                continue;
            }
            if (current is null || line.Length == 0 || line.StartsWith('>')) continue;
            var value = line.StartsWith("- ", StringComparison.Ordinal)
                ? line[2..].Trim()
                : line;
            if (value.Length > 0) result[current].Add(value);
        }
        return result;
    }

    private static Dictionary<string, string> ParseKeyValues(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var split = line.LastIndexOf(':');
            if (split < 1) split = line.LastIndexOf('：');
            if (split < 1) continue;
            result[line[..split].Trim()] = line[(split + 1)..].Trim();
        }
        return result;
    }

    private static double ReadPercent(
        Dictionary<string, string> values,
        string key,
        double fallback) =>
        values.TryGetValue(key, out var raw) &&
        double.TryParse(raw.TrimEnd('%'), out var value)
            ? Math.Clamp(value / 100, 0, 1)
            : fallback;

    private static string NormalizeForMemory(string value)
    {
        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 220 ? normalized : normalized[..220] + "…";
    }

    private static TemperamentBaseline ToTemperament(PersonalityTraits value) => new()
    {
        Playful = value.Playfulness,
        Affectionate = value.Clinginess,
        Sensitive = value.Sensitivity,
        Independent = value.Independence,
        Mischievous = value.Mischief
    };

    private static PersonalityTraits FromTemperament(TemperamentBaseline value) => new()
    {
        Playfulness = value.Playful,
        Clinginess = value.Affectionate,
        Sensitivity = value.Sensitive,
        Independence = value.Independent,
        Mischief = value.Mischievous
    };
}
