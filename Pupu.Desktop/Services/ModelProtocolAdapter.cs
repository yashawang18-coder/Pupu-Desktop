using System.Text.Json;
using System.Text.Json.Nodes;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed record ModelProviderPreset(
    ModelProvider Provider,
    string DisplayName,
    string DefaultEndpoint,
    string DefaultModel,
    ModelApiFormat DefaultApiFormat,
    bool SupportsResponses,
    bool SupportsVision);

/// <summary>
/// Pure protocol translation for OpenAI-compatible providers. This type does
/// not perform network, credential, file-system, or WPF operations.
/// </summary>
public sealed class ModelProtocolAdapter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ModelProviderPreset GetPreset(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenAI => new ModelProviderPreset(
            provider,
            "OpenAI",
            "https://api.openai.com/v1/chat/completions",
            "gpt-4.1-mini",
            ModelApiFormat.OpenAiChat,
            SupportsResponses: true,
            SupportsVision: true),
        ModelProvider.Qwen => new ModelProviderPreset(
            provider,
            "Qwen",
            "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
            "qwen-plus",
            ModelApiFormat.OpenAiChat,
            SupportsResponses: true,
            SupportsVision: true),
        ModelProvider.DeepSeek => new ModelProviderPreset(
            provider,
            "DeepSeek",
            "https://api.deepseek.com/chat/completions",
            "deepseek-v4-flash",
            ModelApiFormat.OpenAiChat,
            SupportsResponses: false,
            SupportsVision: false),
        _ => new ModelProviderPreset(
            ModelProvider.Custom,
            "Custom",
            string.Empty,
            string.Empty,
            ModelApiFormat.OpenAiChat,
            SupportsResponses: true,
            SupportsVision: true)
    };

    public static void ApplyProviderDefaults(ModelApiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        var preset = GetPreset(settings.Provider);
        if (string.IsNullOrWhiteSpace(settings.Endpoint) &&
            settings.Provider != ModelProvider.Custom)
            settings.Endpoint = preset.DefaultEndpoint;
        settings.Endpoint = NormalizeEndpoint(
            settings.Provider,
            settings.ApiFormat,
            settings.Endpoint);
        if (string.IsNullOrWhiteSpace(settings.Model) &&
            !string.IsNullOrWhiteSpace(preset.DefaultModel))
            settings.Model = preset.DefaultModel;
    }

    public static string NormalizeEndpoint(
        ModelProvider provider,
        ModelApiFormat format,
        string? endpoint)
    {
        var value = (endpoint ?? string.Empty).Trim();
        if (value.Length == 0 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;

        var path = uri.AbsolutePath.TrimEnd('/');
        string? suffix = null;
        if (format == ModelApiFormat.OpenAiChat)
        {
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return value.TrimEnd('/');
            if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/compatible-mode/v1", StringComparison.OrdinalIgnoreCase))
                suffix = "/chat/completions";
            else if (path.Length == 0)
            {
                suffix = provider switch
                {
                    ModelProvider.OpenAI => "/v1/chat/completions",
                    ModelProvider.Qwen => "/compatible-mode/v1/chat/completions",
                    ModelProvider.DeepSeek => "/chat/completions",
                    _ => null
                };
            }
        }
        else
        {
            if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
                return value.TrimEnd('/');
            if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/compatible-mode/v1", StringComparison.OrdinalIgnoreCase))
                suffix = "/responses";
        }

        if (suffix is null) return value.TrimEnd('/');
        var builder = new UriBuilder(uri)
        {
            Path = path + suffix
        };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    public static ModelProvider InferLegacyProvider(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return ModelProvider.Custom;
        var host = uri.Host;
        if (host.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
            return ModelProvider.DeepSeek;
        if (host.Contains("dashscope", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("aliyuncs.com", StringComparison.OrdinalIgnoreCase))
            return ModelProvider.Qwen;
        if (host.Contains("openai.com", StringComparison.OrdinalIgnoreCase))
            return ModelProvider.OpenAI;
        return ModelProvider.Custom;
    }

    public static ModelApiFormat InferLegacyApiFormat(string? endpoint) =>
        endpoint?.Contains("/responses", StringComparison.OrdinalIgnoreCase) == true
            ? ModelApiFormat.OpenAiResponses
            : ModelApiFormat.OpenAiChat;

    public string BuildRequestJson(
        ModelApiSettings settings,
        string systemPrompt,
        string ownerMessage,
        IReadOnlyList<ChatMessage>? history = null,
        IReadOnlyList<ModelImageInput>? images = null)
    {
        settings.Normalize();
        var historyWindow = NormalizeHistory(history, settings.ConversationTurns);
        var imageInputs = NormalizeImages(settings, images);
        var model = imageInputs.Count > 0 && !string.IsNullOrWhiteSpace(settings.VisionModel)
            ? settings.VisionModel
            : settings.Model;

        var request = settings.ApiFormat switch
        {
            ModelApiFormat.OpenAiResponses => BuildResponsesRequest(
                settings,
                model,
                systemPrompt,
                ownerMessage,
                historyWindow,
                imageInputs),
            _ => BuildChatRequest(
                settings,
                model,
                systemPrompt,
                ownerMessage,
                historyWindow,
                imageInputs)
        };
        return request.ToJsonString(Json);
    }

    public string ExtractReply(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                var chatText = ExtractContentText(content);
                if (!string.IsNullOrWhiteSpace(chatText)) return chatText;
            }
            if (first.TryGetProperty("text", out var completionText) &&
                completionText.ValueKind == JsonValueKind.String)
                return completionText.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(outputText.GetString()))
            return outputText.GetString() ?? string.Empty;

        if (root.TryGetProperty("output", out var output) &&
            output.ValueKind == JsonValueKind.Array)
        {
            var collected = new List<string>();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var parts)) continue;
                var text = ExtractContentText(parts);
                if (!string.IsNullOrWhiteSpace(text)) collected.Add(text);
            }
            if (collected.Count > 0) return string.Join(Environment.NewLine, collected);
        }

        throw new InvalidOperationException("模型响应中没有可用的文字回复。");
    }

    private static JsonObject BuildChatRequest(
        ModelApiSettings settings,
        string model,
        string systemPrompt,
        string ownerMessage,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ModelImageInput> images)
    {
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            }
        };
        foreach (var item in history)
        {
            messages.Add(new JsonObject
            {
                ["role"] = ToProtocolRole(item.Role),
                ["content"] = item.Text
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = images.Count == 0
                ? JsonValue.Create(ownerMessage)
                : BuildChatUserContent(settings.Provider, ownerMessage, images)
        });

        var request = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["max_tokens"] = settings.MaximumReplyTokens
        };
        if (!settings.OmitTemperature) request["temperature"] = settings.Temperature;
        return request;
    }

    private static JsonObject BuildResponsesRequest(
        ModelApiSettings settings,
        string model,
        string systemPrompt,
        string ownerMessage,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ModelImageInput> images)
    {
        JsonNode input;
        if (history.Count == 0 && images.Count == 0)
        {
            input = JsonValue.Create(ownerMessage)!;
        }
        else
        {
            var messages = new JsonArray();
            foreach (var item in history)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = ToProtocolRole(item.Role),
                    ["content"] = item.Text
                });
            }
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = images.Count == 0
                    ? JsonValue.Create(ownerMessage)
                    : BuildResponsesUserContent(settings.Provider, ownerMessage, images)
            });
            input = messages;
        }

        var request = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = systemPrompt,
            ["input"] = input,
            ["max_output_tokens"] = settings.MaximumReplyTokens
        };
        if (!settings.OmitTemperature) request["temperature"] = settings.Temperature;
        return request;
    }

    private static JsonArray BuildChatUserContent(
        ModelProvider provider,
        string ownerMessage,
        IReadOnlyList<ModelImageInput> images)
    {
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = ownerMessage
            }
        };
        foreach (var image in images)
        {
            var imageUrl = new JsonObject { ["url"] = image.DataUrl };
            if (provider is ModelProvider.OpenAI or ModelProvider.Custom)
                imageUrl["detail"] = image.Detail;
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = imageUrl
            });
        }
        return content;
    }

    private static JsonArray BuildResponsesUserContent(
        ModelProvider provider,
        string ownerMessage,
        IReadOnlyList<ModelImageInput> images)
    {
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "input_text",
                ["text"] = ownerMessage
            }
        };
        foreach (var image in images)
        {
            var item = new JsonObject
            {
                ["type"] = "input_image",
                ["image_url"] = image.DataUrl
            };
            if (provider is ModelProvider.OpenAI or ModelProvider.Custom)
                item["detail"] = image.Detail;
            content.Add(item);
        }
        return content;
    }

    private static IReadOnlyList<ChatMessage> NormalizeHistory(
        IReadOnlyList<ChatMessage>? history,
        int conversationTurns)
    {
        if (history is null || history.Count == 0) return Array.Empty<ChatMessage>();
        return history
            .Where(x => IsConversationRole(x.Role) && !string.IsNullOrWhiteSpace(x.Text))
            .TakeLast(Math.Clamp(conversationTurns, 8, 12) * 2)
            .Select(x => new ChatMessage
            {
                Role = NormalizeStoredRole(x.Role),
                Text = x.Text.Trim(),
                At = x.At
            })
            .ToList();
    }

    private static IReadOnlyList<ModelImageInput> NormalizeImages(
        ModelApiSettings settings,
        IReadOnlyList<ModelImageInput>? images)
    {
        if (!settings.VisionEnabled || !settings.SendAlbumImages ||
            images is null || images.Count == 0)
            return Array.Empty<ModelImageInput>();

        var result = new List<ModelImageInput>();
        foreach (var source in images.Take(2))
        {
            var item = new ModelImageInput
            {
                DataUrl = source.DataUrl,
                Detail = source.Detail
            };
            item.Normalize();
            if (!item.DataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
                !item.DataUrl.Contains(";base64,", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(item);
        }
        return result;
    }

    private static string ExtractContentText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return string.Empty;

        var collected = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                var value = part.GetString();
                if (!string.IsNullOrWhiteSpace(value)) collected.Add(value);
                continue;
            }
            if (part.ValueKind != JsonValueKind.Object) continue;
            if (part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(text.GetString()))
                collected.Add(text.GetString()!);
        }
        return string.Join(Environment.NewLine, collected);
    }

    private static bool IsConversationRole(string? role) =>
        role is not null &&
        (role.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("user", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("pupu", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("assistant", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeStoredRole(string role) =>
        role.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
        role.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? "owner"
            : "pupu";

    private static string ToProtocolRole(string role) =>
        role.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
        role.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? "user"
            : "assistant";
}
