using Microsoft.Extensions.Configuration;

namespace Merchant;

/// <summary>
/// Everything merchant needs to run, from the <c>bot</c> section of the settings file.
///
/// The token is deliberately not here. It is the one secret merchant holds, it is read straight
/// from the environment where the unit file and the container already put it, and keeping it out of
/// this object keeps it out of the container that every command can reach into.
/// </summary>
public sealed class BotOptions
{
    /// <summary>Named client for outbound feed fetches, so the user agent is set in exactly one place.</summary>
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
    /// registered, where global ones take up to an hour, so this is what makes testing bearable.
    /// Unset means register globally.
    /// </summary>
    public ulong? DevGuildId { get; init; }

    /// <summary>
    /// Reads the <c>bot</c> section. Every setting has a working default, so an absent section is
    /// not an error — a settings file that names only feeds is a perfectly good settings file.
    /// </summary>
    /// <param name="config">The whole configuration.</param>
    /// <param name="problems">Appended to when a value could not be read; the defaults stand.</param>
    public static BotOptions Load(IConfiguration config, ICollection<string> problems)
    {
        IConfigurationSection bot = config.GetSection("bot");

        if (bot["token"] is { Length: > 0 })
        {
            problems.Add(
                $"bot.token is ignored and should be deleted — the token is read from " +
                $"{TokenVariable} so it never sits in a file that gets copied around.");
        }

        int minutes = Feeds.ConfigRead.Int(bot, "sweepMinutes", problems) ?? 30;

        if (minutes is < 5 or > 720)
        {
            problems.Add($"bot.sweepMinutes should be between 5 and 720; using 30 instead of {minutes}.");
            minutes = 30;
        }

        return new BotOptions
        {
            DatabasePath = Feeds.ConfigRead.Optional(bot, "databasePath") ?? "merchant.db",
            SweepInterval = TimeSpan.FromMinutes(minutes),
            UserAgent = Feeds.ConfigRead.Optional(bot, "userAgent")
                ?? "merchant/1.0 (Discord game-deal announcer; +https://github.com/TheKrystalShip/merchant)",
            DevGuildId = ReadGuild(bot, problems),
        };
    }

    private static ulong? ReadGuild(IConfigurationSection bot, ICollection<string> problems)
    {
        if (Feeds.ConfigRead.Optional(bot, "devGuildId") is not { } raw)
        {
            return null;
        }

        if (!ulong.TryParse(raw, out ulong guild) || guild == 0)
        {
            problems.Add($"bot.devGuildId should be a Discord server id, not '{raw}'.");
            return null;
        }

        return guild;
    }
}
