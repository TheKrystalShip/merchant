using System.Text.RegularExpressions;

namespace Merchant.Feeds;

/// <summary>
/// The substitution a config string may use: <c>{region}</c> and <c>{currency}</c>, filled per
/// server when a source is built. Deliberately tiny — one feed genuinely needs it, and a template
/// language that grows past that is a scripting language living in a settings file.
/// </summary>
public static partial class Template
{
    /// <summary>The server's two-letter country code, from <c>/merchant region</c>.</summary>
    public const string Region = "region";

    /// <summary>The server's three-letter currency code.</summary>
    public const string Currency = "currency";

    /// <summary>Every placeholder a config string may use.</summary>
    public static readonly IReadOnlyList<string> Tokens = [Region, Currency];

    /// <summary>Checks one config string for a placeholder merchant cannot fill in.</summary>
    public static void Validate(string value, string field, ICollection<string> errors)
    {
        // ${NAME} is reserved for a secrets syntax that does not exist yet. Refusing it stops an
        // API key being pasted in today believing it will be expanded, and it is reported alone
        // because the scan below would also call it an unknown placeholder.
        if (value.Contains("${", StringComparison.Ordinal))
        {
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

    /// <summary>Fills the placeholders in for one server.</summary>
    public static string Fill(string value, GuildSettings settings) => value
        .Replace($"{{{Region}}}", settings.Region, StringComparison.Ordinal)
        .Replace($"{{{Currency}}}", settings.Currency, StringComparison.Ordinal);

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex TokenPattern();
}
