using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds.Factories;

/// <summary>What an <c>rss</c> source needs: the feeds to read, merged into one.</summary>
/// <param name="Urls">Full http(s) addresses, which may carry <see cref="Template"/> placeholders.</param>
public sealed record RssOptions(IReadOnlyList<string> Urls);

/// <summary>
/// <c>"type": "rss"</c> — one or more syndication feeds. The parser is namespace-blind and handles
/// RSS 1.0, RSS 2.0 and Atom without being told which, so a URL is the whole configuration.
/// </summary>
public sealed class RssSourceFactory : ISourceFactory
{
    /// <summary>The <c>type</c> a feed names to ask for this driver.</summary>
    public const string TypeName = "rss";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    public IReadOnlyList<string> Keys => [Schema.SourceKeys.Urls];

    /// <inheritdoc />
    public ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors)
    {
        IReadOnlyList<string> urls = ConfigRead.Strings(source, Schema.SourceKeys.Urls);

        if (urls.Count == 0)
        {
            errors.Add($"{Schema.SourceKeys.Urls} is missing — an {TypeName} feed needs at least one URL.");
            return null;
        }

        int before = errors.Count;

        for (int i = 0; i < urls.Count; i++)
        {
            string field = $"{Schema.SourceKeys.Urls}[{i}]";
            Template.Validate(urls[i], field, errors);

            // Checked after substitution: a placeholder is not a valid URI character, so a URL that
            // only parses once {region} is filled in would fail a naive check here.
            string filled = Template.Fill(urls[i], GuildSettings.Default(0));

            if (!Uri.TryCreate(filled, UriKind.Absolute, out Uri? parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"{field} should be a full http(s) address, not '{urls[i]}'.");
            }
        }

        return errors.Count == before ? new Blueprint(new RssOptions(urls)) : null;
    }

    private sealed class Blueprint : ISourceBlueprint
    {
        private readonly RssOptions _options;

        public Blueprint(RssOptions options) => _options = options;

        public string Type => TypeName;

        public ISource Build(HttpClient http, GuildSettings settings) =>
            new RssSource(http, [.. _options.Urls.Select(url => Template.Fill(url, settings))]);
    }
}
