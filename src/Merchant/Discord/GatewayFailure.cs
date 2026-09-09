using System.Net;
using Discord.Net;

namespace Merchant.Discord;

/// <summary>
/// Telling the one unrecoverable gateway failure apart from every recoverable one.
///
/// Discord.Net reconnects on anything: a dropped connection, a Discord outage, a DNS hiccup. That
/// is right for all of them but one. A 401 means the token is wrong or has been reset, no amount of
/// waiting changes it, and retrying forever is how a bot ends up running, silent, and printing a
/// stack trace into a log nobody is reading.
/// </summary>
internal static class GatewayFailure
{
    /// <summary>
    /// Whether this failure is Discord refusing the token.
    ///
    /// The status arrives wrapped as often as not — the gateway's connect failure surfaces as the
    /// REST exception underneath it, and the connection manager may hand over an aggregate — so
    /// this looks through rather than at.
    /// </summary>
    public static bool IsUnauthorized(Exception? error) => error switch
    {
        null => false,
        HttpException { HttpCode: HttpStatusCode.Unauthorized } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsUnauthorized),
        _ => IsUnauthorized(error.InnerException),
    };
}
