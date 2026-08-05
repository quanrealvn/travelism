using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>
/// A browser plans more than one trip. The session therefore holds a set of
/// memberships rather than a single one, and every guarantee that held for one
/// has to keep holding for a set.
/// </summary>
public sealed class MultiTripSessionTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task Creating_a_second_trip_does_not_sign_the_browser_out_of_the_first()
    {
        // The regression that motivated all of this: the cookie held one trip,
        // so making a new one silently cost you last month's plan.
        var client = factory.CreateApiClient();
        var first = await client.CreateTripAsync(ownerDisplayName: "Quan", name: "Mộc Châu");
        var second = await client.CreateTripAsync(ownerDisplayName: "Quan", name: "Đà Lạt");

        (await client.GetAsync($"/trips/{first.Trip.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "the first trip must still be reachable");
        (await client.GetAsync($"/trips/{second.Trip.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Joining_a_trip_keeps_the_trips_already_held()
    {
        var owner = factory.CreateApiClient();
        var hosted = await owner.CreateTripAsync(ownerDisplayName: "Owner", name: "Sa Pa");

        var traveller = factory.CreateApiClient();
        var own = await traveller.CreateTripAsync(ownerDisplayName: "Linh", name: "Của tôi");
        await traveller.JoinTripAsync(hosted.Trip.InviteCode, "Linh");

        (await traveller.GetAsync($"/trips/{own.Trip.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await traveller.GetAsync($"/trips/{hosted.Trip.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task My_trips_lists_every_trip_the_browser_holds()
    {
        var client = factory.CreateApiClient();
        await client.CreateTripAsync(ownerDisplayName: "Quan", name: "Mộc Châu");
        await client.CreateTripAsync(ownerDisplayName: "Quan", name: "Đà Lạt");

        var trips = await client.GetMyTripsAsync();

        trips.Select(t => t.Name).Should().BeEquivalentTo(["Mộc Châu", "Đà Lạt"]);
    }

    [Fact]
    public async Task My_trips_reports_the_departure_date_so_past_and_future_can_be_told_apart()
    {
        var client = factory.CreateApiClient();
        await client.CreateTripAsync(
            name: "Đã đi",
            startDate: new DateOnly(2020, 5, 1),
            endDate: new DateOnly(2020, 5, 3));
        await client.CreateTripAsync(
            name: "Sắp đi",
            startDate: new DateOnly(2030, 5, 1),
            endDate: new DateOnly(2030, 5, 3));

        var trips = await client.GetMyTripsAsync();

        // Newest departure first, so the switcher opens on what is next.
        trips.Select(t => t.Name).Should().ContainInOrder("Sắp đi", "Đã đi");
        trips.Single(t => t.Name == "Đã đi").EndDate.Should().Be(new DateOnly(2020, 5, 3));
    }

    [Fact]
    public async Task My_trips_counts_members_and_places()
    {
        var owner = factory.CreateApiClient();
        var trip = await owner.CreateTripAsync(ownerDisplayName: "Quan");
        await owner.CreatePlaceAsync(trip.Trip.Id, name: "Thác Dải Yếm");
        await owner.CreatePlaceAsync(trip.Trip.Id, name: "Đồi chè");

        var joiner = factory.CreateApiClient();
        await joiner.JoinTripAsync(trip.Trip.InviteCode, "Linh");

        var summary = (await owner.GetMyTripsAsync()).Single();

        summary.MemberCount.Should().Be(2);
        summary.PlaceCount.Should().Be(2);
    }

    [Fact]
    public async Task My_trips_never_carries_the_invite_code()
    {
        // A browser may hold twenty of these. The code to let someone into a
        // trip has no business being sent for a trip nobody has even opened.
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync();

        var response = await client.GetAsync("/trips/mine");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(trip.Trip.InviteCode);
    }

    [Fact]
    public async Task My_trips_refuses_an_anonymous_caller()
    {
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.GetAsync("/trips/mine");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task A_trip_the_member_was_removed_from_leaves_the_list()
    {
        // The cookie is a claim; the member row is the authority. Being removed
        // must take the trip out of the switcher, not leave a row that 403s.
        var owner = factory.CreateApiClient();
        var trip = await owner.CreateTripAsync(ownerDisplayName: "Owner");

        var joiner = factory.CreateApiClient();
        var joined = await joiner.JoinTripAsync(trip.Trip.InviteCode, "Linh");

        (await joiner.GetMyTripsAsync()).Should().ContainSingle();

        await factory.WithDbAsync(async db =>
        {
            var member = await db.Members.FirstAsync(m => m.Id == joined.Session.MemberId);
            db.Members.Remove(member);
            await db.SaveChangesAsync();
        });

        (await joiner.GetMyTripsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Forgetting_a_trip_removes_only_that_one()
    {
        var client = factory.CreateApiClient();
        var keep = await client.CreateTripAsync(name: "Giữ lại");
        var drop = await client.CreateTripAsync(name: "Bỏ đi");

        var response = await client.DeleteAsync($"/session/trips/{drop.Trip.Id}");
        await response.ShouldBeAsync(HttpStatusCode.NoContent);

        var remaining = await client.GetMyTripsAsync();
        remaining.Select(t => t.Id).Should().Equal(keep.Trip.Id);

        // Forgetting is a device-local act, never a deletion: the trip is still
        // there for everyone else, and for anyone with the invite code.
        (await client.GetAsync($"/trips/{drop.Trip.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Forgetting_the_last_trip_clears_the_session()
    {
        var client = factory.CreateApiClient();
        var only = await client.CreateTripAsync();

        await client.DeleteAsync($"/session/trips/{only.Trip.Id}");

        (await client.GetAsync("/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forgetting_a_trip_the_browser_never_held_is_a_no_op()
    {
        var client = factory.CreateApiClient();
        var mine = await client.CreateTripAsync();

        var response = await client.DeleteAsync($"/session/trips/{Guid.NewGuid()}");

        await response.ShouldBeAsync(HttpStatusCode.NoContent);
        (await client.GetMyTripsAsync()).Select(t => t.Id).Should().Equal(mine.Trip.Id);
    }

    [Fact]
    public async Task Holding_many_trips_still_reaches_the_oldest_of_them()
    {
        // Guards the ordering in the cookie: the most recent membership is
        // written first, and a naive reader that only ever looked at the head
        // would authorise the newest trip and 403 every other one.
        var client = factory.CreateApiClient();
        var first = await client.CreateTripAsync(name: "Đầu tiên");

        for (var i = 0; i < 5; i++)
        {
            await client.CreateTripAsync(name: $"Chuyến {i}");
        }

        (await client.GetAsync($"/trips/{first.Trip.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_browser_stops_holding_the_least_recent_trip_past_the_cap()
    {
        // The cookie has a size limit; something has to give. The oldest trip
        // falls off the device rather than the newest failing to be saved.
        var client = factory.CreateApiClient();
        var oldest = await client.CreateTripAsync(name: "Cũ nhất");

        for (var i = 0; i < WeGo.Api.Auth.SessionTokenService.MaxMemberships; i++)
        {
            await client.CreateTripAsync(name: $"Chuyến {i}");
        }

        (await client.GetMyTripsAsync()).Should()
            .HaveCount(WeGo.Api.Auth.SessionTokenService.MaxMemberships);
        (await client.GetAsync($"/trips/{oldest.Trip.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Session_reports_every_membership_the_browser_holds()
    {
        var client = factory.CreateApiClient();
        var first = await client.CreateTripAsync(name: "Một");
        var second = await client.CreateTripAsync(name: "Hai");

        var session = await client.GetFromJsonAsync<SessionEnvelope>("/session", ApiClient.Json);

        session!.Memberships.Select(m => m.TripId)
            .Should().BeEquivalentTo([first.Trip.Id, second.Trip.Id]);

        // The head is the most recent, which is what a client opens by default.
        session.TripId.Should().Be(second.Trip.Id);
    }

    private sealed record SessionEnvelope(Guid TripId, Guid MemberId, MembershipEnvelope[] Memberships);

    private sealed record MembershipEnvelope(Guid TripId, Guid MemberId);
}
