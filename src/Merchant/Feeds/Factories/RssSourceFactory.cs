using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds.Factories;

/// <summary>
/// <c>"type": "rss"</c> — one or more syndication feeds, merged.
///
/// <code>
/// "source": { "type": "rss", "urls": [ "https://example.test/feed" ] }
/// </code>
///
/// The parser is namespace-blind and handles RSS 1.0, RSS 2.0 and Atom without being told which is
/// which, so a URL is the whole configuration a feed needs.
/// </summary>
public sealed class RssSourceFactory : ISourceFactory
{
    /// <inheritdoc />
    public string Type => "rss";

    /// <inheritdoc />
    public ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors)
    {
        IReadOnlyList<string> urls = ConfigRead.Strings(source, "urls");

        if (urls.Count == 0)
        {
            errors.Add("source.urls is missing — an rss feed needs at least one URL.");
            return null;
        }

        int before = errors.Count;

        for (int i = 0; i < urls.Count; i++)
        {
            string url = urls[i];
            string field = $"source.urls[{i}]";
            Template.Validate(url, field, errors);

            // Checked after substitution: a placeholder is not a valid URI character, so a URL
            // that only parses once {region} is filled in would fail a naive check here.
            string filled = Template.Fill(url, new GuildSettings(0, "US", "USD"));

            if (!Uri.TryCreate(filled, UriKind.Absolute, out Uri? parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"{field} should be a full http(s) address, not '{url}'.");
            }
        }

        return errors.Count == before ? new Blueprint(urls) : null;
    }

    private sealed class Blueprint(IReadOnlyList<string> urls) : ISourceBlueprint
    {
        public string Type => "rss";

        public ISource Build(HttpClient http, GuildSettings settings) =>
            new RssSource(http, [.. urls.Select(url => Template.Fill(url, settings))]);
    }
}
