using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>Pinned to the production limits for the anonymous-abuse surface.</summary>
public sealed class AbuseLimitedAppFactory : WeGoAppFactory
{
    public override int CreateTripPerHour => 5;
    public override int GeocodePerMinute => 30;
}

/// <summary>
/// The endpoints an unauthenticated stranger can reach on a public deployment.
///
/// Trip creation is the only way to consume disk without a session, and place
/// search reaches a shared upstream that enforces its policy by banning the
/// caller — so abuse there costs everyone the feature, not just the abuser.
///
/// Their own factory, as with the join limit: TestServer reports no remote
/// address, so a production-tight limit here would leak into every other test.
/// </summary>
public sealed class AbuseRateLimitTests(AbuseLimitedAppFactory factory)
    : IClassFixture<AbuseLimitedAppFactory>
{
    [Fact]
    public async Task Trip_creation_stops_after_the_hourly_limit()
    {
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
        {
            var client = factory.CreateApiClient();
            var response = await client.PostAsJsonAsync("/trips", new
            {
                name = $"Chuyến {i}",
                destination = "Đà Lạt",
                startDate = "2026-09-01",
                endDate = "2026-09-03",
                ownerDisplayName = "Quân",
            }, ApiClient.Json);

            statuses.Add(response.StatusCode);
        }

        statuses.Take(5).Should().AllBeEquivalentTo(HttpStatusCode.Created);
        statuses[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_refused_request_still_answers_with_the_error_contract()
    {
        // A bare 429 with no body would be the one response in the app that
        // breaks §6, and the client turns `code` into the message it shows.
        for (var i = 0; i < 6; i++)
        {
            await factory.CreateApiClient().PostAsJsonAsync("/trips", new
            {
                name = "Chuyến đi",
                destination = "Sa Pa",
                startDate = "2026-09-01",
                endDate = "2026-09-03",
                ownerDisplayName = "Quân",
            }, ApiClient.Json);
        }

        var refused = await factory.CreateApiClient().PostAsJsonAsync("/trips", new
        {
            name = "Chuyến đi",
            destination = "Sa Pa",
            startDate = "2026-09-01",
            endDate = "2026-09-03",
            ownerDisplayName = "Quân",
        }, ApiClient.Json);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var problem = await refused.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.RateLimited);
        problem.Status.Should().Be(429);
    }

    [Fact]
    public async Task The_health_check_is_never_rate_limited()
    {
        // A throttled health check reads as an unhealthy app, and the platform
        // answers that by restarting the machine — turning a flood into an
        // outage. Exhaust the trip limit first, then prove /health is unaffected.
        for (var i = 0; i < 6; i++)
        {
            await factory.CreateApiClient().PostAsJsonAsync("/trips", new
            {
                name = "Chuyến đi",
                destination = "Huế",
                startDate = "2026-09-01",
                endDate = "2026-09-03",
                ownerDisplayName = "Quân",
            }, ApiClient.Json);
        }

        var health = await factory.CreateApiClient().GetAsync("/health");

        health.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
