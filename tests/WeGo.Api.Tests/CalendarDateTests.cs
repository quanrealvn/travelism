using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Api.Tests;

/// <summary>
/// Spec §7.10 and reviewer step 6: a calendar date must never shift, whatever
/// the server's time zone is. The structural test below is the stronger of the
/// two — a date cannot drift if it is never a <see cref="DateTime"/> in the
/// first place. The round-trip tests then confirm the wire format.
/// </summary>
public sealed class CalendarDateTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    /// <summary>
    /// Calendar concepts are DateOnly/TimeOnly end to end. Asserted by reflection
    /// so a future entity cannot quietly reintroduce a DateTime that a passing
    /// test suite would not notice until a user in another time zone reported it.
    /// </summary>
    [Fact]
    public void No_entity_models_a_calendar_concept_as_a_DateTime()
    {
        var entityTypes = typeof(Trip).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == typeof(Trip).Namespace)
            .ToList();

        entityTypes.Should().NotBeEmpty();

        var offenders = entityTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Type = t, Property = p }))
            .Where(x => x.Property.PropertyType == typeof(DateTime)
                        || x.Property.PropertyType == typeof(DateTime?))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        offenders.Should().BeEmpty(
            "instants are DateTimeOffset and calendar dates are DateOnly; a bare DateTime is neither");
    }

    [Fact]
    public void Date_and_time_properties_use_the_calendar_types()
    {
        typeof(Trip).GetProperty(nameof(Trip.StartDate))!.PropertyType.Should().Be<DateOnly>();
        typeof(Trip).GetProperty(nameof(Trip.EndDate))!.PropertyType.Should().Be<DateOnly>();
        typeof(ItineraryItem).GetProperty(nameof(ItineraryItem.Date))!.PropertyType.Should().Be<DateOnly>();
        typeof(ItineraryItem).GetProperty(nameof(ItineraryItem.StartTime))!.PropertyType
            .Should().Be<TimeOnly?>();
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-02")]
    [InlineData("2026-03-01", "2026-03-03")]
    [InlineData("2026-12-31", "2027-01-01")]
    // A date whose UTC instant falls on the previous day in most western zones,
    // and on the next day in Pacific/Auckland.
    [InlineData("2026-06-15", "2026-06-16")]
    public async Task Trip_dates_round_trip_unchanged(string start, string end)
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", $$"""
            {"name":"Date trip","destination":"Anywhere",
             "startDate":"{{start}}","endDate":"{{end}}","ownerDisplayName":"D{{start}}"}
            """);

        var created = await response.Content.ReadFromJsonAsync<TripSessionResponse>(ApiClient.Json);
        created!.Trip.StartDate.Should().Be(DateOnly.Parse(start));
        created.Trip.EndDate.Should().Be(DateOnly.Parse(end));

        // Re-read through a fresh request so the value comes back off SQLite.
        var reread = await client.GetFromJsonAsync<TripResponse>(
            $"/trips/{created.Trip.Id}", ApiClient.Json);
        reread!.StartDate.Should().Be(DateOnly.Parse(start));
        reread.EndDate.Should().Be(DateOnly.Parse(end));
    }

    [Fact]
    public async Task Dates_are_serialised_as_plain_calendar_strings_with_no_time_or_offset()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "Serialiser",
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 3));

        var raw = await client.GetStringAsync($"/trips/{trip.Trip.Id}");
        using var document = JsonDocument.Parse(raw);

        var startDate = document.RootElement.GetProperty("startDate").GetString();
        startDate.Should().Be("2026-03-01");
        startDate.Should().NotContain("T").And.NotContain("Z").And.NotContain("+");
    }

    [Fact]
    public async Task A_date_stored_through_the_api_is_the_same_date_in_the_database()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "DbChecker",
            startDate: new DateOnly(2026, 6, 15),
            endDate: new DateOnly(2026, 6, 16));

        var stored = await factory.WithDbAsync(db => db.Trips
            .Where(t => t.Id == trip.Trip.Id)
            .Select(t => new { t.StartDate, t.EndDate })
            .SingleAsync());

        stored.StartDate.Should().Be(new DateOnly(2026, 6, 15));
        stored.EndDate.Should().Be(new DateOnly(2026, 6, 16));
    }

    [Fact]
    public async Task Itinerary_dates_survive_the_database_round_trip()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "ItineraryDates",
            startDate: new DateOnly(2026, 6, 1),
            endDate: new DateOnly(2026, 6, 30));
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var date = new DateOnly(2026, 6, 15);
        var startTime = new TimeOnly(23, 30);

        await factory.WithDbAsync(async db =>
        {
            db.ItineraryItems.Add(new ItineraryItem
            {
                Id = Guid.NewGuid(),
                TripId = trip.Trip.Id,
                PlaceId = place.Id,
                Date = date,
                StartTime = startTime,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByMemberId = trip.Session.MemberId,
            });
            await db.SaveChangesAsync();
        });

        var stored = await factory.WithDbAsync(db => db.ItineraryItems
            .Where(i => i.TripId == trip.Trip.Id)
            .Select(i => new { i.Date, i.StartTime })
            .SingleAsync());

        stored.Date.Should().Be(date, "a late-evening time must not roll the date forward");
        stored.StartTime.Should().Be(startTime);
    }

    [Fact]
    public async Task Timestamps_are_stored_as_utc_instants()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Stamper");

        var stored = await factory.WithDbAsync(db => db.Trips
            .Where(t => t.Id == trip.Trip.Id)
            .Select(t => t.CreatedAt)
            .SingleAsync());

        stored.Offset.Should().Be(TimeSpan.Zero, "all timestamps are UTC (spec §3)");
        stored.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_trip_may_use_any_valid_iana_time_zone()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", """
            {"name":"Kiwi trip","destination":"Auckland",
             "startDate":"2026-03-01","endDate":"2026-03-03",
             "timeZoneId":"Pacific/Auckland","ownerDisplayName":"Kiwi"}
            """);

        await response.ShouldBeAsync(System.Net.HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TripSessionResponse>(ApiClient.Json);
        created!.Trip.TimeZoneId.Should().Be("Pacific/Auckland");
        created.Trip.StartDate.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public async Task An_unknown_time_zone_is_rejected()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", """
            {"name":"Bad zone","destination":"Nowhere",
             "startDate":"2026-03-01","endDate":"2026-03-03",
             "timeZoneId":"Mars/Olympus_Mons","ownerDisplayName":"Martian"}
            """);

        await response.ShouldBeAsync(System.Net.HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "timeZoneId");
    }

    [Fact]
    public void Production_code_does_not_read_the_local_clock()
    {
        // DateTime.Now / DateTime.Today are server-timezone dependent. The whole
        // system stamps time through IClock instead, which is UTC by construction.
        var sources = Directory
            .EnumerateFiles(FindRepositoryRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal))
            .ToList();

        sources.Should().NotBeEmpty("the scan must actually find the production sources");

        var offenders = sources
            .Where(path => File.ReadLines(path)
                .Select(line => line.TrimStart())
                // Comment lines are skipped: IClock's own documentation names the
                // very APIs it exists to replace, and that is not a usage.
                .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                               && !line.StartsWith("*", StringComparison.Ordinal))
                .Any(line => line.Contains("DateTime.Now", StringComparison.Ordinal)
                             || line.Contains("DateTime.Today", StringComparison.Ordinal)
                             || line.Contains("DateTimeOffset.Now", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty("time must come from IClock, never from the local clock");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WeGo.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");
        return directory!.FullName;
    }
}
