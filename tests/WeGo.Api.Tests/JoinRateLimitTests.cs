using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>A factory pinned to the spec's production limit of 10 joins/minute.</summary>
public sealed class RateLimitedAppFactory : WeGoAppFactory
{
    public override int JoinPerMinute => 10;
}

/// <summary>
/// Spec §5.7: 10 join attempts per IP per minute, 429 beyond that.
/// Runs on its own factory so the tight limit cannot leak into other tests —
/// TestServer has no remote IP, so the whole suite would share one partition.
/// </summary>
public sealed class JoinRateLimitTests(RateLimitedAppFactory factory)
    : IClassFixture<RateLimitedAppFactory>
{
    [Fact]
    public async Task The_eleventh_join_attempt_in_a_minute_is_rejected()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Owner");

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        // Deliberately wrong codes: the limiter must count attempts, not
        // successes, or brute-forcing invite codes stays free.
        for (var i = 0; i < 11; i++)
        {
            var client = factory.CreateApiClient();
            var response = await client.PostAsJsonAsync("/trips/join", new
            {
                inviteCode = "ZZZZZZZZ",
                displayName = $"Guesser{i}",
            }, ApiClient.Json);

            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
        }

        statuses.Take(10).Should().AllBeEquivalentTo(HttpStatusCode.NotFound);
        statuses[10].Should().Be(HttpStatusCode.TooManyRequests);

        var problem = await rejected!.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.RateLimited);
        problem.Status.Should().Be(429);

        // A genuine invite code is blocked too, so the limit cannot be bypassed
        // by knowing the answer.
        var blocked = factory.CreateApiClient();
        var afterLimit = await blocked.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "Legit",
        }, ApiClient.Json);

        afterLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_rate_limit_does_not_affect_other_endpoints()
    {
        var client = factory.CreateApiClient();

        // Trip creation and reads stay available even once joins are exhausted.
        for (var i = 0; i < 12; i++)
        {
            var attempt = factory.CreateApiClient();
            await attempt.PostAsJsonAsync("/trips/join", new
            {
                inviteCode = "YYYYYYYY",
                displayName = $"Flood{i}",
            }, ApiClient.Json);
        }

        var trip = await client.CreateTripAsync(ownerDisplayName: "Unaffected");
        var response = await client.GetAsync($"/trips/{trip.Trip.Id}");

        await response.ShouldBeAsync(HttpStatusCode.OK);
    }
}
