using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Api.Tests;

public sealed class TripEndpointTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task Creating_a_trip_returns_the_trip_the_owner_and_a_session_cookie()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/trips", new
        {
            name = "Mộc Châu weekend",
            destination = "Mộc Châu, Vietnam",
            startDate = "2026-03-01",
            endDate = "2026-03-03",
            ownerDisplayName = "Quan",
        }, ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TripSessionResponse>(ApiClient.Json);
        created!.Trip.Name.Should().Be("Mộc Châu weekend");
        created.Trip.TimeZoneId.Should().Be(TripDefaults.TimeZoneId);
        created.Trip.Currency.Should().Be("VND");
        created.Trip.CurrencyExponent.Should().Be(0);
        created.Trip.Status.Should().Be(nameof(TripStatus.Planning));
        created.Trip.InviteCode.Should().HaveLength(8);
        created.Trip.Members.Should().ContainSingle();
        created.Session.Role.Should().Be(nameof(MemberRole.Owner));

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().Contain("wego_session=")
            .And.ContainEquivalentOf("httponly")
            .And.ContainEquivalentOf("samesite=lax");
    }

    [Fact]
    public async Task Creating_a_trip_writes_one_activity_log_entry()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Logger");

        var entries = await factory.WithDbAsync(db => db.ActivityLogs
            .Where(a => a.TripId == trip.Trip.Id)
            .ToListAsync());

        entries.Should().ContainSingle()
            .Which.Action.Should().Be(ActivityAction.TripCreated);
    }

    [Fact]
    public async Task Joining_with_a_valid_code_adds_a_member_and_issues_a_session()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Quan");

        var joinerClient = factory.CreateApiClient();
        var response = await joinerClient.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "Linh",
        }, ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.OK);

        var joined = await response.Content.ReadFromJsonAsync<TripSessionResponse>(ApiClient.Json);
        joined!.Session.Role.Should().Be(nameof(MemberRole.Editor));
        joined.Trip.Members.Should().HaveCount(2);

        // The new session really works against the trip.
        var members = await joinerClient.GetFromJsonAsync<List<MemberResponse>>(
            $"/trips/{trip.Trip.Id}/members", ApiClient.Json);
        members!.Select(m => m.DisplayName).Should().BeEquivalentTo(["Quan", "Linh"]);
    }

    [Fact]
    public async Task Joining_accepts_a_lower_case_invite_code()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Case owner");

        var joinerClient = factory.CreateApiClient();
        var response = await joinerClient.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode.ToLowerInvariant(),
            displayName = "Lower",
        }, ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Joining_with_an_unknown_code_is_indistinguishable_from_a_missing_trip()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = "ZZZZZZZZ",
            displayName = "Nobody",
        }, ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.NotFound);
    }

    [Theory]
    [InlineData("Quan")]
    [InlineData("quan")]
    [InlineData("QUAN")]
    [InlineData("  Quan  ")]
    public async Task Joining_with_a_name_already_on_the_trip_conflicts(string attempted)
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Quan", name: $"Dup {Guid.NewGuid():N}");

        var joinerClient = factory.CreateApiClient();
        var response = await joinerClient.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = attempted,
        }, ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.NameTaken);
    }

    [Fact]
    public async Task An_eleventh_member_cannot_join()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Owner");

        for (var i = 2; i <= TripDefaults.MaxMembers; i++)
        {
            var joiner = factory.CreateApiClient();
            var ok = await joiner.PostAsJsonAsync("/trips/join", new
            {
                inviteCode = trip.Trip.InviteCode,
                displayName = $"Member{i}",
            }, ApiClient.Json);
            await ok.ShouldBeAsync(HttpStatusCode.OK);
        }

        var overflow = factory.CreateApiClient();
        var response = await overflow.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "OneTooMany",
        }, ApiClient.Json);

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.TripFull);
    }

    [Fact]
    public async Task Invite_codes_are_unique_across_trips()
    {
        var codes = new List<string>();
        for (var i = 0; i < 25; i++)
        {
            var client = factory.CreateApiClient();
            var trip = await client.CreateTripAsync(ownerDisplayName: $"Owner{i}", name: $"Trip {i}");
            codes.Add(trip.Trip.InviteCode);
        }

        codes.Distinct().Should().HaveCount(codes.Count);
    }

    [Fact]
    public async Task Updating_trip_dates_that_would_orphan_items_conflicts_and_changes_nothing()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "Planner",
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 10));
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        // Itinerary endpoints arrive in milestone 3; the row is seeded directly
        // so the milestone-1 rule can still be proven.
        await factory.WithDbAsync(async db =>
        {
            db.ItineraryItems.Add(new ItineraryItem
            {
                Id = Guid.NewGuid(),
                TripId = trip.Trip.Id,
                PlaceId = place.Id,
                Date = new DateOnly(2026, 3, 9),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByMemberId = trip.Session.MemberId,
            });
            await db.SaveChangesAsync();
        });

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}",
            """{"endDate":"2026-03-05"}""");

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.ItemsOutOfRange);

        var unchanged = await client.GetFromJsonAsync<TripResponse>(
            $"/trips/{trip.Trip.Id}", ApiClient.Json);
        unchanged!.EndDate.Should().Be(new DateOnly(2026, 3, 10), "the rejected change must not be applied");
    }

    [Fact]
    public async Task Widening_trip_dates_is_allowed_with_items_present()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(
            ownerDisplayName: "Widener",
            startDate: new DateOnly(2026, 3, 1),
            endDate: new DateOnly(2026, 3, 5));
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await factory.WithDbAsync(async db =>
        {
            db.ItineraryItems.Add(new ItineraryItem
            {
                Id = Guid.NewGuid(),
                TripId = trip.Trip.Id,
                PlaceId = place.Id,
                Date = new DateOnly(2026, 3, 4),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByMemberId = trip.Session.MemberId,
            });
            await db.SaveChangesAsync();
        });

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}",
            """{"endDate":"2026-03-20"}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_leaves_unmentioned_fields_alone()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Patcher", destination: "Original place");

        var response = await client.PatchJsonAsync($"/trips/{trip.Trip.Id}", """{"name":"Renamed"}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TripResponse>(ApiClient.Json);
        updated!.Name.Should().Be("Renamed");
        updated.Destination.Should().Be("Original place");
        updated.StartDate.Should().Be(trip.Trip.StartDate);
    }

    [Fact]
    public async Task Patch_can_clear_a_nullable_field_with_an_explicit_null()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Budgeter", budgetAmount: 5_000_000);
        trip.Trip.BudgetAmount.Should().Be(5_000_000);

        var response = await client.PatchJsonAsync($"/trips/{trip.Trip.Id}", """{"budgetAmount":null}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TripResponse>(ApiClient.Json);
        updated!.BudgetAmount.Should().BeNull("an explicit null clears the field, unlike an absent one");
    }

    [Fact]
    public async Task Deleting_a_trip_is_not_supported()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Deleter");

        var response = await client.DeleteAsync($"/trips/{trip.Trip.Id}");

        await response.ShouldBeAsync(HttpStatusCode.MethodNotAllowed);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.MethodNotAllowed);
    }

    [Fact]
    public async Task Creating_a_trip_with_an_invalid_body_reports_every_bad_field()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", """
            {
              "name": "   ",
              "destination": "",
              "startDate": "2026-03-10",
              "endDate": "2026-03-01",
              "ownerDisplayName": ""
            }
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.ValidationFailed);
        problem.Errors!.Select(e => e.Field)
            .Should().Contain(["name", "destination", "endDate", "ownerDisplayName"]);
    }

    [Fact]
    public async Task Session_endpoint_reports_the_current_identity()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Whoami");

        var response = await client.GetAsync("/session");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(trip.Trip.Id.ToString()).And.Contain(trip.Session.MemberId.ToString());
    }

    [Fact]
    public async Task Session_endpoint_is_unauthenticated_without_a_cookie()
    {
        var response = await factory.CreateApiClient().GetAsync("/session");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }
}
