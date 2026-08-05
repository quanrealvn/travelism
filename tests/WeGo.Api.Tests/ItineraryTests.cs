using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

public sealed class ItineraryTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End = new(2026, 3, 5);

    private async Task<(HttpClient Client, TripSessionResponse Trip)> NewTripAsync(string owner)
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: owner, name: $"Itinerary {owner}", startDate: Start, endDate: End);
        return (client, trip);
    }

    private static Task<HttpResponseMessage> ScheduleAsync(
        HttpClient client,
        Guid tripId,
        Guid placeId,
        DateOnly date,
        TimeOnly? startTime = null,
        string? note = null) =>
        client.PostAsJsonAsync(
            $"/trips/{tripId}/itinerary",
            new { placeId, date, startTime, note },
            ApiClient.Json);

    /// <summary>Confirms a place so it becomes eligible for suggestions.</summary>
    private static async Task ConfirmAsync(HttpClient client, Guid tripId, Guid placeId) =>
        await client.PostAsync($"/trips/{tripId}/places/{placeId}/like", null);

    [Fact]
    public async Task A_place_can_be_scheduled_on_a_day()
    {
        var (client, trip) = await NewTripAsync("Scheduler");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Thác Dải Yếm");

        var response = await ScheduleAsync(client, trip.Trip.Id, place.Id, Start, new TimeOnly(9, 30));

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var item = await response.Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);

        item!.PlaceName.Should().Be("Thác Dải Yếm");
        item.Date.Should().Be(Start);
        item.StartTime.Should().Be(new TimeOnly(9, 30));
        item.EstimatedDurationMinutes.Should().Be(90);
    }

    [Fact]
    public async Task An_item_without_a_start_time_is_allowed()
    {
        // "Sometime that day" is a real state while planning.
        var (client, trip) = await NewTripAsync("Untimed");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await ScheduleAsync(client, trip.Trip.Id, place.Id, Start);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json))!
            .StartTime.Should().BeNull();
    }

    [Theory]
    [InlineData("2026-02-28")]
    [InlineData("2026-03-06")]
    public async Task A_date_outside_the_trip_is_refused(string date)
    {
        var (client, trip) = await NewTripAsync($"OutOfRange{date.Length}{date[^1]}");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/itinerary", $$"""
            {"placeId":"{{place.Id}}","date":"{{date}}"}
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.DateOutOfRange);
    }

    [Theory]
    [InlineData("2026-03-01")]
    [InlineData("2026-03-05")]
    public async Task The_first_and_last_day_of_the_trip_are_inside_the_range(string date)
    {
        var (client, trip) = await NewTripAsync($"Boundary{date[^1]}");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/itinerary", $$"""
            {"placeId":"{{place.Id}}","date":"{{date}}"}
            """);

        await response.ShouldBeAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_same_place_cannot_be_scheduled_twice_on_one_day()
    {
        var (client, trip) = await NewTripAsync("Duplicate");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Repeated");

        await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start)).ShouldBeAsync(HttpStatusCode.Created);
        var second = await ScheduleAsync(client, trip.Trip.Id, place.Id, Start);

        await second.ShouldBeAsync(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Code.Should().Be(ErrorCodes.DuplicatePlaceOnDate);
    }

    [Fact]
    public async Task The_same_place_may_appear_on_different_days()
    {
        // Spec §6 allows recurrence across the trip, just not within a day.
        var (client, trip) = await NewTripAsync("Recurring");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start)).ShouldBeAsync(HttpStatusCode.Created);
        await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start.AddDays(1)))
            .ShouldBeAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_place_and_day_leave_one_row()
    {
        var (client, trip) = await NewTripAsync("RaceSchedule");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            ScheduleAsync(client, trip.Trip.Id, place.Id, Start)));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);

        var rows = await factory.WithDbAsync(db => db.ItineraryItems
            .CountAsync(i => i.PlaceId == place.Id && i.Date == Start));
        rows.Should().Be(1, "the unique index is what actually enforces §6");
    }

    [Fact]
    public async Task Moving_an_item_to_another_day_works()
    {
        var (client, trip) = await NewTripAsync("Mover2");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);
        var created = await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start))
            .Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/itinerary/{created!.Id}",
            """{"date":"2026-03-03","startTime":"14:00:00"}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var moved = await response.Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);
        moved!.Date.Should().Be(new DateOnly(2026, 3, 3));
        moved.StartTime.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public async Task Moving_onto_a_day_that_already_has_the_place_is_refused_and_changes_nothing()
    {
        // Spec §7.15: this is the drop the client must roll back from.
        var (client, trip) = await NewTripAsync("BadDrop");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var first = await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start))
            .Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);
        await ScheduleAsync(client, trip.Trip.Id, place.Id, Start.AddDays(1));

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/itinerary/{first!.Id}",
            """{"date":"2026-03-02"}""");

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.DuplicatePlaceOnDate);

        var reread = await client.GetFromJsonAsync<List<ItineraryItemResponse>>(
            $"/trips/{trip.Trip.Id}/itinerary?date=2026-03-01", ApiClient.Json);
        reread!.Should().ContainSingle().Which.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task A_start_time_can_be_cleared_with_an_explicit_null()
    {
        var (client, trip) = await NewTripAsync("Clearer2");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);
        var created = await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start, new TimeOnly(9, 0)))
            .Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/itinerary/{created!.Id}",
            """{"startTime":null}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json))!
            .StartTime.Should().BeNull("an explicit null means 'sometime that day'");
    }

    [Fact]
    public async Task The_date_cannot_be_cleared()
    {
        var (client, trip) = await NewTripAsync("NoDateClear");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);
        var created = await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start))
            .Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/itinerary/{created!.Id}",
            """{"date":null}""");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Deleting_an_item_removes_it_for_real_and_logs_it()
    {
        var (client, trip) = await NewTripAsync("ItemDeleter");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);
        var created = await (await ScheduleAsync(client, trip.Trip.Id, place.Id, Start))
            .Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);

        await (await client.DeleteAsync($"/trips/{trip.Trip.Id}/itinerary/{created!.Id}"))
            .ShouldBeAsync(HttpStatusCode.OK);

        var rows = await factory.WithDbAsync(db => db.ItineraryItems
            .IgnoreQueryFilters().CountAsync(i => i.Id == created.Id));
        rows.Should().Be(0, "spec §5.6 hard-deletes itinerary items");

        var logged = await factory.WithDbAsync(db => db.ActivityLogs
            .AnyAsync(a => a.EntityId == created.Id && a.Action == ActivityAction.ItineraryItemDeleted));
        logged.Should().BeTrue();
    }

    [Fact]
    public async Task A_note_over_the_limit_is_refused()
    {
        var (client, trip) = await NewTripAsync("LongNote");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await ScheduleAsync(
            client, trip.Trip.Id, place.Id, Start, note: new string('x', 501));

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "note");
    }

    [Fact]
    public async Task A_place_from_another_trip_cannot_be_scheduled()
    {
        var (client, trip) = await NewTripAsync("SchedAttacker");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "SchedVictim", name: "Victim");
        var victimPlace = await victimClient.CreatePlaceAsync(victim.Trip.Id);

        var response = await ScheduleAsync(client, trip.Trip.Id, victimPlace.Id, Start);

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Listing_by_date_returns_only_that_day_ordered_by_time()
    {
        var (client, trip) = await NewTripAsync("Lister2");
        var morning = await client.CreatePlaceAsync(trip.Trip.Id, name: "Morning", lat: 20.1, lng: 104.1);
        var evening = await client.CreatePlaceAsync(trip.Trip.Id, name: "Evening", lat: 20.2, lng: 104.2);
        var untimed = await client.CreatePlaceAsync(trip.Trip.Id, name: "Untimed", lat: 20.3, lng: 104.3);
        var otherDay = await client.CreatePlaceAsync(trip.Trip.Id, name: "OtherDay", lat: 20.4, lng: 104.4);

        await ScheduleAsync(client, trip.Trip.Id, evening.Id, Start, new TimeOnly(19, 0));
        await ScheduleAsync(client, trip.Trip.Id, untimed.Id, Start);
        await ScheduleAsync(client, trip.Trip.Id, morning.Id, Start, new TimeOnly(8, 0));
        await ScheduleAsync(client, trip.Trip.Id, otherDay.Id, Start.AddDays(1), new TimeOnly(8, 0));

        var day = await client.GetFromJsonAsync<List<ItineraryItemResponse>>(
            $"/trips/{trip.Trip.Id}/itinerary?date=2026-03-01", ApiClient.Json);

        // Timed items in clock order, then the ones with no time yet.
        day!.Select(i => i.PlaceName).Should().Equal("Morning", "Evening", "Untimed");
    }

    [Fact]
    public async Task Suggestions_offer_only_confirmed_places_not_already_on_the_day()
    {
        var (client, trip) = await NewTripAsync("Suggester");

        var confirmed = await client.CreatePlaceAsync(
            trip.Trip.Id, name: "Confirmed", timeSlots: ["Morning"], lat: 20.1, lng: 104.1);
        var alsoConfirmed = await client.CreatePlaceAsync(
            trip.Trip.Id, name: "AlsoConfirmed", timeSlots: ["Morning"], lat: 20.2, lng: 104.2);
        var idea = await client.CreatePlaceAsync(
            trip.Trip.Id, name: "JustAnIdea", timeSlots: ["Morning"], lat: 20.3, lng: 104.3);

        await ConfirmAsync(client, trip.Trip.Id, confirmed.Id);
        await ConfirmAsync(client, trip.Trip.Id, alsoConfirmed.Id);
        await ScheduleAsync(client, trip.Trip.Id, alsoConfirmed.Id, Start);

        var groups = await client.GetFromJsonAsync<List<SuggestionGroupResponse>>(
            $"/trips/{trip.Trip.Id}/suggestions?date=2026-03-01", ApiClient.Json);

        var morning = groups!.Single(g => g.Slot == "Morning").Places.Select(p => p.Name).ToList();
        morning.Should().Equal("Confirmed");
        morning.Should().NotContain("JustAnIdea", "only Confirmed places are suggested");
        morning.Should().NotContain("AlsoConfirmed", "it is already on this day");

        _ = idea;
    }

    [Fact]
    public async Task Suggestions_exclude_soft_deleted_places()
    {
        var (client, trip) = await NewTripAsync("SoftDeleteSuggest");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Doomed", timeSlots: ["Morning"]);
        await ConfirmAsync(client, trip.Trip.Id, place.Id);
        await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}");

        var groups = await client.GetFromJsonAsync<List<SuggestionGroupResponse>>(
            $"/trips/{trip.Trip.Id}/suggestions?date=2026-03-01", ApiClient.Json);

        groups!.SelectMany(g => g.Places).Should().BeEmpty();
    }

    [Fact]
    public async Task Suggestions_for_a_date_outside_the_trip_are_refused()
    {
        var (client, trip) = await NewTripAsync("SuggestOutOfRange");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/suggestions?date=2026-04-01");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.DateOutOfRange);
    }

    [Fact]
    public async Task Suggestions_without_a_date_are_refused()
    {
        var (client, trip) = await NewTripAsync("SuggestNoDate");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/suggestions");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "date");
    }

    [Fact]
    public async Task Suggestions_prefer_a_category_unlike_what_is_already_planned()
    {
        var (client, trip) = await NewTripAsync("Variety");

        var food = await client.CreatePlaceAsync(
            trip.Trip.Id, name: "Cheap food", category: "Food",
            timeSlots: ["Morning"], estimatedCost: 10_000, lat: 20.1, lng: 104.1);
        var sight = await client.CreatePlaceAsync(
            trip.Trip.Id, name: "Pricey sight", category: "Sight",
            timeSlots: ["Morning"], estimatedCost: 900_000, lat: 20.2, lng: 104.2);
        var plannedFood = await client.CreatePlaceAsync(
            trip.Trip.Id, name: "Planned food", category: "Food",
            timeSlots: ["Morning"], lat: 20.3, lng: 104.3);

        foreach (var id in new[] { food.Id, sight.Id, plannedFood.Id })
        {
            await ConfirmAsync(client, trip.Trip.Id, id);
        }

        await ScheduleAsync(client, trip.Trip.Id, plannedFood.Id, Start, new TimeOnly(8, 0));

        var groups = await client.GetFromJsonAsync<List<SuggestionGroupResponse>>(
            $"/trips/{trip.Trip.Id}/suggestions?date=2026-03-01", ApiClient.Json);

        groups!.Single(g => g.Slot == "Morning").Places.Select(p => p.Name)
            .Should().Equal("Pricey sight", "Cheap food");
    }

    [Fact]
    public async Task Itinerary_dates_survive_the_round_trip_exactly()
    {
        var (client, trip) = await NewTripAsync("DateRoundTrip");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var created = await (await ScheduleAsync(
                client, trip.Trip.Id, place.Id, new DateOnly(2026, 3, 4), new TimeOnly(23, 30)))
            .Content.ReadFromJsonAsync<ItineraryItemResponse>(ApiClient.Json);

        var raw = await client.GetStringAsync($"/trips/{trip.Trip.Id}/itinerary?date=2026-03-04");
        raw.Should().Contain("\"date\":\"2026-03-04\"").And.Contain("\"startTime\":\"23:30:00\"");

        created!.Date.Should().Be(new DateOnly(2026, 3, 4));
    }
}
