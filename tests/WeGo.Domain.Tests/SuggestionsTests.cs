using WeGo.Domain.Itinerary;
using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

/// <summary>Spec §5.1 ordering, which §9 requires to be unit-tested as a pure function.</summary>
public sealed class SuggestionsTests
{
    private static SuggestionCandidate Candidate(
        string name,
        PlaceCategory category = PlaceCategory.Sight,
        TimeSlots slots = TimeSlots.Morning,
        long? cost = null) =>
        new(Guid.NewGuid(), name, category, slots, cost);

    private static IReadOnlyList<SuggestionCandidate> Group(
        IReadOnlyList<SuggestionGroup> groups,
        TimeSlots slot) =>
        groups.Single(g => g.Slot == slot).Places;

    [Fact]
    public void Every_slot_is_present_even_when_empty()
    {
        // The client renders four columns whether or not they have content.
        var groups = Suggestions.Build([], []);

        groups.Select(g => g.Slot).Should().Equal(TimeSlotSet.All);
        groups.Should().OnlyContain(g => g.Places.Count == 0);
    }

    [Fact]
    public void A_place_with_several_slots_appears_in_each_of_them()
    {
        var candidate = Candidate("Đồi chè", slots: TimeSlots.Morning | TimeSlots.Evening);

        var groups = Suggestions.Build([candidate], []);

        Group(groups, TimeSlots.Morning).Should().ContainSingle();
        Group(groups, TimeSlots.Evening).Should().ContainSingle();
        Group(groups, TimeSlots.Noon).Should().BeEmpty();
        Group(groups, TimeSlots.Afternoon).Should().BeEmpty();
    }

    [Fact]
    public void A_category_unlike_what_is_planned_comes_first()
    {
        var food = Candidate("Quán phở", PlaceCategory.Food, cost: 10_000);
        var sight = Candidate("Thác", PlaceCategory.Sight, cost: 90_000);

        // A Food place is already on the morning, so Food is the repetitive pick
        // even though it is much cheaper.
        var scheduled = new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, new TimeOnly(8, 0));

        var morning = Group(Suggestions.Build([food, sight], [scheduled]), TimeSlots.Morning);

        morning.Select(p => p.Name).Should().Equal("Thác", "Quán phở");
    }

    [Fact]
    public void Variety_outranks_price()
    {
        // Explicit: rule (a) is applied before rule (b), not blended with it.
        var cheapRepeat = Candidate("Cheap food", PlaceCategory.Food, cost: 1);
        var expensiveNew = Candidate("Pricey sight", PlaceCategory.Sight, cost: 10_000_000);
        var scheduled = new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, new TimeOnly(9, 0));

        var morning = Group(Suggestions.Build([cheapRepeat, expensiveNew], [scheduled]), TimeSlots.Morning);

        morning[0].Name.Should().Be("Pricey sight");
    }

    [Fact]
    public void Within_the_same_novelty_the_cheaper_place_comes_first()
    {
        var expensive = Candidate("Expensive", PlaceCategory.Sight, cost: 500_000);
        var cheap = Candidate("Cheap", PlaceCategory.Sight, cost: 20_000);

        var morning = Group(Suggestions.Build([expensive, cheap], []), TimeSlots.Morning);

        morning.Select(p => p.Name).Should().Equal("Cheap", "Expensive");
    }

    [Fact]
    public void An_unknown_cost_sorts_last_rather_than_as_free()
    {
        var unknown = Candidate("Unknown cost", PlaceCategory.Sight, cost: null);
        var cheap = Candidate("Cheap", PlaceCategory.Sight, cost: 5_000);
        var free = Candidate("Free", PlaceCategory.Sight, cost: 0);

        var morning = Group(Suggestions.Build([unknown, cheap, free], []), TimeSlots.Morning);

        // Zero is a known price and sorts first; null is not a price at all.
        morning.Select(p => p.Name).Should().Equal("Free", "Cheap", "Unknown cost");
    }

    [Fact]
    public void Only_the_matching_slot_is_affected_by_what_is_scheduled()
    {
        var food = Candidate("Food", PlaceCategory.Food, TimeSlots.Morning | TimeSlots.Evening, 10_000);
        var sight = Candidate("Sight", PlaceCategory.Sight, TimeSlots.Morning | TimeSlots.Evening, 90_000);

        // A Food place at 08:00 occupies the morning only.
        var scheduled = new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, new TimeOnly(8, 0));
        var groups = Suggestions.Build([food, sight], [scheduled]);

        Group(groups, TimeSlots.Morning).Select(p => p.Name).Should().Equal("Sight", "Food");
        Group(groups, TimeSlots.Evening).Select(p => p.Name).Should().Equal("Food", "Sight");
    }

    [Fact]
    public void An_item_with_no_start_time_does_not_suppress_any_slot()
    {
        // It has not claimed a part of the day, so it must not make every
        // same-category suggestion look repetitive everywhere.
        var food = Candidate("Food", PlaceCategory.Food, TimeSlots.Morning, 10_000);
        var sight = Candidate("Sight", PlaceCategory.Sight, TimeSlots.Morning, 90_000);
        var untimed = new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, StartTime: null);

        var morning = Group(Suggestions.Build([food, sight], [untimed]), TimeSlots.Morning);

        morning.Select(p => p.Name)
            .Should().Equal(new[] { "Food", "Sight" }, "cheapest first, with novelty untouched");
    }

    [Fact]
    public void The_late_night_hours_count_against_the_evening()
    {
        var food = Candidate("Food", PlaceCategory.Food, TimeSlots.Evening, 10_000);
        var sight = Candidate("Sight", PlaceCategory.Sight, TimeSlots.Evening, 90_000);

        // 01:00 belongs to Evening (spec §5.2), so it suppresses Food there.
        var lateNight = new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, new TimeOnly(1, 0));

        var evening = Group(Suggestions.Build([food, sight], [lateNight]), TimeSlots.Evening);

        evening.Select(p => p.Name).Should().Equal("Sight", "Food");
    }

    [Fact]
    public void Ordering_is_total_so_the_same_inputs_give_the_same_list()
    {
        // Identical category and cost: without a tie-break the order would
        // depend on enumeration and the list would shuffle between requests.
        var a = Candidate("Alpha", PlaceCategory.Sight, cost: 50_000);
        var b = Candidate("Beta", PlaceCategory.Sight, cost: 50_000);

        var first = Group(Suggestions.Build([a, b], []), TimeSlots.Morning).Select(p => p.Name);
        var second = Group(Suggestions.Build([b, a], []), TimeSlots.Morning).Select(p => p.Name);

        first.Should().Equal(second).And.Equal("Alpha", "Beta");
    }

    [Fact]
    public void Several_scheduled_categories_are_all_treated_as_repetition()
    {
        var food = Candidate("Food", PlaceCategory.Food, cost: 1_000);
        var sight = Candidate("Sight", PlaceCategory.Sight, cost: 2_000);
        var photo = Candidate("Photo", PlaceCategory.Photo, cost: 900_000);

        var scheduled = new[]
        {
            new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, new TimeOnly(8, 0)),
            new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Sight, new TimeOnly(9, 0)),
        };

        var morning = Group(Suggestions.Build([food, sight, photo], scheduled), TimeSlots.Morning);

        // Photo is the only fresh category, despite being far the most expensive.
        morning.Select(p => p.Name).Should().Equal("Photo", "Food", "Sight");
    }

    [Fact]
    public void An_empty_candidate_list_yields_empty_groups_rather_than_throwing()
    {
        var scheduled = new ScheduledPlace(Guid.NewGuid(), PlaceCategory.Food, new TimeOnly(8, 0));

        var groups = Suggestions.Build([], [scheduled]);

        groups.Should().HaveCount(4);
        groups.Should().OnlyContain(g => g.Places.Count == 0);
    }
}
