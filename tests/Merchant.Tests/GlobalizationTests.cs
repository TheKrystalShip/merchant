using System.Globalization;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// Globalization has to stay on. This is a regression test for a real failure, not a hypothetical:
/// with <c>InvariantGlobalization</c> enabled the bot connected, registered its commands and
/// reported itself healthy, then threw on the first GUILD_CREATE and left every command broken.
/// </summary>
public class GlobalizationTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("es-ES")]
    [InlineData("de")]
    public void A_guild_locale_can_be_constructed(string locale)
    {
        // Discord.Net builds one of these from every guild's preferred_locale while handling
        // GUILD_CREATE. Under invariant globalization the constructor throws
        // CultureNotFoundException, the dispatch fails, the guild never enters the client's cache,
        // and every command then fails on a null Context.Guild — which is exactly how this
        // presented: "Unknown Guild" in the log, NullReferenceException in the command.
        CultureInfo culture = new(locale);

        Assert.Equal(locale, culture.Name);
    }

    [Fact]
    public void The_runtime_is_not_in_invariant_mode()
    {
        // The direct assertion, in case a future runtime stops throwing and starts silently
        // handing back the invariant culture instead.
        Assert.NotEqual(CultureInfo.InvariantCulture, new CultureInfo("en-US"));
    }
}
