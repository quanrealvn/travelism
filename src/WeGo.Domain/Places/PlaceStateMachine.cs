namespace WeGo.Domain.Places;

/// <summary>Everything a status transition is allowed to depend on.</summary>
/// <param name="TripStatus">Visiting and skipping are only meaningful once the trip is under way.</param>
public readonly record struct PlaceTransitionContext(TripStatus TripStatus);

/// <summary>Why a transition was refused. <c>None</c> means it was allowed.</summary>
public enum TransitionRefusal
{
    None = 0,

    /// <summary>The edge does not exist in the state machine at all.</summary>
    NotAllowed = 1,

    /// <summary>The edge exists but the trip has not started yet.</summary>
    TripNotStarted = 2,

    /// <summary>
    /// The edge exists, but only as a consequence of likes — setting it
    /// directly would contradict the vote it is supposed to reflect.
    /// </summary>
    LikeDriven = 3,
}

/// <summary>
/// Spec §4, as a pure function over the transition table. No database, no HTTP —
/// the whole matrix is unit-testable, which matters because "which transitions
/// are legal" is the kind of rule that quietly rots as endpoints are added.
/// </summary>
public static class PlaceStateMachine
{
    /// <summary>
    /// Edges that exist regardless of trip status. Demotions here are always
    /// manual: nothing automatic ever moves a place *down* out of Confirmed
    /// (see <see cref="StatusForLikes"/>).
    /// </summary>
    private static readonly (PlaceStatus From, PlaceStatus To)[] AlwaysAllowed =
    [
        (PlaceStatus.Idea, PlaceStatus.Shortlist),
        (PlaceStatus.Shortlist, PlaceStatus.Idea),
        (PlaceStatus.Shortlist, PlaceStatus.Confirmed),
        (PlaceStatus.Confirmed, PlaceStatus.Shortlist),
        // Correcting a mistake after the fact, in either direction.
        (PlaceStatus.Visited, PlaceStatus.Skipped),
        (PlaceStatus.Skipped, PlaceStatus.Visited),
    ];

    /// <summary>Edges that additionally require the trip to be Ongoing or Completed.</summary>
    private static readonly (PlaceStatus From, PlaceStatus To)[] RequiresTripUnderway =
    [
        (PlaceStatus.Confirmed, PlaceStatus.Visited),
        (PlaceStatus.Confirmed, PlaceStatus.Skipped),
    ];

    /// <summary>
    /// Edges spec §4 attributes to likes rather than to a decision: "any member
    /// likes it" and "all likes removed". They are legal transitions, but only
    /// as an effect of <see cref="StatusForLikes"/>.
    /// <para>
    /// Letting a member set them directly would produce a Shortlist place that
    /// nobody has liked — a state the next like or unlike would immediately
    /// undo, leaving the vote and the status disagreeing in between.
    /// </para>
    /// </summary>
    private static readonly (PlaceStatus From, PlaceStatus To)[] LikeDrivenOnly =
    [
        (PlaceStatus.Idea, PlaceStatus.Shortlist),
        (PlaceStatus.Shortlist, PlaceStatus.Idea),
    ];

    /// <summary>Every status, for exhaustive testing of the transition matrix.</summary>
    public static IReadOnlyList<PlaceStatus> AllStatuses { get; } = Enum.GetValues<PlaceStatus>();

    /// <summary>True when this edge may only happen as a result of liking or unliking.</summary>
    public static bool IsLikeDriven(PlaceStatus from, PlaceStatus to) =>
        LikeDrivenOnly.Contains((from, to));

    public static bool IsTripUnderway(TripStatus status) =>
        status is TripStatus.Ongoing or TripStatus.Completed;

    /// <summary>Checks a transition, returning why it was refused if it was.</summary>
    public static TransitionRefusal Check(
        PlaceStatus from,
        PlaceStatus to,
        PlaceTransitionContext context)
    {
        // A no-op is not a state change, so there is nothing to refuse. Callers
        // treat this as "already there" rather than as an error.
        if (from == to)
        {
            return TransitionRefusal.None;
        }

        if (AlwaysAllowed.Contains((from, to)))
        {
            return TransitionRefusal.None;
        }

        if (RequiresTripUnderway.Contains((from, to)))
        {
            return IsTripUnderway(context.TripStatus)
                ? TransitionRefusal.None
                : TransitionRefusal.TripNotStarted;
        }

        return TransitionRefusal.NotAllowed;
    }

    public static bool IsAllowed(PlaceStatus from, PlaceStatus to, PlaceTransitionContext context) =>
        Check(from, to, context) == TransitionRefusal.None;

    /// <summary>
    /// Checks a transition a member asked for explicitly. Stricter than
    /// <see cref="Check"/>: it additionally refuses the edges that are supposed
    /// to follow from likes.
    /// </summary>
    public static TransitionRefusal CheckManual(
        PlaceStatus from,
        PlaceStatus to,
        PlaceTransitionContext context)
    {
        if (from != to && IsLikeDriven(from, to))
        {
            return TransitionRefusal.LikeDriven;
        }

        return Check(from, to, context);
    }

    /// <summary>
    /// The status a place should hold given who has liked it.
    /// <para>
    /// Only ever promotes. Spec §4 makes every demotion out of Confirmed a
    /// deliberate act ("any member un-confirms"), and pairs that with "adding a
    /// member must not retro-demote" — both say the same thing: once a group has
    /// agreed on a place, arithmetic must not quietly un-agree it. So a Confirmed
    /// place that loses a like, or gains a member who has not liked it yet, stays
    /// Confirmed until somebody says otherwise.
    /// </para>
    /// <para>
    /// Visited and Skipped are outcomes rather than opinions, so likes do not
    /// touch them either.
    /// </para>
    /// </summary>
    public static PlaceStatus StatusForLikes(PlaceStatus current, int likeCount, int memberCount)
    {
        if (current is PlaceStatus.Confirmed or PlaceStatus.Visited or PlaceStatus.Skipped)
        {
            return current;
        }

        if (likeCount <= 0)
        {
            return PlaceStatus.Idea;
        }

        // memberCount is guarded because a trip always has at least its owner;
        // a zero would otherwise confirm on the first like.
        return memberCount > 0 && likeCount >= memberCount
            ? PlaceStatus.Confirmed
            : PlaceStatus.Shortlist;
    }
}
