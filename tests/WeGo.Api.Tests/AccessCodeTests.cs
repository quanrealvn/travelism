using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>A deployment that only lets people with the shared code start a trip.</summary>
public sealed class RestrictedAppFactory : WeGoAppFactory
{
    public const string Code = "ban-be-cua-quan";

    public override string CreateTripCode => Code;
}

/// <summary>
/// Creating a trip is the only write an unauthenticated stranger can perform,
/// and the only one that consumes disk. On a public host it is the single open
/// door, so a shared code closes it — without touching how trips are shared,
/// because joining still needs nothing but the invite code.
/// </summary>
public sealed class AccessCodeTests(RestrictedAppFactory factory)
    : IClassFixture<RestrictedAppFactory>
{
    private static object TripBody(string? accessCode) => new
    {
        name = "Đà Lạt cuối tuần",
        destination = "Đà Lạt, Lâm Đồng",
        startDate = "2026-09-01",
        endDate = "2026-09-03",
        ownerDisplayName = "Quân",
        accessCode,
    };

    [Fact]
    public async Task Creating_a_trip_without_the_code_is_refused()
    {
        var response = await factory.CreateApiClient()
            .PostAsJsonAsync("/trips", TripBody(null), ApiClient.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.InvalidAccessCode);
    }

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        var response = await factory.CreateApiClient()
            .PostAsJsonAsync("/trips", TripBody("doan-mo"), ApiClient.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.InvalidAccessCode);
    }

    [Fact]
    public async Task The_right_code_creates_the_trip()
    {
        var response = await factory.CreateApiClient()
            .PostAsJsonAsync("/trips", TripBody(RestrictedAppFactory.Code), ApiClient.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Joining_never_needs_the_code()
    {
        // The whole point of the gate is that it does not touch sharing: a
        // friend given an invite link must get in with nothing else.
        var owner = factory.CreateApiClient();
        var created = await owner.PostAsJsonAsync(
            "/trips", TripBody(RestrictedAppFactory.Code), ApiClient.Json);
        var trip = await created.Content.ReadFromJsonAsync<TripSessionBody>();

        var joined = await factory.CreateApiClient().PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip!.Trip.InviteCode,
            displayName = "Linh",
        }, ApiClient.Json);

        joined.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_config_endpoint_says_a_code_is_needed_without_revealing_it()
    {
        var response = await factory.CreateApiClient().GetAsync("/config");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"requiresAccessCode\":true");
        // The client only needs to know whether to ask, never what the answer is.
        body.Should().NotContain(RestrictedAppFactory.Code);
    }

    private sealed record TripSessionBody(TripBrief Trip);

    private sealed record TripBrief(Guid Id, string InviteCode);
}

/// <summary>The same surface with no code configured — the default everywhere else.</summary>
public sealed class OpenAccessTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task An_open_instance_creates_trips_with_no_code()
    {
        var response = await factory.CreateApiClient().PostAsJsonAsync("/trips", new
        {
            name = "Sa Pa",
            destination = "Lào Cai",
            startDate = "2026-09-01",
            endDate = "2026-09-03",
            ownerDisplayName = "Quân",
        }, ApiClient.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_open_instance_says_so()
    {
        var response = await factory.CreateApiClient().GetAsync("/config");

        (await response.Content.ReadAsStringAsync())
            .Should().Contain("\"requiresAccessCode\":false");
    }
}
