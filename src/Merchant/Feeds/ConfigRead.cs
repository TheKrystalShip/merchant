using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds;

/// <summary>
/// Reads single settings, saying what is wrong rather than throwing.
///
/// <see cref="ConfigurationBinder"/> would do this in one line, but answers a bad value with an
/// exception naming a CLR type. The person reading that message is editing a file by hand and has
/// never seen this code, so every failure here names the field and what was expected.
/// </summary>
public static class ConfigRead
{
    /// <summary>A required string. Reports its absence and returns null.</summary>
    public static string? Required(IConfigurationSection section, string key, ICollection<string> errors)
    {
        if (Optional(section, key) is not { } value)
        {
            errors.Add($"{key} is missing.");
            return null;
        }

        return value;
    }

    /// <summary>An optional string, or null when it is absent or blank.</summary>
    public static string? Optional(IConfigurationSection section, string key) =>
        section[key]?.Trim() is { Length: > 0 } value ? value : null;

    /// <summary>An optional whole number.</summary>
    public static int? Int(IConfigurationSection section, string key, ICollection<string> errors)
    {
        if (Optional(section, key) is not { } raw)
        {
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            errors.Add($"{key} should be a whole number, not '{raw}'.");
            return null;
        }

        return value;
    }

    /// <summary>An optional decimal, written the way money is written.</summary>
    public static decimal? Decimal(IConfigurationSection section, string key, ICollection<string> errors)
    {
        if (Optional(section, key) is not { } raw)
        {
            return null;
        }

        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            errors.Add($"{key} should be a number, e.g. 10 or 9.99, not '{raw}'.");
            return null;
        }

        return value;
    }

    /// <summary>An optional flag, defaulting when absent.</summary>
    public static bool Bool(IConfigurationSection section, string key, bool fallback, ICollection<string> errors)
    {
        if (Optional(section, key) is not { } raw)
        {
            return fallback;
        }

        if (!bool.TryParse(raw, out bool value))
        {
            errors.Add($"{key} should be true or false, not '{raw}'.");
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// An optional enum member, named as the enum spells it. Spaces are ignored, so a value that
    /// reads naturally in the file — <c>Deal Rating</c> — matches <c>DealRating</c>.
    /// </summary>
    public static TEnum Enum<TEnum>(
        IConfigurationSection section, string key, TEnum fallback, ICollection<string> errors)
        where TEnum : struct, Enum
    {
        if (Optional(section, key) is not { } raw)
        {
            return fallback;
        }

        if (!System.Enum.TryParse(raw.Replace(" ", string.Empty), ignoreCase: true, out TEnum value)
            || !System.Enum.IsDefined(value))
        {
            errors.Add(
                $"{key} should be one of {string.Join(", ", System.Enum.GetNames<TEnum>())}, not '{raw}'.");
            return fallback;
        }

        return value;
    }

    /// <summary>An optional embed colour, written the way a person writes one: <c>#1B2838</c>.</summary>
    public static uint Colour(IConfigurationSection section, string key, uint fallback, ICollection<string> errors)
    {
        if (Optional(section, key) is not { } raw)
        {
            return fallback;
        }

        string digits = raw.TrimStart('#');

        if (digits.Length != 6
            || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            errors.Add($"{key} should be a colour like #1B2838, not '{raw}'.");
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// A list of strings. A JSON array arrives as numbered children, so this reads them in order
    /// and drops the blanks a trailing comma leaves behind.
    /// </summary>
    public static IReadOnlyList<string> Strings(IConfigurationSection section, string key) =>
        [.. section.GetSection(key).GetChildren()
            .Select(child => child.Value?.Trim())
            .OfType<string>()
            .Where(value => value.Length > 0)];

    /// <summary>
    /// Reports settings that are not in the schema, at any level the file has — a section of it, or
    /// the file itself. A misspelled key is otherwise the worst mistake this file can hold: it is
    /// ignored in silence, and the default left in its place looks deliberate.
    /// </summary>
    /// <param name="settings">The section, or the whole configuration.</param>
    /// <param name="known">Everything recognised here.</param>
    /// <param name="what">How the thing being read reads in a sentence, e.g. <c>a feed</c>.</param>
    /// <param name="errors">Appended to, one line per key.</param>
    /// <param name="offered">
    /// What to name as the alternatives, when that is narrower than <paramref name="known"/> — a key
    /// recognised only so it can be refused should not be suggested to somebody fixing a typo.
    /// </param>
    /// <returns>How many were reported.</returns>
    public static int Unknown(
        IConfiguration settings,
        IReadOnlyList<string> known,
        string what,
        ICollection<string> errors,
        IReadOnlyList<string>? offered = null)
    {
        int found = 0;

        foreach (IConfigurationSection child in settings.GetChildren())
        {
            if (!known.Contains(child.Key, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"there is no setting '{child.Key}' — " +
                           $"{what} takes {string.Join(", ", offered ?? known)}.");
                found++;
            }
        }

        return found;
    }
}
