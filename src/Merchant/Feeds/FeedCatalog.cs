using System.Text.RegularExpressions;
using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds;

/// <summary>
/// Every feed merchant knows how to post, read from the config file at startup.
///
/// Nothing about a feed lives in this assembly any more: its URL, its filters, its label, colour and
/// cadence are all data. Adding one is an edit and a restart, and the three lists that used to have
/// to agree — the catalog, the source switch and the command menu — are one list now.
///
/// A malformed entry is dropped rather than fatal. The person editing this file is often not the
/// person who wrote merchant, and a resident bot going dark at three in the morning over one typo is
/// a worse failure than four working feeds and a loud line in the log. A catalog that ends up empty
/// <em>is</em> fatal: that bot is broken either way and should say so.
/// </summary>
public sealed partial class FeedCatalog
{
    /// <summary>What a feed gets when it does not choose a colour.</summary>
    private const uint DefaultColour = 0x5865F2;

    private readonly Dictionary<string, Category> _categories;
    private readonly Dictionary<string, ISourceBlueprint> _sources;

    private FeedCatalog(
        Dictionary<string, Category> categories,
        Dictionary<string, ISourceBlueprint> sources)
    {
        _categories = categories;
        _sources = sources;

        // Config children come back sorted by key, not in the order the file lists them, so the
        // menu is ordered here rather than pretending the file's order survived.
        All = [.. categories.Values.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The catalog, in the order the menus show it.</summary>
    public IReadOnlyList<Category> All { get; }

    /// <summary>The category with this key, or null when the key is unknown.</summary>
    /// <remarks>
    /// Null is a normal answer, not a bug: a subscription can outlive the feed it names when
    /// somebody removes an entry from the file.
    /// </remarks>
    public Category? Find(string key) =>
        _categories.TryGetValue(key.Trim(), out Category? category) ? category : null;

    /// <summary>The driver behind a feed, e.g. <c>rss</c>. Null when the key is unknown.</summary>
    public string? SourceType(string key) =>
        _sources.TryGetValue(key.Trim(), out ISourceBlueprint? blueprint) ? blueprint.Type : null;

    /// <summary>
    /// Builds the source that fills a feed for one server. Sources are cheap request-builders over
    /// a shared <see cref="HttpClient"/>, so this runs per sweep rather than being held anywhere.
    /// </summary>
    /// <exception cref="ArgumentException">The key is not in the catalog.</exception>
    public ISource SourceFor(string key, HttpClient http, GuildSettings settings) =>
        _sources.TryGetValue(key.Trim(), out ISourceBlueprint? blueprint)
            ? blueprint.Build(http, settings)
            : throw new ArgumentException($"Unknown feed '{key}'.", nameof(key));

    /// <summary>
    /// Reads the <c>feeds</c> section, keeping every entry that is well formed.
    /// </summary>
    /// <param name="feeds">The <c>feeds</c> section of the config file.</param>
    /// <param name="registry">The drivers a feed may name.</param>
    /// <param name="report">What was loaded, what was skipped and why.</param>
    public static FeedCatalog Load(IConfiguration feeds, SourceRegistry registry, out CatalogReport report)
    {
        Dictionary<string, Category> categories = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ISourceBlueprint> sources = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        int configured = 0;
        int disabled = 0;

        foreach (IConfigurationSection entry in feeds.GetChildren())
        {
            List<string> problems = [];
            string key = entry.Key.Trim();

            if (!ConfigRead.Bool(entry, "enabled", true, problems) && problems.Count == 0)
            {
                disabled++;
                continue;
            }

            configured++;

            if (!KeyPattern().IsMatch(key))
            {
                problems.Add("the name of a feed should be lower-case words joined by hyphens, like free-games.");
            }
            else if (key.Length > 100)
            {
                // The key is the value the menu sends back, and Discord rejects the whole
                // autocomplete response when one is longer than this.
                problems.Add("the name of a feed cannot be longer than 100 characters.");
            }

            string? label = ConfigRead.Required(entry, "label", problems);
            string? description = ConfigRead.Required(entry, "description", problems);
            string channel = ConfigRead.Optional(entry, "channel") ?? key;
            Cadence cadence = ConfigRead.Enum(entry, "cadence", Cadence.Daily, problems);
            uint colour = ConfigRead.Colour(entry, "colour", DefaultColour, problems);

            // Discord caps a choice name at 100 characters and rejects the whole autocomplete
            // response when one is longer, which would take the menu down for every feed at once.
            if (label is { Length: > 100 })
            {
                problems.Add("label is longer than the 100 characters Discord allows in a menu.");
            }

            if (description is { Length: > 400 })
            {
                problems.Add("description is longer than 400 characters, which does not fit /merchant help.");
            }

            ISourceBlueprint? blueprint = registry.Create(entry.GetSection("source"), problems);

            if (problems.Count > 0 || label is null || description is null || blueprint is null)
            {
                errors.AddRange(problems.Select(problem => $"feed '{key}': {problem}"));
                continue;
            }

            categories[key] = new Category(key, label, description, channel, cadence, colour);
            sources[key] = blueprint;
        }

        report = new CatalogReport(categories.Count, configured, disabled, errors);
        return new FeedCatalog(categories, sources);
    }

    /// <summary>
    /// Feed names are lower-case and hyphenated because they are three things at once: the row in
    /// the ledger, the value the menu sends back, and what a person types.
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();
}

/// <summary>What one read of the config file produced.</summary>
/// <param name="Loaded">Feeds that are usable.</param>
/// <param name="Configured">Feeds that were meant to be usable, whether or not they parsed.</param>
/// <param name="Disabled">Feeds switched off with <c>"enabled": false</c>.</param>
/// <param name="Errors">One line per rejected feed, each naming the feed and what to fix.</param>
public sealed record CatalogReport(
    int Loaded,
    int Configured,
    int Disabled,
    IReadOnlyList<string> Errors)
{
    /// <summary>The line worth printing whether or not anything went wrong.</summary>
    public string Summary =>
        $"Loaded {Loaded} of {Configured} feed(s)" + (Disabled > 0 ? $", {Disabled} disabled." : ".");
}
