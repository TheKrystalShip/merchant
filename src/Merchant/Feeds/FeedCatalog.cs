using System.Text.RegularExpressions;
using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds;

/// <summary>
/// Every feed merchant can post, read from the settings file at startup. Nothing about a feed lives
/// in this assembly: its URL, filters, label, colour and cadence are all data.
///
/// A malformed entry is dropped rather than fatal — a resident bot going dark at three in the
/// morning over one typo is a worse failure than four working feeds and a loud line in the log. A
/// catalog that ends up empty <em>is</em> fatal: that bot is broken either way and should say so.
/// </summary>
public sealed partial class FeedCatalog
{
    private readonly Dictionary<string, Category> _categories;
    private readonly Dictionary<string, ISourceBlueprint> _sources;

    private FeedCatalog(
        Dictionary<string, Category> categories,
        Dictionary<string, ISourceBlueprint> sources)
    {
        _categories = categories;
        _sources = sources;

        // Config children arrive sorted by key rather than in the order the file lists them, so the
        // menu is ordered here instead of pretending the file's own order survived the read.
        All = [.. categories.Values.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The catalog, in the order the menus show it.</summary>
    public IReadOnlyList<Category> All { get; }

    /// <summary>
    /// The category with this key, or null when the key is unknown — which is a normal answer, not a
    /// bug: a subscription outlives the feed it names when somebody removes an entry from the file.
    /// </summary>
    public Category? Find(string key) =>
        _categories.TryGetValue(key.Trim(), out Category? category) ? category : null;

    /// <summary>The driver behind a feed. Null when the key is unknown.</summary>
    public string? SourceType(string key) =>
        _sources.TryGetValue(key.Trim(), out ISourceBlueprint? blueprint) ? blueprint.Type : null;

    /// <summary>
    /// Builds the source that fills a feed for one server. Sources are cheap request-builders over a
    /// shared <see cref="HttpClient"/>, so this runs per sweep rather than being held anywhere.
    /// </summary>
    /// <exception cref="ArgumentException">The key is not in the catalog.</exception>
    public ISource SourceFor(string key, HttpClient http, GuildSettings settings) =>
        _sources.TryGetValue(key.Trim(), out ISourceBlueprint? blueprint)
            ? blueprint.Build(http, settings)
            : throw new ArgumentException($"Unknown feed '{key}'.", nameof(key));

    /// <summary>
    /// Reads the <see cref="Schema.Feeds"/> section, keeping every entry that is well formed.
    /// </summary>
    /// <param name="feeds">The <see cref="Schema.Feeds"/> section of the settings file.</param>
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

            // Taken as written. Trimming it here would let "free-games " and "free-games" both
            // resolve to one feed, the second quietly replacing the first with no line to say so —
            // and which of them survives would be decided by the order configuration hands its
            // children back rather than by the file. The pattern below refuses the stray space
            // instead, and names the feed with its whitespace showing inside the quotes.
            string key = entry.Key;

            if (!ConfigRead.Bool(entry, Schema.FeedKeys.Enabled, true, problems) && problems.Count == 0)
            {
                disabled++;
                continue;
            }

            configured++;

            if (Read(entry, key, registry, problems) is { } feed)
            {
                categories[key] = feed.Category;
                sources[key] = feed.Source;
            }
            else
            {
                errors.AddRange(problems.Select(problem => $"feed '{key}': {problem}"));
            }
        }

        report = new CatalogReport(categories.Count, configured, disabled, errors);
        return new FeedCatalog(categories, sources);
    }

    /// <summary>One entry, or null when <paramref name="problems"/> says why not.</summary>
    private static (Category Category, ISourceBlueprint Source)? Read(
        IConfigurationSection entry, string key, SourceRegistry registry, List<string> problems)
    {
        ConfigRead.Unknown(entry, Schema.FeedKeys.All, "a feed", problems);

        if (!KeyPattern().IsMatch(key))
        {
            problems.Add("the name of a feed should be lower-case words joined by hyphens, like free-games.");
        }
        else if (key.Length > Schema.Limits.MenuText)
        {
            problems.Add($"the name of a feed cannot be longer than {Schema.Limits.MenuText} characters.");
        }

        string? label = ConfigRead.Required(entry, Schema.FeedKeys.Label, problems);
        string? description = ConfigRead.Required(entry, Schema.FeedKeys.Description, problems);

        if (label is { } l && l.Length > Schema.Limits.MenuText)
        {
            problems.Add($"{Schema.FeedKeys.Label} is longer than the " +
                         $"{Schema.Limits.MenuText} characters Discord allows in a menu.");
        }

        if (description is { } d && d.Length > Schema.Limits.DescriptionText)
        {
            problems.Add($"{Schema.FeedKeys.Description} is longer than " +
                         $"{Schema.Limits.DescriptionText} characters, which does not fit /merchant help.");
        }

        string channel = ConfigRead.Optional(entry, Schema.FeedKeys.Channel) ?? key;
        Cadence cadence = ConfigRead.Enum(entry, Schema.FeedKeys.Cadence, Schema.Defaults.Cadence, problems);
        uint colour = ConfigRead.Colour(entry, Schema.FeedKeys.Colour, Schema.Defaults.Colour, problems);

        ISourceBlueprint? source = registry.Create(entry.GetSection(Schema.FeedKeys.Source), problems);

        return problems.Count > 0 || label is null || description is null || source is null
            ? null
            : (new Category(key, label, description, channel, cadence, colour), source);
    }

    /// <summary>
    /// A feed's name is three things at once: the row in the ledger, the value the menu sends back,
    /// and what a person types. Lower case and hyphens are what all three accept.
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();
}

/// <summary>What one read of the settings file produced.</summary>
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
