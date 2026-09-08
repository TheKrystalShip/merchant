using System.Text.RegularExpressions;

namespace Merchant.Feeds;

/// <summary>
/// The small substitution language config strings are allowed to use: <c>{region}</c> and
/// <c>{currency}</c>, filled per server when a source is built.
///
/// It is deliberately tiny. One feed genuinely needs it — IsThereAnyDeal's giveaways are quoted per
/// storefront — and a template language that grows past that is a scripting language nobody asked
/// for living in a settings file.
/// </summary>
public static partial class Template
{
    /// <summary>Every token a config string may use, in the order they are listed in an error.</summary>
    public static readonly IReadOnlyList<string> Tokens = ["region", "currency"];

    /// <summary>
    /// Checks one config string, reporting an unknown <c>{token}</c> or an unsupported
    /// <c>${…}</c> expansion. Reserving the latter keeps a future secrets syntax from being
    /// mistaken for a literal today, and stops an API key being pasted into this file in the
    /// meantime believing it will be expanded.
    /// </summary>
    public static void Validate(string value, string field, ICollection<string> errors)
    {
        if (value.Contains("${", StringComparison.Ordinal))
        {
            // Reported alone: ${NAME} also looks like an unknown {NAME} to the scan below, and one
            // mistake should produce one line to fix rather than two describing each other.
            errors.Add($"{field} uses ${{…}}, which is not supported yet — write the value out in full.");
            return;
        }

        foreach (Match match in TokenPattern().Matches(value))
        {
            string token = match.Groups[1].Value;

            if (!Tokens.Contains(token, StringComparer.Ordinal))
            {
                errors.Add(
                    $"{field} uses unknown placeholder {{{token}}} — " +
                    $"the ones that exist are {{{string.Join("}, {", Tokens)}}}.");
            }
        }
    }

    /// <summary>Fills the tokens in for one server.</summary>
    public static string Fill(string value, GuildSettings settings) => value
        .Replace("{region}", settings.Region, StringComparison.Ordinal)
        .Replace("{currency}", settings.Currency, StringComparison.Ordinal);

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex TokenPattern();
}
