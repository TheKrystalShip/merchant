using System.Net;
using Discord.Net;
using Merchant.Discord;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// Telling a refused token from a bad afternoon. Getting this wrong in either direction is bad:
/// treat a 401 as transient and merchant runs forever posting nothing, treat an outage as fatal and
/// it gives up on a Discord that would have come back in a minute.
/// </summary>
public class GatewayFailureTests
{
    private static HttpException Refused() =>
        new(HttpStatusCode.Unauthorized, request: null!, discordCode: null, reason: "401: Unauthorized");

    [Fact]
    public void A_401_is_the_token_being_refused()
    {
        Assert.True(GatewayFailure.IsUnauthorized(Refused()));
    }

    [Fact]
    public void A_401_is_still_found_when_it_arrives_wrapped()
    {
        // How it actually turns up: the gateway's connect failure carries the REST call's exception
        // underneath it, sometimes more than one deep.
        Assert.True(GatewayFailure.IsUnauthorized(
            new InvalidOperationException(
                "connecting", new HttpRequestException("gateway", Refused()))));
    }

    [Fact]
    public void A_401_is_found_inside_an_aggregate()
    {
        Assert.True(GatewayFailure.IsUnauthorized(
            new AggregateException(new TimeoutException(), Refused())));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Every_other_status_is_something_to_wait_out(HttpStatusCode code)
    {
        // Discord being unhappy is not the token being wrong, and merchant must not quit over it:
        // reconnecting is exactly right for all of these.
        Assert.False(GatewayFailure.IsUnauthorized(
            new HttpException(code, request: null!, discordCode: null, reason: null)));
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(HttpRequestException))]
    public void An_ordinary_network_failure_is_not_a_refusal(Type type)
    {
        Assert.False(GatewayFailure.IsUnauthorized((Exception)Activator.CreateInstance(type)!));
    }

    [Fact]
    public void Nothing_at_all_is_not_a_refusal()
    {
        // Most gateway log lines carry no exception; this is asked of every one of them.
        Assert.False(GatewayFailure.IsUnauthorized(null));
    }
}
