using System.Text.RegularExpressions;

namespace Pupu.Behavior;

/// <summary>
/// Defines the final, platform-neutral privacy boundary for text sent to a
/// remote language model. Local persistence remains unchanged.
/// </summary>
public sealed class ModelContextPrivacyFilter
{
    public const int DefaultMaximumCharacters = 6000;
    public const string PrivatePathPlaceholder = "【本地路径已隐藏】";

    private static readonly Regex WindowsAbsolutePath = new(
        @"(?<![\p{L}\p{N}_])(?:[A-Za-z]:[\\/]|\\\\)[^\r\n<>|""*?]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnixAbsolutePath = new(
        @"(?<![\p{L}\p{N}_])/(?:Users|home|root|workspace|tmp|private|Volumes|var|opt|mnt)(?:/[^\s\r\n<>|""']+)+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly Regex FileUri = new(
        @"\bfile://[^\s\r\n<>]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    public string Prepare(string? localContext, int maximumCharacters = DefaultMaximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(localContext)) return string.Empty;
        if (maximumCharacters < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                "Maximum context length must be positive.");

        var filtered = FileUri.Replace(localContext, PrivatePathPlaceholder);
        filtered = WindowsAbsolutePath.Replace(filtered, PrivatePathPlaceholder);
        filtered = UnixAbsolutePath.Replace(filtered, PrivatePathPlaceholder);
        filtered = CollapseRepeatedPlaceholders(filtered).Trim();

        if (filtered.Length <= maximumCharacters) return filtered;
        return filtered[..maximumCharacters].TrimEnd() + Environment.NewLine +
               "【其余本地记忆已省略】";
    }

    private static string CollapseRepeatedPlaceholders(string value)
    {
        var doubled = PrivatePathPlaceholder + PrivatePathPlaceholder;
        while (value.Contains(doubled, StringComparison.Ordinal))
            value = value.Replace(doubled, PrivatePathPlaceholder, StringComparison.Ordinal);
        return value;
    }
}
