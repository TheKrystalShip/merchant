namespace Merchant;

/// <summary>
/// Everything merchant needs to start, read from the environment.
///
/// Environment variables and nothing else: the same build runs from a systemd unit today and a
/// container on somebody else's host later, with no settings file to carry across.
/// </summary>
public sealed class BotOptions
{
    /// <summary>Named client for outbound feed fetches, so the user agent is set in exactly one place.</summary>
    public const string HttpClientName = "feeds";

    /// <summary>The Discord bot token.</summary>
    public required string Token { get; init; }

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
    /// Reads the options from the environment.
    /// </summary>
    /// <exception cref="InvalidOperationException">The token is missing.</exception>
    public static BotOptions FromEnvironment()
    {
        string? token = Environment.GetEnvironmentVariable("MERCHANT_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "MERCHANT_TOKEN is not set. Create an application at https://discord.com/developers, " +
                "add a bot to it, and put its token in MERCHANT_TOKEN.");
        }

        return new BotOptions
        {
            Token = token.Trim(),
            DatabasePath = Environment.GetEnvironmentVariable("MERCHANT_DB") is { Length: > 0 } db
                ? db
                : "merchant.db",
            SweepInterval = TimeSpan.FromMinutes(
                int.TryParse(Environment.GetEnvironmentVariable("MERCHANT_SWEEP_MINUTES"), out int m)
                && m is >= 5 and <= 720
                    ? m
                    : 30),
            UserAgent = Environment.GetEnvironmentVariable("MERCHANT_USER_AGENT") is { Length: > 0 } ua
                ? ua
                : "merchant/1.0 (Discord game-deal announcer; +https://github.com/TheKrystalShip/merchant)",
            DevGuildId = ulong.TryParse(
                Environment.GetEnvironmentVariable("MERCHANT_DEV_GUILD"), out ulong g) && g > 0
                    ? g
                    : null,
        };
    }
}
