using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>
/// Pasting a map link is the escape hatch for everything OpenStreetMap does not
/// have. Each test builds its own factory because the stub expander records calls.
/// </summary>
public sealed class ResolveLinkTests
{
    private static Task<HttpResponseMessage> ResolveAsync(HttpClient client, Guid tripId, string url) =>
        client.PostAsJsonAsync($"/trips/{tripId}/places/resolve-link", new { url }, ApiClient.Json);

    [Fact]
    public async Task A_full_google_maps_link_yields_the_pin_and_its_name()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Paster");

        var response = await ResolveAsync(client, trip.Trip.Id,
            "https://www.google.com/maps/place/Th%C3%A1c+D%E1%BA%A3i+Y%E1%BA%BFm/"
            + "@20.8200000,104.5900000,17z/data=!4m6!3m5!8m2!3d20.817975!4d104.591686");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GeocodeResultResponse>(ApiClient.Json);

        result!.Name.Should().Be("Thác Dải Yếm");
        result.Lat.Should().BeApproximately(20.817975, 0.000001);
        result.Lng.Should().BeApproximately(104.591686, 0.000001);
        result.Kind.Should().Be("link");

        factory.LinkExpander.Calls.Should().BeEmpty("a full link needs no network round trip");
    }

    [Fact]
    public async Task A_shortened_link_is_expanded_first()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Shortener");

        var response = await ResolveAsync(client, trip.Trip.Id, "https://maps.app.goo.gl/AbCdEf123");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GeocodeResultResponse>(ApiClient.Json);

        result!.Lat.Should().BeApproximately(20.817975, 0.000001);
        factory.LinkExpander.Calls.Should().ContainSingle()
            .Which.Host.Should().Be("maps.app.goo.gl");
    }

    [Fact]
    public async Task A_shortened_link_that_cannot_be_followed_is_reported_not_crashed()
    {
        using var factory = new WeGoAppFactory();
        factory.LinkExpander.ExpandsTo = null;

        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Broken");

        var response = await ResolveAsync(client, trip.Trip.Id, "https://maps.app.goo.gl/AbCdEf123");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.LinkNotRecognised);
    }

    [Fact]
    public async Task A_bare_coordinate_pair_is_accepted()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Coords");

        var response = await ResolveAsync(client, trip.Trip.Id, "20.8386, 104.6383");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GeocodeResultResponse>(ApiClient.Json);

        result!.Lat.Should().BeApproximately(20.8386, 0.00001);
        result.Name.Should().BeEmpty("a coordinate pair carries no name");
    }

    [Fact]
    public async Task The_distance_from_the_trip_is_reported()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Distance");
        await client.CreatePlaceAsync(trip.Trip.Id, name: "Anchor", lat: 20.8386, lng: 104.6383);

        var response = await ResolveAsync(client, trip.Trip.Id, "20.8500, 104.6500");
        var result = await response.Content.ReadFromJsonAsync<GeocodeResultResponse>(ApiClient.Json);

        result!.DistanceKm.Should().NotBeNull();
        result.DistanceKm!.Value.Should().BeLessThan(5);
    }

    [Theory]
    [InlineData("https://example.com/somewhere")]
    [InlineData("https://www.google.com/maps/place/NoCoordinatesHere")]
    [InlineData("not a link at all")]
    [InlineData("")]
    public async Task Input_with_no_location_is_rejected_with_guidance(string url)
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: $"Bad{url.Length}");

        var response = await ResolveAsync(client, trip.Trip.Id, url);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().BeOneOf(ErrorCodes.LinkNotRecognised, ErrorCodes.ValidationFailed);
    }

    [Theory]
    // The endpoint fetches a user-supplied URL, so anything outside the two
    // Google shorteners must never become an outbound request.
    [InlineData("http://localhost:5080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://maps.app.goo.gl.evil.com/abc")]
    [InlineData("file:///etc/passwd")]
    public async Task A_link_outside_the_allowlist_is_never_fetched(string url)
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: $"SSRF{url.Length}");

        var response = await ResolveAsync(client, trip.Trip.Id, url);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        factory.LinkExpander.Calls.Should().BeEmpty(
            "the server must not make a request to a host the caller chose");
    }

    [Fact]
    public async Task Resolving_requires_membership_of_the_trip()
    {
        using var factory = new WeGoAppFactory();
        var attacker = factory.CreateApiClient();
        await attacker.CreateTripAsync(ownerDisplayName: "Attacker");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "Victim");

        var response = await ResolveAsync(attacker, victim.Trip.Id, "20.8386, 104.6383");

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
        factory.LinkExpander.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolving_requires_a_session()
    {
        using var factory = new WeGoAppFactory();
        var owner = factory.CreateApiClient();
        var trip = await owner.CreateTripAsync(ownerDisplayName: "Owner");

        var anonymous = factory.CreateApiClient();
        var response = await ResolveAsync(anonymous, trip.Trip.Id, "https://maps.app.goo.gl/abc");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
        factory.LinkExpander.Calls.Should().BeEmpty(
            "an unauthenticated caller must not be able to make the server fetch a URL");
    }

    [Fact]
    public async Task A_resolved_link_can_be_saved_as_a_place()
    {
        // The whole point: a place OpenStreetMap has never heard of still ends
        // up on the wishlist.
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "EndToEnd");

        var resolved = await (await ResolveAsync(client, trip.Trip.Id, "https://maps.app.goo.gl/AbCdEf123"))
            .Content.ReadFromJsonAsync<GeocodeResultResponse>(ApiClient.Json);

        var place = await client.CreatePlaceAsync(
            trip.Trip.Id, name: resolved!.Name, lat: resolved.Lat, lng: resolved.Lng);

        place.Name.Should().Be("Thác Dải Yếm");
        place.Lat.Should().BeApproximately(20.817975, 0.000001);
    }
}
