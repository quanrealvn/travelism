using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;
using WeGo.Domain.Places;

namespace WeGo.Api.Tests;

/// <summary>
/// Reviewer step 2: every illegal transition attempted through the public API,
/// not the service layer, and auto-confirm checked at exactly the boundary.
/// </summary>
public sealed class PlaceStatusTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    private static Task<HttpResponseMessage> SetStatusAsync(
        HttpClient client,
        Guid tripId,
        Guid placeId,
        string status,
        string? skipReason = null) =>
        client.PostAsJsonAsync(
            $"/trips/{tripId}/places/{placeId}/status",
            new { status, skipReason },
            ApiClient.Json);

    /// <summary>Drives a place to a starting status through legal moves only.</summary>
    private async Task<(HttpClient Client, Guid TripId, Guid PlaceId)> ArrangeAsync(
        PlaceStatus start,
        string owner,
        bool tripUnderway = false)
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: owner, name: $"Trip {owner}");
        var place = await client.CreatePlaceAsync(trip.Trip.Id, name: $"Place {owner}");

        // A one-member trip confirms on a single like, which keeps the setup
        // for each starting status to the shortest legal path.
        switch (start)
        {
            case PlaceStatus.Idea:
                break;

            case PlaceStatus.Shortlist:
                // Solo trip: one like confirms, and un-confirming is the
                // deliberate edge back down to Shortlist.
                await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
                await SetStatusAsync(client, trip.Trip.Id, place.Id, "Shortlist");
                break;

            case PlaceStatus.Confirmed:
                await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
                break;

            case PlaceStatus.Visited:
            case PlaceStatus.Skipped:
                await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
                await SetTripOngoingAsync(trip.Trip.Id);
                await SetStatusAsync(client, trip.Trip.Id, place.Id, start.ToString());
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(start), start, null);
        }

        if (tripUnderway)
        {
            await SetTripOngoingAsync(trip.Trip.Id);
        }

        var current = await client.GetFromJsonAsync<PlaceResponse>(
            $"/trips/{trip.Trip.Id}/places/{place.Id}", ApiClient.Json);
        current!.Status.Should().Be(start.ToString(), "the arrangement must actually reach {0}", start);

        return (client, trip.Trip.Id, place.Id);
    }

    private Task SetTripOngoingAsync(Guid tripId) =>
        factory.WithDbAsync(async db =>
        {
            var trip = await db.Trips.FindAsync(tripId);
            trip!.Status = TripStatus.Ongoing;
            await db.SaveChangesAsync();
        });

    public static TheoryData<PlaceStatus, PlaceStatus> IllegalWhilePlanning()
    {
        // Idea <-> Shortlist are deliberately absent: they are legal moves, but
        // only as a consequence of liking, so asking for them directly is
        // refused too. Covered by Moving_between_idea_and_shortlist_directly.
        var allowed = new HashSet<(PlaceStatus, PlaceStatus)>
        {
            (PlaceStatus.Shortlist, PlaceStatus.Confirmed),
            (PlaceStatus.Confirmed, PlaceStatus.Shortlist),
            (PlaceStatus.Visited, PlaceStatus.Skipped),
            (PlaceStatus.Skipped, PlaceStatus.Visited),
        };

        var data = new TheoryData<PlaceStatus, PlaceStatus>();
        foreach (var from in PlaceStateMachine.AllStatuses)
        {
            foreach (var to in PlaceStateMachine.AllStatuses)
            {
                if (from != to && !allowed.Contains((from, to)))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IllegalWhilePlanning))]
    public async Task Every_illegal_transition_is_refused_through_the_api(PlaceStatus from, PlaceStatus to)
    {
        var (client, tripId, placeId) = await ArrangeAsync(from, $"Illegal{from}{to}");

        var response = await SetStatusAsync(client, tripId, placeId, to.ToString());

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.InvalidStatusTransition);

        // The refused change must not have been applied.
        var reread = await client.GetFromJsonAsync<PlaceResponse>(
            $"/trips/{tripId}/places/{placeId}", ApiClient.Json);
        reread!.Status.Should().Be(from.ToString());
    }

    [Theory]
    [InlineData(PlaceStatus.Visited)]
    [InlineData(PlaceStatus.Skipped)]
    public async Task Visiting_is_refused_until_the_trip_starts(PlaceStatus target)
    {
        var (client, tripId, placeId) = await ArrangeAsync(PlaceStatus.Confirmed, $"Early{target}");

        var refused = await SetStatusAsync(client, tripId, placeId, target.ToString());
        await refused.ShouldBeAsync(HttpStatusCode.Conflict);
        (await refused.ReadProblemAsync()).Code.Should().Be(ErrorCodes.InvalidStatusTransition);

        await SetTripOngoingAsync(tripId);

        var allowed = await SetStatusAsync(client, tripId, placeId, target.ToString());
        await allowed.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(PlaceStatus.Idea, "Shortlist")]
    [InlineData(PlaceStatus.Shortlist, "Idea")]
    public async Task Moving_between_idea_and_shortlist_directly_is_refused(
        PlaceStatus from,
        string to)
    {
        // These follow from liking. Setting them by hand would leave a place
        // shortlisted that nobody voted for.
        var (client, tripId, placeId) = await ArrangeAsync(from, $"LikeDriven{from}{to}");

        var response = await SetStatusAsync(client, tripId, placeId, to);

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.InvalidStatusTransition);
        problem.Detail.Should().Contain("liking");

        var reread = await client.GetFromJsonAsync<PlaceResponse>(
            $"/trips/{tripId}/places/{placeId}", ApiClient.Json);
        reread!.Status.Should().Be(from.ToString());
    }

    [Fact]
    public async Task A_single_like_on_a_solo_trip_confirms_immediately()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Solo");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var liked = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        liked!.Status.Should().Be(nameof(PlaceStatus.Confirmed), "the only member has liked it");
        liked.LikedByMemberIds.Should().Equal(trip.Session.MemberId);
    }

    [Fact]
    public async Task Auto_confirm_happens_exactly_when_the_last_member_likes()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Quan", name: "Two-person");
        var place = await ownerClient.CreatePlaceAsync(trip.Trip.Id);

        var joiner = factory.CreateApiClient();
        await joiner.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "Linh",
        }, ApiClient.Json);

        var afterFirst = await ownerClient.PostAsync(
            $"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
        var shortlisted = await afterFirst.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        shortlisted!.Status.Should().Be(nameof(PlaceStatus.Shortlist), "one of two members");

        var afterSecond = await joiner.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
        var confirmed = await afterSecond.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        confirmed!.Status.Should().Be(nameof(PlaceStatus.Confirmed), "both members have liked it");
        confirmed.LikedByMemberIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_new_member_does_not_retro_demote_a_confirmed_place()
    {
        // Spec §4, and the reviewer checks for it by name.
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Owner", name: "Retro");
        var place = await ownerClient.CreatePlaceAsync(trip.Trip.Id);

        await ownerClient.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);

        var joiner = factory.CreateApiClient();
        await joiner.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "Latecomer",
        }, ApiClient.Json);

        var reread = await ownerClient.GetFromJsonAsync<PlaceResponse>(
            $"/trips/{trip.Trip.Id}/places/{place.Id}", ApiClient.Json);

        reread!.Status.Should().Be(
            nameof(PlaceStatus.Confirmed),
            "a member joining must not un-agree what the group already agreed");
    }

    [Fact]
    public async Task Liking_twice_is_a_no_op()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Repeater");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
        var second = await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);

        await second.ShouldBeAsync(HttpStatusCode.OK);
        var place2 = await second.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        place2!.LikedByMemberIds.Should().HaveCount(1);

        var rows = await factory.WithDbAsync(db => db.PlaceLikes.CountAsync(l => l.PlaceId == place.Id));
        rows.Should().Be(1, "the composite key makes a duplicate impossible");
    }

    [Fact]
    public async Task Unliking_something_you_never_liked_is_a_no_op()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "NeverLiked");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var unchanged = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        unchanged!.Status.Should().Be(nameof(PlaceStatus.Idea));
    }

    [Fact]
    public async Task Removing_the_only_like_returns_the_place_to_an_idea()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Quan2", name: "Unlike");
        var place = await ownerClient.CreatePlaceAsync(trip.Trip.Id);

        var joiner = factory.CreateApiClient();
        await joiner.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "Linh2",
        }, ApiClient.Json);

        await ownerClient.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
        var response = await ownerClient.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like");

        var place2 = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        place2!.Status.Should().Be(nameof(PlaceStatus.Idea));
        place2.LikedByMemberIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Withdrawing_a_like_does_not_demote_a_confirmed_place()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Steadfast");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        await client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
        var response = await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like");

        var place2 = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        place2!.Status.Should().Be(
            nameof(PlaceStatus.Confirmed),
            "leaving Confirmed is a deliberate act, never arithmetic");
        place2.LikedByMemberIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Force_confirming_is_logged_as_such()
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Forcer2", name: "Force");
        var place = await ownerClient.CreatePlaceAsync(trip.Trip.Id);

        var joiner = factory.CreateApiClient();
        await joiner.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = trip.Trip.InviteCode,
            displayName = "Undecided",
        }, ApiClient.Json);

        await ownerClient.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null);
        var response = await SetStatusAsync(ownerClient, trip.Trip.Id, place.Id, "Confirmed");

        await response.ShouldBeAsync(HttpStatusCode.OK);

        var actions = await factory.WithDbAsync(db => db.ActivityLogs
            .Where(a => a.EntityId == place.Id)
            .Select(a => a.Action)
            .ToListAsync());

        actions.Should().Contain(
            ActivityAction.ForceConfirmed,
            "spec §4 asks for the manual path to be distinguishable from agreement");
    }

    [Fact]
    public async Task A_skip_reason_is_kept_and_cleared_again_on_correction()
    {
        var (client, tripId, placeId) = await ArrangeAsync(
            PlaceStatus.Confirmed, "Skipper", tripUnderway: true);

        var skipped = await SetStatusAsync(client, tripId, placeId, "Skipped", "Trời mưa to");
        await skipped.ShouldBeAsync(HttpStatusCode.OK);
        (await skipped.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json))!
            .SkipReason.Should().Be("Trời mưa to");

        // Visited ↔ Skipped is the correction path; the stale reason must go.
        var corrected = await SetStatusAsync(client, tripId, placeId, "Visited");
        (await corrected.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json))!
            .SkipReason.Should().BeNull("a visited place has no reason for being skipped");
    }

    [Fact]
    public async Task An_unknown_status_is_unprocessable_rather_than_a_conflict()
    {
        var (client, tripId, placeId) = await ArrangeAsync(PlaceStatus.Idea, "BadStatus");

        var response = await SetStatusAsync(client, tripId, placeId, "Pending");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "status");
    }

    [Fact]
    public async Task Liking_a_place_in_another_trip_is_refused()
    {
        var attacker = factory.CreateApiClient();
        await attacker.CreateTripAsync(ownerDisplayName: "LikeAttacker", name: "Attacker");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "LikeVictim", name: "Victim");
        var place = await victimClient.CreatePlaceAsync(victim.Trip.Id);

        var response = await attacker.PostAsync($"/trips/{victim.Trip.Id}/places/{place.Id}/like", null);

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrent_likes_from_the_same_member_produce_one_row()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "RaceLiker");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            client.PostAsync($"/trips/{trip.Trip.Id}/places/{place.Id}/like", null)));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);

        var rows = await factory.WithDbAsync(db => db.PlaceLikes.CountAsync(l => l.PlaceId == place.Id));
        rows.Should().Be(1);
    }
}
