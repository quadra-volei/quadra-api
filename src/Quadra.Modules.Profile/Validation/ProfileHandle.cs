using System.Text.RegularExpressions;

namespace Quadra.Modules.Profile.Validation;

/// <summary>
/// The <c>@handle</c> rule shared by the profile update and the availability check: 3–20
/// characters of a–z, 0–9 and underscore. Handles are case-insensitive, so they are compared
/// and stored in their normalized (trimmed, lowercase, no leading <c>@</c>) form.
/// </summary>
public static partial class ProfileHandle
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    public const string RuleMessage =
        "Handle must be 3 to 20 characters: letters a-z, digits and underscore.";

    [GeneratedRegex("^[a-z0-9_]{3,20}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Pattern();

    /// <summary>Trims, drops one leading <c>@</c> and lowercases. Null becomes empty.</summary>
    public static string Normalize(string? handle)
    {
        var trimmed = (handle ?? string.Empty).Trim();
        if (trimmed.StartsWith('@'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>True when <paramref name="handle"/> is valid once normalized.</summary>
    public static bool IsValid(string? handle) => Pattern().IsMatch(Normalize(handle));
}
