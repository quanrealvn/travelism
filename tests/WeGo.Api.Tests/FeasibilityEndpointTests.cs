using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;
using WeGo.Domain.Itinerary;
using WeGo.Infrastructure.Routing;

namespace WeGo.Api.Tests;

/// <summary>
/// Spec §5.2 and §5.4 end to end, including reviewer step 8's failure
/// injection: OSRM is stubbed and its outage must not break the endpoint.
/// </summary>
public sealed class FeasibilityEndpointTests
{
    private static readonly DateOnly Day = new(2026, 3, 1);

    private static async Task<(WeGoAppFactory Factory, HttpClient Client, Guid TripId)> ArrangeAsync()
    {
        var factory = new WeGoAppFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "Planner", startDate: Day, endDate: Day.AddDays(3));

        return (factory, client, trip.Trip.Id);
    }

    private static async Task<Guid> ScheduleAsync(
        HttpClient client,
        Guid tripId,
        string name,
        double lat,
        double lng,
        string? startTime,
        int durationMinutes = 60,
        string[]? slots = null)
    {
        var place = await client.CreatePlaceAsync(
            tripId,
            name: name,
            lat: lat,
            lng: lng,
            timeSlots: slots ?? ["Morning", "Noon", "Afternoon", "Evening"],
            estimatedDurationMinutes: durationMinutes);

        var response = await client.PostAsJsonAsync(
            $"/trips/{tripId}/itinerary",
            new { placeId = place.Id, date = Day, startTime },
            ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var item = await response.Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);
        return item!.Id;
    }

    private static async Task<FeasibilityResponse> FeasibilityAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync($"/trips/{tripId}/itinerary/feasibility?date=2026-03-01");
        await response.ShouldBeAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FeasibilityResponse>(ApiClient.Json))!;
    }

    [Fact]
    public async Task An_empty_day_is_feasible()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        (await FeasibilityAsync(client, tripId)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_overlap_is_reported_as_an_error()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "First", 20.80, 104.60, "09:00:00", durationMinutes: 120);
        var second = await ScheduleAsync(client, tripId, "Second", 20.81, 104.61, "10:00:00");

        var findings = await FeasibilityAsync(client, tripId);

        var overlap = findings.Items.Single(f => f.Code == FeasibilityCodes.Overlap);
        overlap.Level.Should().Be("error");
        overlap.ItineraryItemId.Should().Be(second);
    }

    [Fact]
    public async Task A_gap_shorter_than_the_drive_is_reported_with_its_source()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;
        factory.Routes.Result = new RouteResult(45, 20_000);

        await ScheduleAsync(client, tripId, "A", 20.80, 104.60, "09:00:00");
        await ScheduleAsync(client, tripId, "B", 21.20, 105.20, "10:10:00");

        var findings = await FeasibilityAsync(client, tripId);

        var insufficient = findings.Items.Single(f => f.Code == FeasibilityCodes.InsufficientTravelTime);
        insufficient.Level.Should().Be("error");
        insufficient.Data["travelMinutes"]!.ToString().Should().Be("45");
        insufficient.Data["source"]!.ToString().Should().Be("osrm");
    }

    [Fact]
    public async Task An_osrm_outage_falls_back_to_an_estimate_and_says_so()
    {
        // Reviewer step 8: timeout, 500 and 200-with-no-route all arrive here
        // as null, and none of them may break the endpoint.
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;
        factory.Routes.Result = null;

        await ScheduleAsync(client, tripId, "A", 20.80, 104.60, "09:00:00");
        await ScheduleAsync(client, tripId, "B", 21.20, 105.20, "10:05:00");

        var findings = await FeasibilityAsync(client, tripId);

        var insufficient = findings.Items.Single(f => f.Code == FeasibilityCodes.InsufficientTravelTime);
        insufficient.Data["source"]!.ToString().Should().Be(
            "haversine", "the UI has to be able to show this as an estimate");
    }

    [Fact]
    public async Task An_osrm_outage_does_not_break_unrelated_endpoints()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;
        factory.Routes.Result = null;

        await ScheduleAsync(client, tripId, "A", 20.80, 104.60, "09:00:00");

        var places = await client.GetAsync($"/trips/{tripId}/places");
        await places.ShouldBeAsync(HttpStatusCode.OK);

        var itinerary = await client.GetAsync($"/trips/{tripId}/itinerary");
        await itinerary.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Travel_times_are_cached_so_a_second_read_makes_no_calls()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "A", 20.80, 104.60, "09:00:00");
        await ScheduleAsync(client, tripId, "B", 20.90, 104.70, "11:00:00");

        await FeasibilityAsync(client, tripId);
        var afterFirst = factory.Routes.Calls;
        afterFirst.Should().Be(1, "one pair means at most one lookup");

        await FeasibilityAsync(client, tripId);
        factory.Routes.Calls.Should().Be(afterFirst, "the second read is served from cache");
    }

    [Fact]
    public async Task A_day_of_n_items_makes_at_most_n_minus_one_lookups()
    {
        // Spec §5.4 states the bound explicitly.
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "A", 20.80, 104.60, "08:00:00", durationMinutes: 30);
        await ScheduleAsync(client, tripId, "B", 20.85, 104.65, "10:00:00", durationMinutes: 30);
        await ScheduleAsync(client, tripId, "C", 20.90, 104.70, "12:00:00", durationMinutes: 30);
        await ScheduleAsync(client, tripId, "D", 20.95, 104.75, "14:00:00", durationMinutes: 30);

        await FeasibilityAsync(client, tripId);

        factory.Routes.Calls.Should().Be(3, "four items form three consecutive pairs");
    }

    [Fact]
    public async Task Moving_a_place_invalidates_its_cached_leg()
    {
        // Spec §7.4 again, now visible through feasibility: after a move the
        // route must be fetched afresh rather than served stale.
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        var placeA = await client.CreatePlaceAsync(tripId, name: "A", lat: 20.80, lng: 104.60);
        var placeB = await client.CreatePlaceAsync(tripId, name: "B", lat: 20.90, lng: 104.70);

        foreach (var (id, time) in new[] { (placeA.Id, "09:00:00"), (placeB.Id, "11:00:00") })
        {
            await client.PostAsJsonAsync(
                $"/trips/{tripId}/itinerary",
                new { placeId = id, date = Day, startTime = time },
                ApiClient.Json);
        }

        await FeasibilityAsync(client, tripId);
        var before = factory.Routes.Calls;

        await client.PatchJsonAsync($"/trips/{tripId}/places/{placeA.Id}", """{"lat":20.5,"lng":104.2}""");

        await FeasibilityAsync(client, tripId);
        factory.Routes.Calls.Should().BeGreaterThan(before, "the cached leg was invalidated by the move");
    }

    [Fact]
    public async Task An_item_with_no_time_is_reported_and_not_paired()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "Timed", 20.80, 104.60, "09:00:00");
        var untimed = await ScheduleAsync(client, tripId, "Untimed", 21.50, 105.50, null);

        var findings = await FeasibilityAsync(client, tripId);

        findings.Items.Should().ContainSingle()
            .Which.Code.Should().Be(FeasibilityCodes.UnscheduledTime);
        findings.Items[0].ItineraryItemId.Should().Be(untimed);
        factory.Routes.Calls.Should().Be(0, "an untimed item forms no pair to look up");
    }

    [Fact]
    public async Task A_time_outside_the_places_slots_is_a_warning()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "Breakfast", 20.80, 104.60, "20:00:00", slots: ["Morning"]);

        var findings = await FeasibilityAsync(client, tripId);

        var mismatch = findings.Items.Single(f => f.Code == FeasibilityCodes.TimeSlotMismatch);
        mismatch.Level.Should().Be("warning");
    }

    [Fact]
    public async Task An_item_crossing_midnight_is_reported()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "Late", 20.80, 104.60, "23:30:00", durationMinutes: 90);

        var findings = await FeasibilityAsync(client, tripId);

        findings.Items.Should().Contain(f => f.Code == FeasibilityCodes.CrossesMidnight);
    }

    [Fact]
    public async Task Feasibility_never_blocks_a_write()
    {
        // Spec §5.2: it is a pure read. An impossible day must still be savable.
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        await ScheduleAsync(client, tripId, "First", 20.80, 104.60, "09:00:00", durationMinutes: 300);
        await ScheduleAsync(client, tripId, "Second", 21.50, 105.50, "09:30:00");

        var findings = await FeasibilityAsync(client, tripId);
        findings.Items.Should().Contain(f => f.Level == "error");

        var stored = await factory.WithDbAsync(db => db.ItineraryItems.CountAsync(i => i.TripId == tripId));
        stored.Should().Be(2, "both items are saved despite the plan being impossible");
    }

    [Fact]
    public async Task A_date_outside_the_trip_is_refused()
    {
        var (factory, client, tripId) = await ArrangeAsync();
        using var _ = factory;

        var response = await client.GetAsync($"/trips/{tripId}/itinerary/feasibility?date=2026-05-01");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.DateOutOfRange);
    }

    [Fact]
    public async Task Feasibility_requires_membership()
    {
        var (factory, _, _) = await ArrangeAsync();
        using var _f = factory;

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "FeasVictim", name: "Victim");

        var attacker = factory.CreateApiClient();
        await attacker.CreateTripAsync(ownerDisplayName: "FeasAttacker", name: "Attacker");

        var response = await attacker.GetAsync(
            $"/trips/{victim.Trip.Id}/itinerary/feasibility?date=2026-03-01");

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }
}
