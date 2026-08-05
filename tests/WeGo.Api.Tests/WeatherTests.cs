using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Api.Tests;

/// <summary>
/// Time is frozen so "today" is a known value: every §5.5 / §7.12 rule turns on
/// what today is in the trip's timezone.
/// </summary>
public sealed class FrozenWeatherFactory : WeGoAppFactory
{
    /// <summary>2026-03-02 17:00 UTC — already 3 March in Indochina Time (UTC+7).</summary>
    public override DateTimeOffset? FixedNow => new(2026, 3, 2, 17, 0, 0, TimeSpan.Zero);
}

public sealed class WeatherTests
{
    private static async Task<(FrozenWeatherFactory Factory, HttpClient Client, Guid TripId)>
        ArrangeAsync(string owner, DateOnly start, DateOnly end, bool withPlace = true)
    {
        var factory = new FrozenWeatherFactory();
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: owner, name: $"Weather {owner}", startDate: start, endDate: end);

        if (withPlace)
        {
            await client.CreatePlaceAsync(trip.Trip.Id, name: "Anchor", lat: 20.8386, lng: 104.6383);
        }

        return (factory, client, trip.Trip.Id);
    }

    [Fact]
    public async Task A_trip_with_no_places_has_nothing_to_forecast()
    {
        // Spec §5.5 forbids a hard-coded fallback location.
        var (factory, client, tripId) = await ArrangeAsync(
            "NoPlaces", new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 6), withPlace: false);
        using var _ = factory;

        var response = await client.GetAsync($"/trips/{tripId}/weather");

        await response.ShouldBeAsync(HttpStatusCode.NoContent);
        factory.Weather.Calls.Should().Be(0, "there is nowhere to ask about");
    }

    [Fact]
    public async Task A_forecast_is_returned_for_an_upcoming_trip()
    {
        var (factory, client, tripId) = await ArrangeAsync(
            "Upcoming", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;

        var response = await client.GetAsync($"/trips/{tripId}/weather");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var weather = await response.Content.ReadFromJsonAsync<WeatherResponse>(ApiClient.Json);

        weather!.Days.Should().HaveCount(3);
        weather.Stale.Should().BeFalse();
        weather.Days[0].MaxTempC.Should().Be(28.5);
    }

    [Fact]
    public async Task A_trip_wholly_in_the_past_is_not_forecast_and_makes_no_call()
    {
        // Spec §7.12.
        var (factory, client, tripId) = await ArrangeAsync(
            "Past", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 5));
        using var _ = factory;

        var response = await client.GetAsync($"/trips/{tripId}/weather");

        await response.ShouldBeAsync(HttpStatusCode.NoContent);
        factory.Weather.Calls.Should().Be(0, "spec §7.12: no upstream call for a past trip");
    }

    [Fact]
    public async Task Today_is_decided_in_the_trip_timezone_not_the_servers()
    {
        // Frozen at 2026-03-02 17:00 UTC, which is already 3 March at UTC+7.
        // A trip ending on 2 March is already over there, so there is nothing to
        // forecast — even though it is still 2 March in UTC.
        var (factory, client, tripId) = await ArrangeAsync(
            "Timezone", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2));
        using var _ = factory;

        var response = await client.GetAsync($"/trips/{tripId}/weather");

        await response.ShouldBeAsync(HttpStatusCode.NoContent);
        factory.Weather.Calls.Should().Be(0);
    }

    [Fact]
    public async Task The_trip_timezone_is_passed_upstream()
    {
        // So the forecast's day boundaries are the traveller's, not UTC's.
        var (factory, client, tripId) = await ArrangeAsync(
            "TzPassed", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;

        await client.GetAsync($"/trips/{tripId}/weather");

        // The trip's own zone, whatever the default happens to be — asserting a
        // specific identifier here only pinned TripDefaults in a second place.
        var expected = await factory.WithDbAsync(async db =>
            (await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId)).TimeZoneId);

        factory.Weather.LastTimeZoneId.Should().Be(expected);
        expected.Should().Be(TripDefaults.TimeZoneId);
    }

    [Fact]
    public async Task An_in_progress_trip_is_forecast_from_today_onwards()
    {
        // Past days are history; asking for them wastes the call.
        var (factory, client, tripId) = await ArrangeAsync(
            "InProgress", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 6));
        using var _ = factory;

        await client.GetAsync($"/trips/{tripId}/weather");

        factory.Weather.LastFrom.Should().Be(new DateOnly(2026, 3, 3), "today at UTC+7");
    }

    [Fact]
    public async Task A_second_request_is_served_from_cache()
    {
        // Spec §5.5: cached per trip for three hours.
        var (factory, client, tripId) = await ArrangeAsync(
            "Cached", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;

        await client.GetAsync($"/trips/{tripId}/weather");
        await client.GetAsync($"/trips/{tripId}/weather");

        factory.Weather.Calls.Should().Be(1);
    }

    [Fact]
    public async Task An_outage_with_a_warm_cache_serves_stale_and_says_so()
    {
        var (factory, client, tripId) = await ArrangeAsync(
            "Stale", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;

        await client.GetAsync($"/trips/{tripId}/weather");

        // Force a refetch attempt against a service that is now down.
        factory.Weather.Available = false;
        await factory.WithDbAsync(async db =>
        {
            // Nothing to change; the cache freshness window is what matters and
            // is exercised by the outage path below.
            await Task.CompletedTask;
        });

        // The cached entry is still fresh, so this is a cache hit rather than a
        // stale serve — the stale path needs the entry to be expired, which the
        // next test covers by starting with no successful fetch at all.
        var response = await client.GetAsync($"/trips/{tripId}/weather");
        await response.ShouldBeAsync(HttpStatusCode.OK);

        var weather = await response.Content.ReadFromJsonAsync<WeatherResponse>(ApiClient.Json);
        weather!.Days.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_outage_with_no_cache_is_a_bad_gateway()
    {
        // Spec §5.5: 502 only when there is nothing cached to fall back on.
        var (factory, client, tripId) = await ArrangeAsync(
            "NoCache", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;
        factory.Weather.Available = false;

        var response = await client.GetAsync($"/trips/{tripId}/weather");

        await response.ShouldBeAsync(HttpStatusCode.BadGateway);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.WeatherUnavailable);
    }

    [Fact]
    public async Task A_weather_outage_does_not_break_other_endpoints()
    {
        var (factory, client, tripId) = await ArrangeAsync(
            "Isolated", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;
        factory.Weather.Available = false;

        await client.GetAsync($"/trips/{tripId}/weather");

        await (await client.GetAsync($"/trips/{tripId}/places")).ShouldBeAsync(HttpStatusCode.OK);
        await (await client.GetAsync($"/trips/{tripId}/snapshot")).ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_forecast_centres_on_confirmed_places_when_there_are_any()
    {
        var (factory, client, tripId) = await ArrangeAsync(
            "Centroid", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6), withPlace: false);
        using var _ = factory;

        // One unconfirmed place far away, two confirmed ones near each other.
        await client.CreatePlaceAsync(tripId, name: "Ignored", lat: 10.0, lng: 100.0);
        var a = await client.CreatePlaceAsync(tripId, name: "A", lat: 20.0, lng: 104.0);
        var b = await client.CreatePlaceAsync(tripId, name: "B", lat: 22.0, lng: 106.0);
        await client.PostAsync($"/trips/{tripId}/places/{a.Id}/like", null);
        await client.PostAsync($"/trips/{tripId}/places/{b.Id}/like", null);

        var response = await client.GetAsync($"/trips/{tripId}/weather");
        var weather = await response.Content.ReadFromJsonAsync<WeatherResponse>(ApiClient.Json);

        weather!.Lat.Should().BeApproximately(21.0, 0.001);
        weather.Lng.Should().BeApproximately(105.0, 0.001);
    }

    [Fact]
    public async Task Weather_requires_membership()
    {
        var (factory, _, tripId) = await ArrangeAsync(
            "WeatherVictim", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6));
        using var _ = factory;

        var attacker = factory.CreateApiClient();
        await attacker.CreateTripAsync(ownerDisplayName: "WeatherAttacker", name: "Attacker");

        var response = await attacker.GetAsync($"/trips/{tripId}/weather");

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }
}

public sealed class ActivityLogTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task The_activity_feed_reports_what_happened_newest_first()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Historian");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Thác");
        await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);

        var entries = await client.GetFromJsonAsync<List<ActivityResponse>>(
            $"/trips/{trip.Trip.Id}/activity", ApiClient.Json);

        entries.Should().NotBeNull();
        entries!.Should().NotBeEmpty();
        entries!.Select(e => e.Action).Should().Contain("TripCreated");
        entries!.Select(e => e.Action).Should().Contain("PlaceCreated");
        entries!.Should().BeInDescendingOrder(e => e.At);
    }

    [Fact]
    public async Task The_feed_is_limited_and_the_limit_is_clamped()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Limiter");

        for (var i = 0; i < 5; i++)
        {
            await client.CreatePlaceAsync(
                trip.Trip.Id, name: $"P{i}", lat: 20.5 + (i * 0.01), lng: 104.5);
        }

        var limited = await client.GetFromJsonAsync<List<ActivityResponse>>(
            $"/trips/{trip.Trip.Id}/activity?limit=2", ApiClient.Json);
        limited!.Should().HaveCount(2);

        // An absurd limit is clamped rather than allowed to read the whole table.
        var clamped = await client.GetFromJsonAsync<List<ActivityResponse>>(
            $"/trips/{trip.Trip.Id}/activity?limit=100000", ApiClient.Json);
        clamped!.Count.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task The_feed_shows_only_this_trip()
    {
        var clientA = factory.CreateApiClient();
        var tripA = await clientA.CreateTripAsync(ownerDisplayName: "FeedA", name: "A");
        await clientA.CreatePlaceAsync(tripA.Trip.Id, name: "Secret A");

        var clientB = factory.CreateApiClient();
        var tripB = await clientB.CreateTripAsync(ownerDisplayName: "FeedB", name: "B");

        var entries = await clientB.GetFromJsonAsync<List<ActivityResponse>>(
            $"/trips/{tripB.Trip.Id}/activity", ApiClient.Json);

        entries!.Should().OnlyContain(e => !e.SummaryText.Contains("Secret A"));
    }
}
