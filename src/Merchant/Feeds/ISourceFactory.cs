using Merchant.Sources;
using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds;

/// <summary>
/// Builds one kind of source from its block of the settings file. A factory owns both halves of its
/// driver's contract — what its options mean and what a bad value looks like — so adding a driver is
/// one class and one registration, with no switch anywhere to keep in step.
///
/// Factories live here rather than in <c>Sources/</c> on purpose: a source fetches, and does not
/// know where its URL came from.
/// </summary>
public interface ISourceFactory
{
    /// <summary>The <c>type</c> a feed names to ask for this driver.</summary>
    string Type { get; }

    /// <summary>The options it accepts, besides <see cref="Schema.SourceKeys.Type"/>.</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>Reads and checks one <c>source</c> block.</summary>
    /// <param name="source">The feed's <c>source</c> section.</param>
    /// <param name="errors">Appended to when something is wrong; the caller prefixes the feed key.</param>
    /// <returns>
    /// A blueprint, or null when the block was rejected and <paramref name="errors"/> says why.
    /// </returns>
    ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors);
}

/// <summary>
/// A source definition that has been read and validated, waiting only for the things that vary per
/// server. Holding this rather than the raw config means everything that can be wrong with a feed
/// has already been said out loud at startup, so nothing fails for the first time during a sweep.
/// </summary>
public interface ISourceBlueprint
{
    /// <summary>
    /// The driver that built it, for diagnostics and for questions like "which feeds are priced in USD".
    /// </summary>
    string Type { get; }

    /// <summary>Builds the source for one server, filling in its region and currency.</summary>
    ISource Build(HttpClient http, GuildSettings settings);
}
