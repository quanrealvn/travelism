using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;

namespace WeGo.Api.Tests;

/// <summary>
/// Spec §7.9 and reviewer step 7: concurrent writers must not surface
/// SQLITE_BUSY as a 5xx, duplicate rows, or lose writes.
/// <para>
/// One <see cref="HttpClient"/> issues all the parallel requests — it is
/// thread-safe, shares the one session cookie, and matches what a real client
/// does with several requests in flight.
/// </para>
/// </summary>
public sealed class ConcurrencyTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task Twenty_parallel_place_creations_all_succeed_without_a_5xx()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Hammerer");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", $$"""
                {"name":"Place {{i}}","lat":{{20 + (i * 0.01)}},"lng":105,
                 "category":"Other","timeSlots":["Morning"],"estimatedDurationMinutes":30}
                """)));

        await AssertNoServerErrorsAsync(responses);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        var stored = await factory.WithDbAsync(db => db.Places
            .Where(p => p.TripId == trip.Trip.Id)
            .CountAsync());

        stored.Should().Be(20, "every committed write is present exactly once");
    }

    [Fact]
    public async Task Twenty_parallel_edits_of_one_place_settle_on_a_last_write_wins_value()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Racer");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Contended");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            client.PatchJsonAsync(
                $"/trips/{trip.Trip.Id}/places/{place.Id}",
                $$"""{"name":"Edit {{i}}"}""")));

        await AssertNoServerErrorsAsync(responses);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var rows = await factory.WithDbAsync(db => db.Places
            .Where(p => p.Id == place.Id)
            .ToListAsync());

        rows.Should().ContainSingle("last-write-wins updates in place, it does not insert");
        rows[0].Name.Should().StartWith("Edit ", "one of the concurrent writes won outright");
    }

    [Fact]
    public async Task A_mixed_burst_of_writes_stays_consistent()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Mixer");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: "Seed");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            (i % 2) == 0
                ? client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", $$"""
                    {"name":"Burst {{i}}","lat":21,"lng":{{105 + (i * 0.01)}},
                     "category":"Food","timeSlots":["Noon"],"estimatedDurationMinutes":45}
                    """)
                : client.PatchJsonAsync(
                    $"/trips/{trip.Trip.Id}/places/{place.Id}",
                    $$"""{"estimatedDurationMinutes":{{30 + i}}}""")));

        await AssertNoServerErrorsAsync(responses);

        var listed = await client.GetFromJsonAsync<List<PlaceResponse>>(
            $"/trips/{trip.Trip.Id}/places", ApiClient.Json);

        listed!.Should().HaveCount(11, "the seed place plus the ten that were created");
        listed!.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Concurrent_joins_with_the_same_name_produce_exactly_one_member()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "JoinOwner");

        // Separate clients here: each join is a different would-be member, and
        // they must not share a cookie jar.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            factory.CreateApiClient().PostJsonAsync("/trips/join", $$"""
                {"inviteCode":"{{trip.Trip.InviteCode}}","displayName":"Contested"}
                """)));

        await AssertNoServerErrorsAsync(responses);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK)
            .Should().Be(1, "the unique index settles the race the pre-check cannot");

        var named = await factory.WithDbAsync(db => db.Members
            .CountAsync(m => m.TripId == trip.Trip.Id && m.DisplayName == "Contested"));

        named.Should().Be(1);
    }

    [Fact]
    public async Task Parallel_trip_creations_never_collide_on_an_invite_code()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            factory.CreateApiClient().PostJsonAsync("/trips", $$"""
                {"name":"Parallel {{i}}","destination":"Somewhere",
                 "startDate":"2026-03-01","endDate":"2026-03-03",
                 "ownerDisplayName":"Owner {{i}}"}
                """)));

        await AssertNoServerErrorsAsync(responses);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
    }

    /// <summary>Fails with the response body attached, so a 5xx is diagnosable rather than just red.</summary>
    private static async Task AssertNoServerErrorsAsync(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            if ((int)response.StatusCode < 500)
            {
                continue;
            }

            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"A concurrent request failed with {(int)response.StatusCode}. Body: {body}");
        }
    }
}
