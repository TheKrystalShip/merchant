namespace Merchant.Feeds;

/// <summary>
/// The settings file's vocabulary: every key it may contain, and every limit it is held to.
/// The reader, the validation and the error messages all name their fields from here, so the file
/// format is written down once instead of being spelled out at each place it is read.
/// </summary>
public static class Schema
{
    /// <summary>Everything merchant needs to run, other than the feeds.</summary>
    public const string Bot = "bot";

    /// <summary>The catalog: one entry per feed, named by its key.</summary>
    public const string Feeds = "feeds";

    /// <summary>
    /// Log levels, read by the host rather than by merchant: <c>"logging": { "logLevel": {
    /// "default": "Debug" } }</c>. Recognised here so the one section merchant does not read
    /// itself is not reported as a mistake.
    /// </summary>
    public const string Logging = "logging";

    /// <summary>The sections the file may hold at its top level.</summary>
    public static readonly IReadOnlyList<string> RootKeys = [Bot, Feeds, Logging];

    /// <summary>Keys in the <see cref="Bot"/> section.</summary>
    public static class BotKeys
    {
        public const string DatabasePath = "databasePath";
        public const string SweepMinutes = "sweepMinutes";
        public const string UserAgent = "userAgent";
        public const string DevGuildId = "devGuildId";

        /// <summary>Recognised only so it can be refused: the token comes from the environment.</summary>
        public const string Token = "token";

        /// <summary>Everything the section recognises.</summary>
        public static readonly IReadOnlyList<string> All =
            [DatabasePath, SweepMinutes, UserAgent, DevGuildId, Token];

        /// <summary>The ones worth suggesting: <see cref="Token"/> is recognised only to be refused.</summary>
        public static readonly IReadOnlyList<string> Offered =
            [DatabasePath, SweepMinutes, UserAgent, DevGuildId];
    }

    /// <summary>Keys in one feed entry.</summary>
    public static class FeedKeys
    {
        public const string Label = "label";
        public const string Description = "description";
        public const string Channel = "channel";
        public const string Cadence = "cadence";
        public const string Colour = "colour";
        public const string Enabled = "enabled";
        public const string Source = "source";

        /// <summary>Everything a feed accepts, for reporting a misspelling.</summary>
        public static readonly IReadOnlyList<string> All =
            [Label, Description, Channel, Cadence, Colour, Enabled, Source];
    }

    /// <summary>Keys in a feed's <see cref="FeedKeys.Source"/> block. Which apply depends on its type.</summary>
    public static class SourceKeys
    {
        public const string Type = "type";
        public const string Urls = "urls";
        public const string UpperPrice = "upperPrice";
        public const string MinMetacritic = "minMetacritic";
        public const string SortBy = "sortBy";
    }

    /// <summary>The bounds a setting is held to.</summary>
    public static class Limits
    {
        /// <summary>
        /// Discord rejects an entire autocomplete response containing a longer name or value, so
        /// one overlong label takes the menu down for every feed rather than for its own.
        /// </summary>
        public const int MenuText = 100;

        /// <summary>Longer than this stops fitting the fields of <c>/merchant help</c>.</summary>
        public const int DescriptionText = 400;

        public const int MinSweepMinutes = 5;
        public const int MaxSweepMinutes = 720;
        public const int MinMetacritic = 0;
        public const int MaxMetacritic = 100;
    }

    /// <summary>What a setting means when it is left out.</summary>
    public static class Defaults
    {
        /// <summary>
        /// The ledger's file name. Where it sits is resolved rather than written down here:
        /// <see cref="Merchant.MerchantConfig.ResolveDatabasePath"/>.
        /// </summary>
        public const string DatabaseFileName = "merchant.db";

        public const int SweepMinutes = 30;

        public const string UserAgent =
            "merchant/1.0 (Discord game-deal announcer; +https://github.com/TheKrystalShip/merchant)";

        /// <summary>Discord's own blurple, for a feed that names no colour of its own.</summary>
        public const uint Colour = 0x5865F2;

        /// <summary>A digest a day floods nothing, which is what makes it the safe default.</summary>
        public const Merchant.Cadence Cadence = Merchant.Cadence.Daily;
    }
}
