using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Pupu.Behavior;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed class LocalPetStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly JsonSerializerOptions _lineJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public LocalPetStore()
    {
        Directory.CreateDirectory(StoragePaths.MemoryDirectory);
        Directory.CreateDirectory(StoragePaths.LogDirectory);
    }

    public Task<PetProfile> LoadProfileAsync() =>
        LoadOrDefaultAsync(StoragePaths.ProfileFile, () => new PetProfile());

    public Task<PetState> LoadStateAsync() =>
        LoadOrDefaultAsync(StoragePaths.StateFile, () => new PetState());

    public Task<MemorySummary> LoadSummaryAsync() =>
        LoadOrDefaultAsync(StoragePaths.SummaryFile, () => new MemorySummary());

    public Task<BehaviorPolicy> LoadBehaviorPolicyAsync() =>
        LoadOrDefaultAsync(StoragePaths.BehaviorPolicyFile, () => new BehaviorPolicy());

    public Task<List<BehaviorCorrection>> LoadCorrectionsAsync() =>
        LoadOrDefaultAsync(StoragePaths.CorrectionsFile, () => new List<BehaviorCorrection>());

    public bool PersonalityBehaviorV2Exists => File.Exists(StoragePaths.PersonalityBehaviorV2File);

    public Task<PersonalityBehaviorState> LoadPersonalityBehaviorV2Async() =>
        LoadOrDefaultAsync(
            StoragePaths.PersonalityBehaviorV2File,
            PersonalityBehaviorState.SafeCompanionDefault);

    public Task SaveProfileAsync(PetProfile value) => WriteJsonAtomicAsync(StoragePaths.ProfileFile, value);
    public Task SaveStateAsync(PetState value) => WriteJsonAtomicAsync(StoragePaths.StateFile, value);
    public Task SaveSummaryAsync(MemorySummary value) => WriteJsonAtomicAsync(StoragePaths.SummaryFile, value);
    public Task SaveBehaviorPolicyAsync(BehaviorPolicy value) =>
        WriteJsonAtomicAsync(StoragePaths.BehaviorPolicyFile, value);
    public Task SaveCorrectionsAsync(List<BehaviorCorrection> value) =>
        WriteJsonAtomicAsync(StoragePaths.CorrectionsFile, value);
    public Task SavePersonalityBehaviorV2Async(PersonalityBehaviorState value) =>
        WriteJsonAtomicAsync(StoragePaths.PersonalityBehaviorV2File, value);

    public async Task<string> LoadEditableMemoryAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return File.Exists(StoragePaths.EditableMemoryFile)
                ? await File.ReadAllTextAsync(StoragePaths.EditableMemoryFile, Encoding.UTF8)
                : string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveEditableMemoryAsync(string text)
    {
        await _gate.WaitAsync();
        try { await WriteTextAtomicWithoutLockAsync(StoragePaths.EditableMemoryFile, text); }
        finally { _gate.Release(); }
    }

    public async Task AppendEventAsync(MemoryEvent memoryEvent)
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(StoragePaths.EventsFile))
            {
                await File.WriteAllTextAsync(
                    StoragePaths.EventsFile,
                    "# pupu 相处事件日志\n\n> 时间 | 类型 | 动作 | 重要度 | 情绪 | 摘要\n\n",
                    new UTF8Encoding(false));
            }
            var summary = memoryEvent.Summary
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("|", "｜");
            var effects = string.Join(",", memoryEvent.AppliedEffects)
                .Replace("|", "｜")
                .Replace("\r", " ")
                .Replace("\n", " ");
            var line = $"- {memoryEvent.At:O} | {memoryEvent.Kind} | {memoryEvent.BehaviorKey} | " +
                       $"{memoryEvent.InteractionType} | {memoryEvent.Lifecycle} | " +
                       $"{memoryEvent.CompletionRatio.ToString("0.000", CultureInfo.InvariantCulture)} | " +
                       $"{memoryEvent.InterruptReason.Replace("|", "｜")} | {effects} | " +
                       $"{memoryEvent.Importance.ToString("0.000", CultureInfo.InvariantCulture)} | " +
                       $"{memoryEvent.Sentiment.ToString("0.000", CultureInfo.InvariantCulture)} | " +
                       $"{memoryEvent.Context.Replace("|", "｜")} | {memoryEvent.AnimationSource.Replace("|", "｜")} | {summary}" +
                       Environment.NewLine;
            await File.AppendAllTextAsync(StoragePaths.EventsFile, line, Encoding.UTF8);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<MemoryEvent>> ReadRecentEventsAsync(int maximum = 80)
    {
        await _gate.WaitAsync();
        try
        {
            await MigrateLegacyEventsWithoutLockAsync();
            if (!File.Exists(StoragePaths.EventsFile)) return new List<MemoryEvent>();

            var queue = new Queue<MemoryEvent>(maximum);
            foreach (var line in File.ReadLines(StoragePaths.EventsFile, Encoding.UTF8))
            {
                var item = ParseMarkdownEvent(line);
                if (item is null) continue;
                if (queue.Count == maximum) queue.Dequeue();
                queue.Enqueue(item);
            }

            return queue.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> LoadOrDefaultAsync<T>(string path, Func<T> factory)
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(path))
            {
                var created = factory();
                await WriteJsonAtomicWithoutLockAsync(path, created);
                return created;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<T>(stream, _json) ?? factory();
            }
            catch (JsonException)
            {
                var backup = path + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(path, backup, true);
                var created = factory();
                await WriteJsonAtomicWithoutLockAsync(path, created);
                return created;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value)
    {
        await _gate.WaitAsync();
        try { await WriteJsonAtomicWithoutLockAsync(path, value); }
        finally { _gate.Release(); }
    }

    private async Task WriteJsonAtomicWithoutLockAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, value, _json);
            await stream.FlushAsync();
        }
        File.Move(temp, path, true);
    }

    private static MemoryEvent? ParseMarkdownEvent(string line)
    {
        var value = line.Trim();
        if (!value.StartsWith("- ", StringComparison.Ordinal)) return null;
        var parts = value[2..].Split(" | ", StringSplitOptions.None);
        if (parts.Length == 6)
        {
            if (!DateTimeOffset.TryParse(parts[0], out var legacyAt) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyImportance) ||
                !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var legacySentiment))
                return null;
            return new MemoryEvent
            {
                At = legacyAt,
                Kind = parts[1].Trim(),
                BehaviorKey = parts[2].Trim(),
                Importance = legacyImportance,
                Sentiment = legacySentiment,
                Summary = parts[5].Trim().Replace("｜", "|")
            };
        }
        if (parts.Length != 13 ||
            !DateTimeOffset.TryParse(parts[0], out var at) ||
            !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var completionRatio) ||
            !double.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var importance) ||
            !double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var sentiment))
            return null;

        return new MemoryEvent
        {
            At = at,
            Kind = parts[1].Trim(),
            BehaviorKey = parts[2].Trim(),
            InteractionType = parts[3].Trim(),
            Lifecycle = parts[4].Trim(),
            CompletionRatio = completionRatio,
            InterruptReason = parts[6].Trim().Replace("｜", "|"),
            AppliedEffects = parts[7].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Importance = importance,
            Sentiment = sentiment,
            Context = parts[10].Trim().Replace("｜", "|"),
            AnimationSource = parts[11].Trim().Replace("｜", "|"),
            Summary = parts[12].Trim().Replace("｜", "|")
        };
    }

    private async Task MigrateLegacyEventsWithoutLockAsync()
    {
        if (File.Exists(StoragePaths.EventsFile) || !File.Exists(StoragePaths.LegacyEventsFile)) return;
        var lines = new List<string>();
        foreach (var line in File.ReadLines(StoragePaths.LegacyEventsFile, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<MemoryEvent>(line, _lineJson);
                if (item is null) continue;
                var summary = item.Summary.Replace("\r", " ").Replace("\n", " ").Replace("|", "｜");
                lines.Add($"- {item.At:O} | {item.Kind} | {item.BehaviorKey} | " +
                          $"{item.Importance.ToString("0.000", CultureInfo.InvariantCulture)} | " +
                          $"{item.Sentiment.ToString("0.000", CultureInfo.InvariantCulture)} | {summary}");
            }
            catch (JsonException) { }
        }

        var header = new[]
        {
            "# pupu 相处事件日志",
            "",
            "> 这是可读的 Markdown 日志。每行格式：时间 | 类型 | 动作 | 重要度 | 情绪 | 摘要。",
            ""
        };
        await WriteTextAtomicWithoutLockAsync(StoragePaths.EventsFile, string.Join(Environment.NewLine, header.Concat(lines)) + Environment.NewLine);
    }

    private static async Task WriteTextAtomicWithoutLockAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
