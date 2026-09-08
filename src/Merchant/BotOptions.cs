using Merchant.Feeds;
using Microsoft.Extensions.Configuration;

namespace Merchant;

/// <summary>
/// Everything merchant needs to run, from the <see cref="Schema.Bot"/> section.
///
/// The token is not here. It is the one secret merchant holds, it is read straight from the
/// environment where the unit file and the container already put it, and keeping it out of this
/// object keeps it out of the container every command can reach into.
/// </summary>
public sealed class BotOptions
{
    /// <summary>
    /// Named client for outbound feed fetches, so the user agent is set in exactly one place.
    /// </summary>
    public const string HttpClientName = "feeds";

    /// <summary>The environment variable the Discord bot token is read from.</summary>
    public const string TokenVariable = "MERCHANT_TOKEN";

    /// <summary>Where the ledger lives.</summary>
    public required string DatabasePath { get; init; }

    /// <summary>How often every subscribed feed is fetched.</summary>
    public required TimeSpan SweepInterval { get; init; }

    /// <summary>
    /// Sent on every outbound request. CheapShark rejects a generic or absent agent outright, and
    /// Reddit throttles one harder, so this is not decoration.
    /// </summary>
    public required string UserAgent { get; init; }

    /// <summary>
    /// A single guild to register commands into. Guild commands appear the moment they are
    /// registered, where global ones take up to an hour. Unset means register globally.
    /// </summary>
    public ulong? DevGuildId { get; init; }

    /// <summary>
    /// Reads the <see cref="Schema.Bot"/> section. Every setting has a working default, so an absent
    /// section is not an error — a file that names only feeds is a perfectly good settings file.
    /// </summary>
    /// <param name="config">The whole configuration.</param>
    /// <param name="problems">Appended to when a value cannot be read; the defaults stand.</param>
    public static BotOptions Load(IConfiguration config, ICollection<string> problems)
    {
        // The top level, before the section: a file whose feeds are under "feed" parses, validates
        // and starts a bot that announces nothing, and the reason is one letter that nothing else
        // in this file would ever mention.
        ConfigRead.Unknown(config, Schema.RootKeys, "the settings file", problems);

        IConfigurationSection bot = config.GetSection(Schema.Bot);

        ConfigRead.Unknown(
            bot, Schema.BotKeys.All, $"the {Schema.Bot} section", problems, Schema.BotKeys.Offered);

        if (bot[Schema.BotKeys.Token] is { Length: > 0 })
        {
            problems.Add(
                $"{Schema.Bot}.{Schema.BotKeys.Token} is ignored and should be deleted — the token " +
                $"is read from {TokenVariable} so it never sits in a file that gets copied around.");
        }

        int minutes = ConfigRead.Int(bot, Schema.BotKeys.SweepMinutes, problems)
                      ?? Schema.Defaults.SweepMinutes;

        if (minutes is < Schema.Limits.MinSweepMinutes or > Schema.Limits.MaxSweepMinutes)
        {
            problems.Add(
                $"{Schema.Bot}.{Schema.BotKeys.SweepMinutes} should be between " +
                $"{Schema.Limits.MinSweepMinutes} and {Schema.Limits.MaxSweepMinutes}; " +
                $"using {Schema.Defaults.SweepMinutes} instead of {minutes}.");

            minutes = Schema.Defaults.SweepMinutes;
        }

        return new BotOptions
        {
            DatabasePath = ConfigRead.Optional(bot, Schema.BotKeys.DatabasePath)
                ?? MerchantConfig.ResolveDatabasePath(),
            SweepInterval = TimeSpan.FromMinutes(minutes),
            UserAgent = ConfigRead.Optional(bot, Schema.BotKeys.UserAgent) ?? Schema.Defaults.UserAgent,
            DevGuildId = ReadGuild(bot, problems),
        };
    }

    private static ulong? ReadGuild(IConfigurationSection bot, ICollection<string> problems)
    {
        if (ConfigRead.Optional(bot, Schema.BotKeys.DevGuildId) is not { } raw)
        {
            return null;
        }

        if (!ulong.TryParse(raw, out ulong guild) || guild == 0)
        {
            problems.Add(
                $"{Schema.Bot}.{Schema.BotKeys.DevGuildId} should be a Discord server id, not '{raw}'.");
            return null;
        }

        return guild;
    }
}
