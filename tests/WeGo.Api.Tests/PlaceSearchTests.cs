using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;
using WeGo.Infrastructure.Geocoding;

namespace WeGo.Api.Tests;

/// <summary>
/// Each test gets its own factory: the stub geocoder records calls and is
/// mutated per test, so sharing one would let tests interfere.
/// </summary>
public sealed class PlaceSearchTests
{
    [Fact]
    public async Task Search_returns_the_geocoder_results()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Searcher");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<List<GeocodeResultResponse>>(ApiClient.Json);

        results.Should().HaveCount(2);
        results![0].Name.Should().Be("Thác Dải Yếm");
        results[0].Lat.Should().Be(20.8333);
        results[0].Lng.Should().Be(104.6667);
        results[0].Kind.Should().Be("waterfall");
        results[0].DisplayName.Should().Contain("Mộc Châu");
    }

    [Fact]
    public async Task Results_are_ordered_by_distance_from_the_trip()
    {
        using var factory = new WeGoAppFactory();
        factory.Geocoder.Results.Clear();
        // Upstream order puts the far match first, which is exactly what
        // Nominatim does for a Vietnamese name it ranks by "importance".
        factory.Geocoder.Results.Add(
            new GeocodeSearchResult("Kaohsiung street", "…, 高雄市, 臺灣", 22.6273, 120.3014, "road"));
        factory.Geocoder.Results.Add(
            new GeocodeSearchResult("Hang Táu", "Hang Táu, Mộc Châu", 20.8500, 104.6500, "hamlet"));

        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Ranker");
        await client.CreatePlaceAsync(trip.Trip.Id, name: "Anchor", lat: 20.8386, lng: 104.6383);

        var results = await client.GetFromJsonAsync<List<GeocodeResultResponse>>(
            $"/trips/{trip.Trip.Id}/places/search?q=hang+tau", ApiClient.Json);

        results.Should().NotBeNull();
        var ranked = results!;
        ranked.Should().HaveCount(2);
        ranked[0].Name.Should().Be("Hang Táu", "the nearby match belongs on top");
        ranked[0].DistanceKm.Should().BeLessThan(30);
        ranked[1].DistanceKm.Should().BeGreaterThan(1_000, "the Taiwan match is visibly far away");
    }

    [Fact]
    public async Task Results_carry_no_distance_when_the_trip_has_no_places_yet()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "NoAnchor");

        var results = await client.GetFromJsonAsync<List<GeocodeResultResponse>>(
            $"/trips/{trip.Trip.Id}/places/search?q=thac", ApiClient.Json);

        results.Should().NotBeNull();
        results!.Should().NotBeEmpty();
        results!.Should().OnlyContain(r => r.DistanceKm == null);
    }

    [Fact]
    public async Task Upstream_order_is_preserved_when_there_is_nothing_to_measure_from()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Unranked");

        var results = await client.GetFromJsonAsync<List<GeocodeResultResponse>>(
            $"/trips/{trip.Trip.Id}/places/search?q=thac", ApiClient.Json);

        results!.Select(r => r.Name)
            .Should().Equal(factory.Geocoder.Results.Select(r => r.Name));
    }

    [Fact]
    public async Task Search_trims_the_query_before_calling_the_geocoder()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Trimmer");

        await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q={Uri.EscapeDataString("  thác  ")}");

        factory.Geocoder.Calls.Should().ContainSingle()
            .Which.Query.Should().Be("thác");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    public async Task Search_rejects_a_query_that_is_too_short_without_calling_upstream(string query)
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: $"Short{query.Length}");

        var response = await client.GetAsync(
            $"/trips/{trip.Trip.Id}/places/search?q={Uri.EscapeDataString(query)}");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.ValidationFailed);

        factory.Geocoder.Calls.Should().BeEmpty(
            "a query the server will reject must never reach the shared upstream service");
    }

    [Fact]
    public async Task Search_rejects_a_missing_query_parameter()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "NoQuery");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/places/search");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "q");
    }

    [Fact]
    public async Task Search_answers_502_when_the_geocoder_is_down()
    {
        using var factory = new WeGoAppFactory();
        factory.Geocoder.FailWith = new GeocodingUnavailableException("upstream exploded");

        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Unlucky");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");

        await response.ShouldBeAsync(HttpStatusCode.BadGateway);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.GeocodingUnavailable);
    }

    [Fact]
    public async Task A_geocoder_outage_does_not_break_creating_a_place_by_hand()
    {
        using var factory = new WeGoAppFactory();
        factory.Geocoder.FailWith = new GeocodingUnavailableException("upstream exploded");

        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Manual");

        // The whole point of keeping manual coordinates: search is a
        // convenience, not a dependency.
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Nhập tay");

        place.Name.Should().Be("Nhập tay");
    }

    [Fact]
    public async Task Search_is_biased_towards_the_places_already_on_the_trip()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Biased");

        await client.CreatePlaceAsync(trip.Trip.Id, name: "A", lat: 20.0, lng: 104.0);
        await client.CreatePlaceAsync(trip.Trip.Id, name: "B", lat: 22.0, lng: 106.0);

        await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");

        var call = factory.Geocoder.Calls.Should().ContainSingle().Subject;
        call.Near.Should().NotBeNull();
        call.Near!.Value.Lat.Should().BeApproximately(21.0, 0.0001, "centroid of 20.0 and 22.0");
        call.Near.Value.Lng.Should().BeApproximately(105.0, 0.0001, "centroid of 104.0 and 106.0");
    }

    [Fact]
    public async Task Search_has_no_bias_point_on_an_empty_trip()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Empty");

        await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");

        factory.Geocoder.Calls.Should().ContainSingle()
            .Which.Near.Should().BeNull("there is nothing to bias towards yet");
    }

    [Fact]
    public async Task A_soft_deleted_place_does_not_drag_the_bias_point()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "SoftDeleted");

        await client.CreatePlaceAsync(trip.Trip.Id, name: "Keep", lat: 20.0, lng: 104.0);
        var removed = await client.CreatePlaceAsync(trip.Trip.Id, name: "Gone", lat: 50.0, lng: 10.0);
        await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{removed.Id}");

        await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");

        var call = factory.Geocoder.Calls.Should().ContainSingle().Subject;
        call.Near!.Value.Lat.Should().BeApproximately(20.0, 0.0001);
        call.Near.Value.Lng.Should().BeApproximately(104.0, 0.0001);
    }

    [Fact]
    public async Task Search_clamps_an_absurd_limit()
    {
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Greedy");

        await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac&limit=100000");

        factory.Geocoder.Calls.Should().ContainSingle()
            .Which.Limit.Should().Be(WeGo.Domain.Places.GeocodeQuery.MaxLimit);
    }

    [Fact]
    public async Task Search_requires_a_session()
    {
        using var factory = new WeGoAppFactory();
        var owner = factory.CreateApiClient();
        var trip = await owner.CreateTripAsync(ownerDisplayName: "Owner");

        var anonymous = factory.CreateApiClient();
        var response = await anonymous.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
        factory.Geocoder.Calls.Should().BeEmpty("an unauthenticated caller must not be able to relay lookups");
    }

    [Fact]
    public async Task Search_refuses_a_member_of_another_trip()
    {
        using var factory = new WeGoAppFactory();
        var attacker = factory.CreateApiClient();
        await attacker.CreateTripAsync(ownerDisplayName: "Attacker");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "Victim");

        var response = await attacker.GetAsync($"/trips/{victim.Trip.Id}/places/search?q=thac");

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_search_route_does_not_shadow_fetching_a_place_by_id()
    {
        // "search" is a literal segment sharing a prefix with {placeId:guid};
        // this pins that routing still resolves both.
        using var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Router");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Real place");

        var byId = await client.GetAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");
        await byId.ShouldBeAsync(HttpStatusCode.OK);
        (await byId.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json))!.Name
            .Should().Be("Real place");

        var search = await client.GetAsync($"/trips/{trip.Trip.Id}/places/search?q=thac");
        await search.ShouldBeAsync(HttpStatusCode.OK);
    }
}
