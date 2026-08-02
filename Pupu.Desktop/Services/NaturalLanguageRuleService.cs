using System.Text.RegularExpressions;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed class NaturalLanguageRuleService
{
    private static readonly Regex SentenceSplitter = new("[\\r\\n。；;！!?]+", RegexOptions.Compiled);
    private static readonly Regex DurationPattern = new(
        "(?<number>\\d+(?:\\.\\d+)?|半|一|两|二|三|四|五|六|七|八|九|十)\\s*(?<unit>秒|分钟|分|小时|钟头)",
        RegexOptions.Compiled);
    private static readonly Regex TouchCountPattern = new(
        "(?<number>\\d+|一|两|二|三|四|五|六|七|八|九|十)\\s*(?:次|下)",
        RegexOptions.Compiled);

    public NaturalLanguageApplyResult Apply(
        string input,
        PetProfile profile,
        BehaviorPolicy policy,
        MemorySummary summary)
    {
        var result = new NaturalLanguageApplyResult();
        foreach (var raw in SentenceSplitter.Split(input))
        {
            var sentence = raw.Trim(' ', '\t', '，', ',');
            if (sentence.Length < 2) continue;

            if (ContainsAny(sentence, "删除规则", "移除规则", "取消规则"))
            {
                var needle = StripPrefix(sentence, "删除规则", "移除规则", "取消规则", "：", ":");
                var removed = policy.NaturalLanguageRules.RemoveAll(x =>
                    string.IsNullOrWhiteSpace(needle) || x.Contains(needle, StringComparison.OrdinalIgnoreCase));
                AddChange(result, removed > 0 ? $"已移除 {removed} 条相关角色规则" : "没有找到相符的角色规则");
                continue;
            }

            var recognized = false;

            recognized |= ApplyMemory(sentence, profile, summary, result);
            recognized |= ApplyIgnoredBehavior(sentence, policy, result);
            recognized |= ApplyWalkBehavior(sentence, policy, result);
            recognized |= ApplyInteractionDuration(sentence, policy, result);
            recognized |= ApplyTouchBehavior(sentence, policy, result);
            recognized |= ApplyAnimationCadence(sentence, policy, result);
            recognized |= ApplyAnimationSpeed(sentence, policy, result);
            recognized |= ApplyEnvironmentMode(sentence, policy, result);
            recognized |= ApplyPersonality(sentence, profile, result);

            RemoveSupersededRules(policy.NaturalLanguageRules, sentence);
            AddUnique(policy.NaturalLanguageRules, sentence, 40);
            if (!recognized)
                AddChange(result, $"已保存为补充角色规则：{sentence}");
        }

        profile.ManualMemories = profile.ManualMemories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .TakeLast(40)
            .ToList();
        policy.Clamp();
        profile.Baseline.Clamp();
        return result;
    }

    private static bool ApplyMemory(
        string sentence,
        PetProfile profile,
        MemorySummary summary,
        NaturalLanguageApplyResult result)
    {
        if (ContainsAny(sentence, "忘掉", "删除记忆", "移除记忆"))
        {
            var needle = StripPrefix(sentence, "忘掉", "删除记忆", "移除记忆", "：", ":");
            var removed = profile.ManualMemories.RemoveAll(x =>
                string.IsNullOrWhiteSpace(needle) || x.Contains(needle, StringComparison.OrdinalIgnoreCase));
            removed += summary.Highlights.RemoveAll(x =>
                !string.IsNullOrWhiteSpace(needle) && x.Contains(needle, StringComparison.OrdinalIgnoreCase));
            AddChange(result, removed > 0 ? $"已移除 {removed} 条相关记忆" : "没有找到相符的记忆");
            return true;
        }

        if (ContainsAny(sentence, "记住", "加入记忆", "记得"))
        {
            var memory = StripPrefix(sentence, "请记住", "记住", "加入记忆", "记得", "：", ":");
            if (memory.Length > 1)
            {
                AddUnique(profile.ManualMemories, memory, 40);
                AddChange(result, $"已加入长期记忆：{memory}");
            }
            return true;
        }

        var nameMatch = Regex.Match(sentence, "我(?:叫|的名字是)(?<value>[^，,]+)");
        if (nameMatch.Success)
        {
            summary.OwnerFacts["主人称呼"] = nameMatch.Groups["value"].Value.Trim();
            AddChange(result, $"记住了主人称呼：{summary.OwnerFacts["主人称呼"]}");
            return true;
        }

        return false;
    }

    private static bool ApplyIgnoredBehavior(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        if (!ContainsAny(sentence, "不理", "没人理", "冷落", "不搭理")) return false;
        // PersonalityBehaviorV2 deliberately does not translate absence into
        // punishment, debt, blame, or a prerequisite for mischief.
        AddChange(result, "已记住：主人没有互动不会产生惩罚、欠账、责怪或报复性行为");
        return true;
    }

    private static bool ApplyWalkBehavior(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        if (!ContainsAny(sentence, "遛猫", "散步", "出去走")) return false;

        if (ContainsAny(sentence, "不要提醒", "不用提醒", "不必提醒", "不用每天", "不想每天"))
        {
            policy.DailyWalkReminder = false;
            AddChange(result, "已保存不主动提醒散步；不会累积散步欠账");
            return true;
        }

        var duration = ReadDuration(sentence);
        var describesOneWalk = ContainsAny(sentence, "持续", "每次", "一次", "跑", "走上", "遛上");
        if (duration is not null && describesOneWalk)
        {
            policy.WalkDurationMinutes = Math.Max(1, (int)Math.Round(duration.Value.TotalMinutes));
            AddChange(result, $"每次遛猫约持续 {policy.WalkDurationMinutes} 分钟");
        }
        else
        {
            policy.DailyWalkReminder = false;
            AddChange(result, "已保存散步偏好；V2 不按主人缺席时长主动催促或累积欠账");
        }
        return true;
    }

    private static bool ApplyInteractionDuration(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        var duration = ReadDuration(sentence);
        if (duration is null) return false;
        var changed = false;

        if (ContainsAny(sentence, "投喂", "吃饭", "吃东西", "进食"))
        {
            policy.FeedingSeconds = Math.Max(15, (int)Math.Round(duration.Value.TotalSeconds));
            AddChange(result, $"投喂过程约持续 {FormatDuration(policy.FeedingSeconds)}");
            changed = true;
        }

        if (ContainsAny(sentence, "铲屎", "猫砂", "拉屎", "清理"))
        {
            policy.CleaningSeconds = Math.Max(15, (int)Math.Round(duration.Value.TotalSeconds));
            AddChange(result, $"猫砂互动约持续 {FormatDuration(policy.CleaningSeconds)}");
            changed = true;
        }

        if (ContainsAny(sentence, "梳毛", "刷毛"))
        {
            policy.GroomingSeconds = Math.Max(15, (int)Math.Round(duration.Value.TotalSeconds));
            AddChange(result, $"梳毛约持续 {FormatDuration(policy.GroomingSeconds)}");
            changed = true;
        }

        if (ContainsAny(sentence, "逗猫棒", "玩耍", "陪玩"))
        {
            policy.WandPlaySeconds = Math.Max(15, (int)Math.Round(duration.Value.TotalSeconds));
            AddChange(result, $"陪玩约持续 {FormatDuration(policy.WandPlaySeconds)}");
            changed = true;
        }

        if (ContainsAny(sentence, "睡觉", "睡眠", "打呼噜"))
        {
            policy.SleepMinutes = Math.Max(5, (int)Math.Round(duration.Value.TotalMinutes));
            AddChange(result, $"一次长睡约持续 {policy.SleepMinutes} 分钟");
            changed = true;
        }

        if (ContainsAny(sentence, "走来走去", "自主走动", "溜达") && !ContainsAny(sentence, "遛猫", "散步"))
        {
            policy.AutonomousRoamSeconds = Math.Max(8, (int)Math.Round(duration.Value.TotalSeconds));
            AddChange(result, $"自主走动约持续 {FormatDuration(policy.AutonomousRoamSeconds)}");
            changed = true;
        }

        return changed;
    }

    private static bool ApplyAnimationCadence(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        if (!ContainsAny(sentence, "动作切换", "切换动作", "换动作")) return false;
        var duration = ReadDuration(sentence);
        if (duration is not null)
            policy.MinimumIdleActionSeconds = Math.Clamp(
                (int)Math.Round(duration.Value.TotalSeconds),
                90,
                300);
        else if (ContainsAny(sentence, "不要太频繁", "慢一点", "少一点", "别太快"))
            policy.MinimumIdleActionSeconds = 120;
        else if (ContainsAny(sentence, "快一点", "频繁一点"))
            policy.MinimumIdleActionSeconds = 90;
        else
            return false;

        AddChange(result, $"自主动作至少停留约 {policy.MinimumIdleActionSeconds} 秒");
        return true;
    }

    private static bool ApplyTouchBehavior(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        if (!ContainsAny(sentence, "撸猫", "摸猫", "摸摸", "rua", "点击", "连点", "碰他", "摸他")) return false;
        var changed = false;
        var countMatch = TouchCountPattern.Match(sentence);
        var touchCount = countMatch.Success ? (int)Math.Round(ParseNumber(countMatch.Groups["number"].Value)) : 0;

        if (touchCount > 0 && ContainsAny(sentence, "生气", "跑开", "逃跑", "炸毛"))
        {
            AddChange(result, $"已保存“约 {touchCount} 次快速触摸”的上下文偏好；实际反应仍由有界容忍、压力、信任和近期触摸共同评分");
            changed = true;
        }
        else if (touchCount > 0 && ContainsAny(sentence, "烦", "不耐烦", "警告", "甩尾"))
        {
            AddChange(result, $"已保存“约 {touchCount} 次快速触摸”的上下文偏好；不会按固定点击次数直接绑定动画");
            changed = true;
        }

        if (ContainsAny(sentence, "更容易生气", "容易生气", "容易烦", "敏感一点"))
        {
            AddChange(result, "已保存连续触摸偏好；具体反应由敏感度、当前压力、关系与近期记录共同评分");
            changed = true;
        }
        else if (ContainsAny(sentence, "不容易生气", "耐心一点", "多摸几下", "别太容易烦"))
        {
            AddChange(result, "已保存连续触摸偏好；高信任只会有限缓冲，容忍仍有硬上限");
            changed = true;
        }

        var duration = ReadDuration(sentence);
        if (duration is not null && ContainsAny(sentence, "跑开", "逃跑", "跑掉"))
        {
            AddChange(result, "已保存保持距离的表达偏好；实际时长由压力、安全感和冷却决定，不再由性格或固定秒数线性延长");
            changed = true;
        }
        else if (duration is not null && ContainsAny(sentence, "内", "窗口", "连点"))
        {
            policy.PettingBurstWindowMilliseconds = Math.Max(900, (int)Math.Round(duration.Value.TotalMilliseconds));
            AddChange(result, $"连续触摸判定窗口约 {policy.PettingBurstWindowMilliseconds / 1000.0:0.#} 秒");
            changed = true;
        }

        if (!changed && ContainsAny(sentence, "呼噜", "眨眼", "歪头", "问我"))
        {
            AddChange(result, "已保存轻触时的偏好反应，会与黏人、活泼、信任和触摸记忆共同选择");
            changed = true;
        }
        return changed;
    }

    private static bool ApplyAnimationSpeed(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        if (!ContainsAny(sentence, "动作速度", "动画速度", "动作慢", "动画慢", "播放慢", "动作快", "动画快", "播放快"))
            return false;

        if (ContainsAny(sentence, "慢", "不要太快", "柔和"))
            policy.AnimationSpeedMultiplier = Math.Min(2.5, policy.AnimationSpeedMultiplier + 0.2);
        else if (ContainsAny(sentence, "快", "加速"))
            policy.AnimationSpeedMultiplier = Math.Max(0.75, policy.AnimationSpeedMultiplier - 0.15);
        else
            return false;

        AddChange(result, $"动作播放速度调整为 {_SpeedLabel(policy.AnimationSpeedMultiplier)}");
        return true;
    }

    private static bool ApplyEnvironmentMode(
        string sentence,
        BehaviorPolicy policy,
        NaturalLanguageApplyResult result)
    {
        var changed = false;
        if (ContainsAny(sentence, "开启勿扰", "进入勿扰", "勿扰模式"))
        {
            policy.DoNotDisturb = !ContainsAny(sentence, "关闭", "退出", "取消");
            AddChange(result, policy.DoNotDisturb ? "已开启勿扰模式" : "已关闭勿扰模式");
            changed = true;
        }
        if (ContainsAny(sentence, "开启会议", "进入会议", "会议模式"))
        {
            policy.MeetingMode = !ContainsAny(sentence, "关闭", "退出", "取消");
            AddChange(result, policy.MeetingMode ? "已开启会议模式" : "已关闭会议模式");
            changed = true;
        }
        return changed;
    }

    private static bool ApplyPersonality(
        string sentence,
        PetProfile profile,
        NaturalLanguageApplyResult result)
    {
        var negative = ContainsAny(sentence, "少一点", "低一点", "别太", "不那么", "降低");
        var positive = ContainsAny(sentence, "更", "多一点", "高一点", "非常", "提高");
        if (!negative && !positive) return false;
        var delta = negative ? -0.08 : 0.08;
        var changed = false;

        changed |= AdjustTrait(sentence, "活泼", () => profile.Baseline.Playfulness += delta, "活泼度", delta, result);
        changed |= AdjustTrait(sentence, "黏人", () => profile.Baseline.Clinginess += delta, "黏人度", delta, result);
        changed |= AdjustTrait(sentence, "敏感", () => profile.Baseline.Sensitivity += delta, "敏感度", delta, result);
        changed |= AdjustTrait(sentence, "独立", () => profile.Baseline.Independence += delta, "独立度", delta, result);
        changed |= AdjustTrait(sentence, "淘气", () => profile.Baseline.Mischief += delta, "淘气度", delta, result);
        return changed;
    }

    private static bool AdjustTrait(
        string sentence,
        string keyword,
        Action apply,
        string label,
        double delta,
        NaturalLanguageApplyResult result)
    {
        if (!sentence.Contains(keyword)) return false;
        apply();
        AddChange(result, $"{label}{(delta > 0 ? "提高" : "降低")}一点");
        return true;
    }

    private static TimeSpan? ReadDuration(string sentence)
    {
        var match = DurationPattern.Match(sentence);
        if (!match.Success) return null;
        var value = ParseNumber(match.Groups["number"].Value);
        return match.Groups["unit"].Value switch
        {
            "秒" => TimeSpan.FromSeconds(value),
            "小时" or "钟头" => TimeSpan.FromHours(value),
            _ => TimeSpan.FromMinutes(value)
        };
    }

    private static double ParseNumber(string value)
    {
        if (double.TryParse(value, out var parsed)) return parsed;
        return value switch
        {
            "半" => 0.5,
            "一" => 1,
            "两" or "二" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            "六" => 6,
            "七" => 7,
            "八" => 8,
            "九" => 9,
            "十" => 10,
            _ => 1
        };
    }

    private static string FormatDuration(int seconds) =>
        seconds >= 60 ? $"{seconds / 60.0:0.#} 分钟" : $"{seconds} 秒";

    private static string _SpeedLabel(double multiplier) =>
        multiplier >= 1.65 ? "很慢" : multiplier >= 1.25 ? "偏慢" : multiplier >= 0.95 ? "自然" : "偏快";

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);

    private static void RemoveSupersededRules(List<string> rules, string current)
    {
        if (ContainsAny(current, "趴", "躺", "走来走去", "走动", "溜达", "巡逻"))
            rules.RemoveAll(x => ContainsAny(x, "趴", "躺", "走来走去", "走动", "溜达", "巡逻"));
        if (ContainsAny(current, "捣乱", "恶作剧", "搞事情"))
            rules.RemoveAll(x => ContainsAny(x, "捣乱", "恶作剧", "搞事情"));
        if (ContainsAny(current, "遛猫", "散步", "出去走"))
            rules.RemoveAll(x => ContainsAny(x, "遛猫", "散步", "出去走"));
        if (ContainsAny(current, "动作切换", "切换动作", "换动作"))
            rules.RemoveAll(x => ContainsAny(x, "动作切换", "切换动作", "换动作"));
        if (ContainsAny(current, "撸猫", "摸猫", "摸摸", "rua", "点击", "连点"))
            rules.RemoveAll(x => ContainsAny(x, "撸猫", "摸猫", "摸摸", "rua", "点击", "连点"));

        foreach (var trait in new[] { "活泼", "黏人", "敏感", "独立", "淘气" })
        {
            if (current.Contains(trait)) rules.RemoveAll(x => x.Contains(trait));
        }
    }

    private static string StripPrefix(string value, params string[] parts)
    {
        foreach (var part in parts) value = value.Replace(part, string.Empty, StringComparison.OrdinalIgnoreCase);
        return value.Trim(' ', '\t', '，', ',', '：', ':');
    }

    private static void AddUnique(List<string> values, string value, int maximum)
    {
        values.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        values.Add(value);
        if (values.Count > maximum) values.RemoveRange(0, values.Count - maximum);
    }

    private static void AddChange(NaturalLanguageApplyResult result, string change)
    {
        result.Changed = true;
        if (!result.Changes.Contains(change)) result.Changes.Add(change);
    }
}
