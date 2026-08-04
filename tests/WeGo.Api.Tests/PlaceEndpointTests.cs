using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Api.Tests;

public sealed class PlaceEndpointTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task Creating_a_place_stores_it_as_an_idea_and_returns_it()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Creator");

        var place = await client.CreatePlaceAsync(
            trip.Trip.Id,
            name: "Đồi chè trái tim",
            category: "Photo",
            timeSlots: ["Morning", "Evening"],
            estimatedCost: 50_000);

        place.Status.Should().Be(nameof(PlaceStatus.Idea));
        place.TimeSlots.Should().Equal("Morning", "Evening");
        place.EstimatedCost.Should().Be(50_000);
        place.IsDeleted.Should().BeFalse();
        place.LikedByMemberIds.Should().BeEmpty();
        place.UpdatedByMemberId.Should().Be(trip.Session.MemberId);
    }

    [Fact]
    public async Task Listing_places_excludes_soft_deleted_rows_by_default()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Lister");
        var kept = await client.CreatePlaceAsync(trip.Trip.Id, name: "Kept");
        var removed = await client.CreatePlaceAsync(trip.Trip.Id, name: "Removed");

        await (await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{removed.Id}"))
            .ShouldBeAsync(HttpStatusCode.OK);

        var visible = await client.GetFromJsonAsync<List<PlaceResponse>>(
            $"/trips/{trip.Trip.Id}/places", ApiClient.Json);

        visible!.Select(p => p.Id).Should().Equal(kept.Id);
    }

    [Fact]
    public async Task Listing_with_includeDeleted_returns_them_marked()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Includer");
        var removed = await client.CreatePlaceAsync(trip.Trip.Id, name: "Removed");

        await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{removed.Id}");

        var all = await client.GetFromJsonAsync<List<PlaceResponse>>(
            $"/trips/{trip.Trip.Id}/places?includeDeleted=true", ApiClient.Json);

        all!.Should().ContainSingle(p => p.Id == removed.Id)
            .Which.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_soft_deleted_place_is_no_longer_readable_by_id()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Reader");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");
        await response.ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_the_same_place_twice_is_a_not_found_rather_than_a_crash()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "DoubleDeleter");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await (await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}"))
            .ShouldBeAsync(HttpStatusCode.OK);
        await (await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}"))
            .ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_scheduled_place_without_force_conflicts_and_lists_the_dates()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "Scheduler",
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 10));
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await SeedItineraryAsync(trip.Trip.Id, place.Id, trip.Session.MemberId,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 5));

        var response = await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.PlaceInUse);
        problem.Detail.Should().Contain("2 day");

        var stillThere = await client.GetAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");
        await stillThere.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Force_deleting_a_scheduled_place_removes_items_and_cache_and_logs_once()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "Forcer",
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 10));
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Doomed");
        var other = await client.CreatePlaceAsync(trip.Trip.Id, name: "Survivor", lat: 21.0, lng: 105.0);

        await SeedItineraryAsync(trip.Trip.Id, place.Id, trip.Session.MemberId,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 5));
        await SeedTravelCacheAsync(trip.Trip.Id, place.Id, other.Id);
        await SeedTravelCacheAsync(trip.Trip.Id, other.Id, place.Id);

        var logsBefore = await factory.WithDbAsync(db =>
            db.ActivityLogs.CountAsync(a => a.TripId == trip.Trip.Id));

        var response = await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}?force=true");
        await response.ShouldBeAsync(HttpStatusCode.OK);

        await factory.WithDbAsync(async db =>
        {
            var stored = await db.Places.IgnoreQueryFilters().SingleAsync(p => p.Id == place.Id);
            stored.IsDeleted.Should().BeTrue("places are only ever soft-deleted");

            var items = await db.ItineraryItems.IgnoreQueryFilters()
                .Where(i => i.PlaceId == place.Id).ToListAsync();
            items.Should().BeEmpty("scheduled items are hard-deleted with force");

            var cache = await db.TravelTimeCaches
                .Where(c => c.FromPlaceId == place.Id || c.ToPlaceId == place.Id)
                .ToListAsync();
            cache.Should().BeEmpty("cache rows go in both directions (spec §7.13)");

            var logs = await db.ActivityLogs
                .Where(a => a.TripId == trip.Trip.Id && a.Action == ActivityAction.PlaceDeleted)
                .ToListAsync();
            logs.Should().ContainSingle("spec §5.6 asks for one entry summarising both effects");
            logs[0].SummaryText.Should().Contain("2 itinerary item");

            var total = await db.ActivityLogs.CountAsync(a => a.TripId == trip.Trip.Id);
            total.Should().Be(logsBefore + 1);
        });

        // The unrelated place is untouched.
        await (await client.GetAsync($"/trips/{trip.Trip.Id}/places/{other.Id}"))
            .ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_an_unscheduled_place_still_clears_its_travel_cache()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "CacheClearer");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "A");
        var other = await client.CreatePlaceAsync(trip.Trip.Id, name: "B", lat: 21.0, lng: 105.0);

        await SeedTravelCacheAsync(trip.Trip.Id, place.Id, other.Id);

        await (await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}"))
            .ShouldBeAsync(HttpStatusCode.OK);

        var remaining = await factory.WithDbAsync(db => db.TravelTimeCaches
            .CountAsync(c => c.FromPlaceId == place.Id || c.ToPlaceId == place.Id));

        remaining.Should().Be(0);
    }

    [Fact]
    public async Task Moving_a_place_invalidates_its_cached_travel_times_in_both_directions()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Mover");
        var moved = await client.CreatePlaceAsync(trip.Trip.Id, name: "Moved", lat: 20.8, lng: 104.6);
        var other = await client.CreatePlaceAsync(trip.Trip.Id, name: "Other", lat: 21.0, lng: 105.0);
        var third = await client.CreatePlaceAsync(trip.Trip.Id, name: "Third", lat: 21.5, lng: 105.5);

        await SeedTravelCacheAsync(trip.Trip.Id, moved.Id, other.Id);
        await SeedTravelCacheAsync(trip.Trip.Id, other.Id, moved.Id);
        await SeedTravelCacheAsync(trip.Trip.Id, other.Id, third.Id);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{moved.Id}",
            """{"lat":20.9,"lng":104.7}""");
        await response.ShouldBeAsync(HttpStatusCode.OK);

        await factory.WithDbAsync(async db =>
        {
            var touching = await db.TravelTimeCaches
                .CountAsync(c => c.FromPlaceId == moved.Id || c.ToPlaceId == moved.Id);
            touching.Should().Be(0, "spec §7.4 invalidates both directions");

            var untouched = await db.TravelTimeCaches
                .CountAsync(c => c.FromPlaceId == other.Id && c.ToPlaceId == third.Id);
            untouched.Should().Be(1, "routes not involving the moved place stay cached");
        });
    }

    [Fact]
    public async Task Editing_a_place_without_moving_it_keeps_the_cache()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Renamer");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Before", lat: 20.8, lng: 104.6);
        var other = await client.CreatePlaceAsync(trip.Trip.Id, name: "Other", lat: 21.0, lng: 105.0);

        await SeedTravelCacheAsync(trip.Trip.Id, place.Id, other.Id);

        await (await client.PatchJsonAsync(
                $"/trips/{trip.Trip.Id}/places/{place.Id}",
                """{"name":"After"}"""))
            .ShouldBeAsync(HttpStatusCode.OK);

        var remaining = await factory.WithDbAsync(db => db.TravelTimeCaches
            .CountAsync(c => c.FromPlaceId == place.Id));

        remaining.Should().Be(1, "a rename does not change the route");
    }

    [Fact]
    public async Task Patch_updates_only_the_fields_that_were_sent()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "PartialPatcher");
        var place = await client.CreatePlaceAsync(
            trip.Trip.Id,
            name: "Original",
            category: "Food",
            timeSlots: ["Noon"],
            estimatedCost: 120_000,
            openHoursText: "08:00-17:00");

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{place.Id}",
            """{"name":"Renamed"}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        updated!.Name.Should().Be("Renamed");
        updated.Category.Should().Be("Food");
        updated.TimeSlots.Should().Equal("Noon");
        updated.EstimatedCost.Should().Be(120_000);
        updated.OpenHoursText.Should().Be("08:00-17:00");
    }

    [Fact]
    public async Task Patch_can_clear_optional_fields_with_an_explicit_null()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Clearer");
        var place = await client.CreatePlaceAsync(
            trip.Trip.Id, estimatedCost: 99_000, openHoursText: "all day");

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{place.Id}",
            """{"estimatedCost":null,"openHoursText":null}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        updated!.EstimatedCost.Should().BeNull();
        updated.OpenHoursText.Should().BeNull();
    }

    [Fact]
    public async Task Patch_rejects_an_empty_time_slot_array()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "SlotClearer");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{place.Id}",
            """{"timeSlots":[]}""");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "timeSlots");
    }

    [Fact]
    public async Task Every_place_mutation_writes_an_activity_log_entry()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Auditor");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);
        await client.PatchJsonAsync($"/trips/{trip.Trip.Id}/places/{place.Id}", """{"name":"Edited"}""");
        await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");

        var actions = await factory.WithDbAsync(db => db.ActivityLogs
            .Where(a => a.TripId == trip.Trip.Id)
            .OrderBy(a => a.At)
            .Select(a => a.Action)
            .ToListAsync());

        actions.Should().Equal(
            ActivityAction.TripCreated,
            ActivityAction.PlaceCreated,
            ActivityAction.PlaceUpdated,
            ActivityAction.PlaceDeleted);
    }

    [Fact]
    public async Task A_place_in_another_trip_is_not_returned_in_this_trip_list()
    {
        var clientA = factory.CreateApiClient();
        var tripA = await clientA.CreateTripAsync(ownerDisplayName: "A");
        await clientA.CreatePlaceAsync(tripA.Trip.Id, name: "A-place");

        var clientB = factory.CreateApiClient();
        var tripB = await clientB.CreateTripAsync(ownerDisplayName: "B");
        await clientB.CreatePlaceAsync(tripB.Trip.Id, name: "B-place");

        var listA = await clientA.GetFromJsonAsync<List<PlaceResponse>>(
            $"/trips/{tripA.Trip.Id}/places", ApiClient.Json);

        listA!.Should().ContainSingle().Which.Name.Should().Be("A-place");
    }

    private Task SeedItineraryAsync(Guid tripId, Guid placeId, Guid memberId, params DateOnly[] dates) =>
        factory.WithDbAsync(async db =>
        {
            foreach (var date in dates)
            {
                db.ItineraryItems.Add(new ItineraryItem
                {
                    Id = Guid.NewGuid(),
                    TripId = tripId,
                    PlaceId = placeId,
                    Date = date,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    UpdatedByMemberId = memberId,
                });
            }

            await db.SaveChangesAsync();
        });

    private Task SeedTravelCacheAsync(Guid tripId, Guid fromPlaceId, Guid toPlaceId) =>
        factory.WithDbAsync(async db =>
        {
            db.TravelTimeCaches.Add(new TravelTimeCache
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                FromPlaceId = fromPlaceId,
                ToPlaceId = toPlaceId,
                Mode = TravelTimeMode.Driving,
                Minutes = 30,
                Meters = 12_000,
                Source = TravelTimeSource.Osrm,
                FetchedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });
}
