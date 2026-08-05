using WeGo.Domain.Itinerary;
using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

/// <summary>Spec §5.2 plus the §7 edge cases 1, 2, 3 and 5 that §9 names explicitly.</summary>
public sealed class FeasibilityTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static FeasibilityItem Item(
        string start,
        int durationMinutes = 60,
        TimeSlots slots = TimeSlots.Morning | TimeSlots.Noon | TimeSlots.Afternoon | TimeSlots.Evening,
        int createdOffsetSeconds = 0,
        Guid? itemId = null,
        Guid? placeId = null) =>
        new(
            itemId ?? Guid.NewGuid(),
            placeId ?? Guid.NewGuid(),
            start.Length == 0 ? null : TimeOnly.Parse(start),
            durationMinutes,
            slots,
            Epoch.AddSeconds(createdOffsetSeconds));

    private static Func<Guid, Guid, TravelLeg?> Travel(int minutes, TravelTimeSource source = TravelTimeSource.Osrm) =>
        (_, _) => new TravelLeg(minutes, source);

    private static readonly Func<Guid, Guid, TravelLeg?> NoTravel = (_, _) => null;

    private static FeasibilityFinding? Find(IReadOnlyList<FeasibilityFinding> findings, string code) =>
        findings.FirstOrDefault(f => f.Code == code);

    [Fact]
    public void An_empty_day_produces_nothing()
    {
        Feasibility.Analyze([], NoTravel).Should().BeEmpty();
    }

    [Fact]
    public void A_single_timed_item_has_no_pair_checks()
    {
        // Spec §7.1.
        var findings = Feasibility.Analyze([Item("09:00")], Travel(30));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Items_without_a_start_time_are_reported_but_not_paired()
    {
        var findings = Feasibility.Analyze([Item(""), Item("")], Travel(600));

        findings.Should().HaveCount(2);
        findings.Should().OnlyContain(f => f.Code == FeasibilityCodes.UnscheduledTime);
        findings.Should().OnlyContain(f => f.Level == FeasibilityLevel.Info);
    }

    [Fact]
    public void An_untimed_item_does_not_pair_with_a_timed_one()
    {
        // Spec §5.2: untimed items are excluded from pairing entirely.
        var findings = Feasibility.Analyze([Item("09:00"), Item("")], Travel(600));

        findings.Should().ContainSingle().Which.Code.Should().Be(FeasibilityCodes.UnscheduledTime);
    }

    [Fact]
    public void A_comfortable_gap_produces_no_finding()
    {
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("10:30")],
            Travel(20));

        findings.Should().BeEmpty("a 30 minute gap covers a 20 minute drive without idling");
    }

    [Fact]
    public void An_overlap_is_an_error_on_the_later_item()
    {
        var second = Guid.NewGuid();
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 120), Item("10:00", itemId: second)],
            Travel(10));

        var overlap = Find(findings, FeasibilityCodes.Overlap);
        overlap.Should().NotBeNull();
        overlap!.Level.Should().Be(FeasibilityLevel.Error);
        overlap.ItineraryItemId.Should().Be(second, "the later item is the one that cannot start on time");
        overlap.Data["overlapMinutes"].Should().Be(60);
    }

    [Fact]
    public void Two_items_at_the_same_time_overlap()
    {
        // Spec §7.2: identical start times with a non-zero duration is an overlap.
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60, createdOffsetSeconds: 0),
             Item("09:00", durationMinutes: 60, createdOffsetSeconds: 1)],
            Travel(5));

        Find(findings, FeasibilityCodes.Overlap).Should().NotBeNull();
    }

    [Fact]
    public void Pairing_order_for_equal_times_follows_created_at()
    {
        // Spec §7.2 asks for deterministic ordering, so the finding always
        // lands on the same item rather than varying with input order.
        var earlier = Guid.NewGuid();
        var later = Guid.NewGuid();

        var forwards = Feasibility.Analyze(
            [Item("09:00", createdOffsetSeconds: 0, itemId: earlier),
             Item("09:00", createdOffsetSeconds: 5, itemId: later)],
            Travel(5));

        var backwards = Feasibility.Analyze(
            [Item("09:00", createdOffsetSeconds: 5, itemId: later),
             Item("09:00", createdOffsetSeconds: 0, itemId: earlier)],
            Travel(5));

        Find(forwards, FeasibilityCodes.Overlap)!.ItineraryItemId.Should().Be(later);
        Find(backwards, FeasibilityCodes.Overlap)!.ItineraryItemId.Should().Be(later);
    }

    [Fact]
    public void A_gap_shorter_than_the_drive_is_an_error_carrying_the_numbers()
    {
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("10:10")],
            Travel(40));

        var insufficient = Find(findings, FeasibilityCodes.InsufficientTravelTime);
        insufficient.Should().NotBeNull();
        insufficient!.Level.Should().Be(FeasibilityLevel.Error);
        insufficient.Data["gapMinutes"].Should().Be(10);
        insufficient.Data["travelMinutes"].Should().Be(40);
        insufficient.Data["source"].Should().Be("osrm");
    }

    [Fact]
    public void A_gap_exactly_equal_to_the_drive_is_accepted()
    {
        // Spec §5.2 makes the error condition "gap < travel", so equality passes.
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("10:30")],
            Travel(30));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void An_estimated_travel_time_is_marked_as_such()
    {
        // Spec §5.4: the UI needs to be able to say "ước tính".
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("10:10")],
            Travel(40, TravelTimeSource.Haversine));

        Find(findings, FeasibilityCodes.InsufficientTravelTime)!
            .Data["source"].Should().Be("haversine");
    }

    [Fact]
    public void An_unknown_travel_time_produces_no_pair_finding()
    {
        // Spec §7.5 routes an unroutable pair to the haversine fallback, so a
        // null here means genuinely nothing to say rather than a silent pass.
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("10:01")],
            NoTravel);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void A_long_idle_gap_is_reported_as_information()
    {
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("13:00")],
            Travel(30));

        var idle = Find(findings, FeasibilityCodes.IdleGap);
        idle.Should().NotBeNull();
        idle!.Level.Should().Be(FeasibilityLevel.Info);
        idle.Data["idleMinutes"].Should().Be(150);
    }

    [Fact]
    public void A_gap_exactly_at_the_idle_threshold_is_not_reported()
    {
        // Spec §5.2 says "gap > travel + 90", so 120 with a 30 minute drive is fine.
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("12:00")],
            Travel(30));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void One_minute_past_the_idle_threshold_is_reported()
    {
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60), Item("12:01")],
            Travel(30));

        Find(findings, FeasibilityCodes.IdleGap).Should().NotBeNull();
    }

    [Fact]
    public void A_start_time_outside_the_places_slots_is_a_warning()
    {
        var findings = Feasibility.Analyze([Item("13:00", slots: TimeSlots.Morning)], NoTravel);

        var mismatch = Find(findings, FeasibilityCodes.TimeSlotMismatch);
        mismatch.Should().NotBeNull();
        mismatch!.Level.Should().Be(FeasibilityLevel.Warning);
        mismatch.Data["actualSlot"].Should().Be(nameof(TimeSlots.Noon));
    }

    [Theory]
    // The slot boundaries from spec §5.2, checked on the exact edge minutes.
    [InlineData("05:00", TimeSlots.Morning)]
    [InlineData("10:59", TimeSlots.Morning)]
    [InlineData("11:00", TimeSlots.Noon)]
    [InlineData("13:59", TimeSlots.Noon)]
    [InlineData("14:00", TimeSlots.Afternoon)]
    [InlineData("17:59", TimeSlots.Afternoon)]
    [InlineData("18:00", TimeSlots.Evening)]
    [InlineData("23:59", TimeSlots.Evening)]
    [InlineData("00:00", TimeSlots.Evening)]
    [InlineData("04:59", TimeSlots.Evening)]
    public void A_matching_slot_produces_no_warning(string start, TimeSlots slot)
    {
        Feasibility.Analyze([Item(start, slots: slot)], NoTravel)
            .Should().NotContain(f => f.Code == FeasibilityCodes.TimeSlotMismatch);
    }

    [Fact]
    public void An_item_running_past_midnight_is_reported_and_clamped()
    {
        // Spec §7.3 exactly: 23:30 plus 90 minutes.
        var findings = Feasibility.Analyze([Item("23:30", durationMinutes: 90)], NoTravel);

        var crosses = Find(findings, FeasibilityCodes.CrossesMidnight);
        crosses.Should().NotBeNull();
        crosses!.Level.Should().Be(FeasibilityLevel.Info);
        crosses.Data["clampedEnd"].Should().Be("23:59");
    }

    [Fact]
    public void An_item_ending_exactly_at_midnight_does_not_cross_it()
    {
        Feasibility.Analyze([Item("23:00", durationMinutes: 60)], NoTravel)
            .Should().NotContain(f => f.Code == FeasibilityCodes.CrossesMidnight);
    }

    [Fact]
    public void Clamping_keeps_the_gap_arithmetic_on_the_same_day()
    {
        // Without the clamp, 23:30 + 90 minutes would wrap to 01:00 and the gap
        // to a 23:45 item would come out as a comfortable +22h45m.
        var second = Guid.NewGuid();
        var findings = Feasibility.Analyze(
            [Item("23:30", durationMinutes: 90), Item("23:45", itemId: second)],
            Travel(5));

        var overlap = Find(findings, FeasibilityCodes.Overlap);
        overlap.Should().NotBeNull("the clamped end of 23:59 is after the 23:45 start");
        overlap!.ItineraryItemId.Should().Be(second);
    }

    [Fact]
    public void Every_consecutive_pair_is_checked_not_just_the_first()
    {
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 30),
             Item("10:00", durationMinutes: 30),
             Item("10:15", durationMinutes: 30)],
            Travel(5));

        // 09:00-09:30 then 10:00 is fine; 10:00-10:30 then 10:15 overlaps.
        findings.Count(f => f.Code == FeasibilityCodes.Overlap).Should().Be(1);
    }

    [Fact]
    public void Findings_of_different_kinds_can_apply_to_one_item()
    {
        var findings = Feasibility.Analyze(
            [Item("09:00", durationMinutes: 60, slots: TimeSlots.Morning),
             Item("10:10", slots: TimeSlots.Morning)],
            Travel(40));

        findings.Should().Contain(f => f.Code == FeasibilityCodes.InsufficientTravelTime);
        findings.Should().NotContain(f => f.Code == FeasibilityCodes.TimeSlotMismatch);
    }
}

public sealed class TravelEstimateTests
{
    [Fact]
    public void The_estimate_applies_the_road_factor_and_average_speed()
    {
        // Mộc Châu to Hà Nội, about 128 km straight line -> 173 km by road
        // at 32 km/h, which is a little over five hours.
        var minutes = TravelEstimate.MinutesBetween(20.8386, 104.6383, 21.0285, 105.8542);

        minutes.Should().BeInRange(310, 340);
    }

    [Fact]
    public void The_same_point_takes_no_time()
    {
        TravelEstimate.MinutesBetween(20.8386, 104.6383, 20.8386, 104.6383).Should().Be(0);
    }

    [Fact]
    public void A_short_hop_still_rounds_up_to_a_whole_minute()
    {
        // Rounding up keeps the estimate conservative: understating travel is
        // what makes a plan quietly impossible.
        var minutes = TravelEstimate.MinutesBetween(20.8386, 104.6383, 20.8390, 104.6390);

        minutes.Should().Be(1);
    }

    [Fact]
    public void The_estimate_is_symmetric()
    {
        var there = TravelEstimate.MinutesBetween(20.8, 104.6, 21.0, 105.8);
        var back = TravelEstimate.MinutesBetween(21.0, 105.8, 20.8, 104.6);

        there.Should().Be(back);
    }
}
