using System.IO;
using System.Text.Json;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed class ConversationSessionStore
{
    public const int MinimumTurns = 8;
    public const int MaximumTurns = 12;
    public const int DefaultTurns = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<ChatMessage>> LoadAsync(
        int maximumTurns = DefaultTurns,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadWithoutLockAsync(cancellationToken);
            return Trim(document.Messages, maximumTurns);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(
        ChatMessage message,
        int maximumTurns = DefaultTurns,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadWithoutLockAsync(cancellationToken);
            document.Messages.Add(message);
            document.Messages = Trim(document.Messages, maximumTurns);
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithoutLockAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendExchangeAsync(
        string ownerText,
        string petText,
        int maximumTurns = DefaultTurns,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadWithoutLockAsync(cancellationToken);
            var now = DateTimeOffset.Now;
            document.Messages.Add(new ChatMessage
            {
                Role = "owner",
                Text = ownerText,
                At = now
            });
            document.Messages.Add(new ChatMessage
            {
                Role = "pupu",
                Text = petText,
                At = now
            });
            document.Messages = Trim(document.Messages, maximumTurns);
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithoutLockAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceAsync(
        IEnumerable<ChatMessage> messages,
        int maximumTurns = DefaultTurns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveWithoutLockAsync(
                new ConversationSessionDocument
                {
                    Messages = Trim(messages, maximumTurns),
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveWithoutLockAsync(
                new ConversationSessionDocument { UpdatedAt = DateTimeOffset.UtcNow },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<ConversationSessionDocument> LoadWithoutLockAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(StoragePaths.ConversationFile))
            return new ConversationSessionDocument();
        try
        {
            await using var stream = new FileStream(
                StoragePaths.ConversationFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<ConversationSessionDocument>(
                       stream,
                       Json,
                       cancellationToken)
                   ?? new ConversationSessionDocument();
        }
        catch (JsonException)
        {
            var backup = StoragePaths.ConversationFile +
                         $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(StoragePaths.ConversationFile, backup, true);
            return new ConversationSessionDocument();
        }
    }

    private static async Task SaveWithoutLockAsync(
        ConversationSessionDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StoragePaths.MemoryDirectory);
        var temporary = StoragePaths.ConversationFile + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         8192,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, document, Json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, StoragePaths.ConversationFile, true);
    }

    private static List<ChatMessage> Trim(
        IEnumerable<ChatMessage> source,
        int maximumTurns)
    {
        var turnLimit = Math.Clamp(maximumTurns, MinimumTurns, MaximumTurns);
        var normalized = source
            .Where(x => IsConversationRole(x.Role) && !string.IsNullOrWhiteSpace(x.Text))
            .Select(CloneNormalized)
            .ToList();
        if (normalized.Count == 0) return normalized;

        var ownerIndices = normalized
            .Select((item, index) => new { item.Role, Index = index })
            .Where(x => x.Role == "owner")
            .Select(x => x.Index)
            .ToList();
        if (ownerIndices.Count > turnLimit)
        {
            var firstRetainedOwner = ownerIndices[ownerIndices.Count - turnLimit];
            normalized = normalized.Skip(firstRetainedOwner).ToList();
        }

        // A normal turn is one owner and one pet message. The small allowance
        // keeps an in-flight owner message without allowing unbounded growth.
        var messageLimit = turnLimit * 2 + 1;
        if (normalized.Count > messageLimit)
            normalized = normalized.TakeLast(messageLimit).ToList();
        while (normalized.Count > 1 && normalized[0].Role == "pupu")
            normalized.RemoveAt(0);
        return normalized;
    }

    private static ChatMessage CloneNormalized(ChatMessage source)
    {
        var text = string.Join(
            ' ',
            (source.Text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > 4000) text = text[..4000] + "…";
        return new ChatMessage
        {
            Role = source.Role.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
                   source.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? "owner"
                : "pupu",
            Text = text,
            At = source.At
        };
    }

    private static bool IsConversationRole(string? role) =>
        role is not null &&
        (role.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("pupu", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("assistant", StringComparison.OrdinalIgnoreCase));

    private sealed class ConversationSessionDocument
    {
        public ConversationSessionDocument() { }

        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
