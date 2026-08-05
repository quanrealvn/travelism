using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

/// <summary>
/// Spec §9: "state-machine transition matrix (every pair, allowed + rejected)".
/// The matrix is enumerated rather than sampled, so a transition added to the
/// implementation without being added here fails the test.
/// </summary>
public sealed class PlaceStateMachineTests
{
    private static readonly PlaceTransitionContext Planning = new(TripStatus.Planning);
    private static readonly PlaceTransitionContext Ongoing = new(TripStatus.Ongoing);
    private static readonly PlaceTransitionContext Completed = new(TripStatus.Completed);

    /// <summary>
    /// The complete set of edges from spec §4, written out independently of the
    /// implementation so the test is a second reading of the spec rather than a
    /// mirror of the code.
    /// </summary>
    private static readonly HashSet<(PlaceStatus, PlaceStatus)> AllowedWhilePlanning =
    [
        (PlaceStatus.Idea, PlaceStatus.Shortlist),
        (PlaceStatus.Shortlist, PlaceStatus.Idea),
        (PlaceStatus.Shortlist, PlaceStatus.Confirmed),
        (PlaceStatus.Confirmed, PlaceStatus.Shortlist),
        (PlaceStatus.Visited, PlaceStatus.Skipped),
        (PlaceStatus.Skipped, PlaceStatus.Visited),
    ];

    private static readonly HashSet<(PlaceStatus, PlaceStatus)> AllowedOnlyOnceUnderway =
    [
        (PlaceStatus.Confirmed, PlaceStatus.Visited),
        (PlaceStatus.Confirmed, PlaceStatus.Skipped),
    ];

    public static TheoryData<PlaceStatus, PlaceStatus> EveryPair()
    {
        var data = new TheoryData<PlaceStatus, PlaceStatus>();
        foreach (var from in PlaceStateMachine.AllStatuses)
        {
            foreach (var to in PlaceStateMachine.AllStatuses)
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_pair_matches_the_spec_while_planning(PlaceStatus from, PlaceStatus to)
    {
        var expected = from == to || AllowedWhilePlanning.Contains((from, to));

        PlaceStateMachine.IsAllowed(from, to, Planning).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_pair_matches_the_spec_once_the_trip_is_ongoing(PlaceStatus from, PlaceStatus to)
    {
        var expected = from == to
                       || AllowedWhilePlanning.Contains((from, to))
                       || AllowedOnlyOnceUnderway.Contains((from, to));

        PlaceStateMachine.IsAllowed(from, to, Ongoing).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void A_completed_trip_allows_the_same_pairs_as_an_ongoing_one(PlaceStatus from, PlaceStatus to)
    {
        // Recording that you visited somewhere after getting home is normal.
        PlaceStateMachine.IsAllowed(from, to, Completed)
            .Should().Be(PlaceStateMachine.IsAllowed(from, to, Ongoing));
    }

    [Theory]
    [InlineData(PlaceStatus.Confirmed, PlaceStatus.Visited)]
    [InlineData(PlaceStatus.Confirmed, PlaceStatus.Skipped)]
    public void Visiting_before_the_trip_starts_is_refused_for_the_right_reason(
        PlaceStatus from,
        PlaceStatus to)
    {
        // The distinction matters: the client should say "the trip has not
        // started", not "that is not a valid change".
        PlaceStateMachine.Check(from, to, Planning).Should().Be(TransitionRefusal.TripNotStarted);
        PlaceStateMachine.Check(from, to, Ongoing).Should().Be(TransitionRefusal.None);
    }

    [Theory]
    [InlineData(PlaceStatus.Idea, PlaceStatus.Confirmed)]
    [InlineData(PlaceStatus.Idea, PlaceStatus.Visited)]
    [InlineData(PlaceStatus.Idea, PlaceStatus.Skipped)]
    [InlineData(PlaceStatus.Shortlist, PlaceStatus.Visited)]
    [InlineData(PlaceStatus.Shortlist, PlaceStatus.Skipped)]
    [InlineData(PlaceStatus.Confirmed, PlaceStatus.Idea)]
    [InlineData(PlaceStatus.Visited, PlaceStatus.Idea)]
    [InlineData(PlaceStatus.Visited, PlaceStatus.Shortlist)]
    [InlineData(PlaceStatus.Visited, PlaceStatus.Confirmed)]
    [InlineData(PlaceStatus.Skipped, PlaceStatus.Idea)]
    [InlineData(PlaceStatus.Skipped, PlaceStatus.Shortlist)]
    [InlineData(PlaceStatus.Skipped, PlaceStatus.Confirmed)]
    public void Edges_outside_the_table_are_refused_in_every_trip_status(PlaceStatus from, PlaceStatus to)
    {
        foreach (var context in new[] { Planning, Ongoing, Completed })
        {
            PlaceStateMachine.Check(from, to, context)
                .Should().Be(TransitionRefusal.NotAllowed, "{0} → {1} is not in spec §4", from, to);
        }
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void A_transition_to_the_same_status_is_never_an_error(PlaceStatus from, PlaceStatus to)
    {
        if (from != to)
        {
            return;
        }

        PlaceStateMachine.Check(from, to, Planning).Should().Be(TransitionRefusal.None);
    }

    [Theory]
    [InlineData(PlaceStatus.Idea, PlaceStatus.Shortlist)]
    [InlineData(PlaceStatus.Shortlist, PlaceStatus.Idea)]
    public void The_like_driven_edges_are_refused_when_asked_for_directly(PlaceStatus from, PlaceStatus to)
    {
        // Legal as an effect of liking, refused as a decision — setting them
        // directly would leave the status disagreeing with the vote.
        PlaceStateMachine.Check(from, to, Planning).Should().Be(TransitionRefusal.None);
        PlaceStateMachine.CheckManual(from, to, Planning).Should().Be(TransitionRefusal.LikeDriven);
    }

    [Theory]
    [InlineData(PlaceStatus.Shortlist, PlaceStatus.Confirmed)]
    [InlineData(PlaceStatus.Confirmed, PlaceStatus.Shortlist)]
    public void The_deliberate_edges_stay_available_directly(PlaceStatus from, PlaceStatus to)
    {
        // Force-confirming and un-confirming are decisions spec §4 gives to a
        // member, so they must survive the stricter check.
        PlaceStateMachine.CheckManual(from, to, Planning).Should().Be(TransitionRefusal.None);
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void CheckManual_never_permits_more_than_Check(PlaceStatus from, PlaceStatus to)
    {
        foreach (var context in new[] { Planning, Ongoing, Completed })
        {
            if (PlaceStateMachine.CheckManual(from, to, context) == TransitionRefusal.None)
            {
                PlaceStateMachine.Check(from, to, context)
                    .Should().Be(TransitionRefusal.None, "the manual check is a narrowing, never a widening");
            }
        }
    }

    public sealed class StatusForLikes
    {
        [Fact]
        public void No_likes_leaves_a_place_as_an_idea()
        {
            PlaceStateMachine.StatusForLikes(PlaceStatus.Idea, likeCount: 0, memberCount: 2)
                .Should().Be(PlaceStatus.Idea);
        }

        [Fact]
        public void One_like_out_of_two_shortlists_it()
        {
            PlaceStateMachine.StatusForLikes(PlaceStatus.Idea, likeCount: 1, memberCount: 2)
                .Should().Be(PlaceStatus.Shortlist);
        }

        [Fact]
        public void Everyone_liking_it_confirms_it()
        {
            PlaceStateMachine.StatusForLikes(PlaceStatus.Shortlist, likeCount: 2, memberCount: 2)
                .Should().Be(PlaceStatus.Confirmed);
        }

        [Fact]
        public void Removing_the_last_like_returns_it_to_an_idea()
        {
            PlaceStateMachine.StatusForLikes(PlaceStatus.Shortlist, likeCount: 0, memberCount: 2)
                .Should().Be(PlaceStatus.Idea);
        }

        [Fact]
        public void A_confirmed_place_is_not_demoted_when_a_like_is_withdrawn()
        {
            // Spec §4 makes leaving Confirmed a deliberate act, never arithmetic.
            PlaceStateMachine.StatusForLikes(PlaceStatus.Confirmed, likeCount: 1, memberCount: 2)
                .Should().Be(PlaceStatus.Confirmed);
        }

        [Fact]
        public void A_confirmed_place_survives_a_new_member_joining()
        {
            // Spec §4: "existing Confirmed places stay confirmed (do NOT
            // retro-demote)" — the member count grew, the likes did not.
            PlaceStateMachine.StatusForLikes(PlaceStatus.Confirmed, likeCount: 2, memberCount: 5)
                .Should().Be(PlaceStatus.Confirmed);
        }

        [Theory]
        [InlineData(PlaceStatus.Visited)]
        [InlineData(PlaceStatus.Skipped)]
        public void An_outcome_is_never_changed_by_likes(PlaceStatus status)
        {
            PlaceStateMachine.StatusForLikes(status, likeCount: 0, memberCount: 2).Should().Be(status);
            PlaceStateMachine.StatusForLikes(status, likeCount: 2, memberCount: 2).Should().Be(status);
        }

        [Fact]
        public void More_likes_than_members_still_confirms_rather_than_overflowing()
        {
            // Can happen momentarily if a member is removed between reads.
            PlaceStateMachine.StatusForLikes(PlaceStatus.Shortlist, likeCount: 3, memberCount: 2)
                .Should().Be(PlaceStatus.Confirmed);
        }

        [Fact]
        public void A_like_on_a_memberless_trip_does_not_confirm()
        {
            // Defensive: a trip always has its owner, so this state is
            // unreachable — but "0 >= 0" would otherwise confirm on one like.
            PlaceStateMachine.StatusForLikes(PlaceStatus.Idea, likeCount: 1, memberCount: 0)
                .Should().Be(PlaceStatus.Shortlist);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(10)]
        public void A_solo_or_group_trip_confirms_only_at_unanimity(int memberCount)
        {
            for (var likes = 0; likes < memberCount; likes++)
            {
                var expected = likes == 0 ? PlaceStatus.Idea : PlaceStatus.Shortlist;
                PlaceStateMachine.StatusForLikes(PlaceStatus.Idea, likes, memberCount)
                    .Should().Be(expected);
            }

            PlaceStateMachine.StatusForLikes(PlaceStatus.Idea, memberCount, memberCount)
                .Should().Be(PlaceStatus.Confirmed);
        }
    }
}
