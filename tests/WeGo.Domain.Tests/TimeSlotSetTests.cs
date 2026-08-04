using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

public sealed class TimeSlotSetTests
{
    [Theory]
    // Spec §5.2 boundaries, asserted on the exact edge minutes on both sides.
    [InlineData(5, 0, TimeSlots.Morning)]
    [InlineData(10, 59, TimeSlots.Morning)]
    [InlineData(11, 0, TimeSlots.Noon)]
    [InlineData(13, 59, TimeSlots.Noon)]
    [InlineData(14, 0, TimeSlots.Afternoon)]
    [InlineData(17, 59, TimeSlots.Afternoon)]
    [InlineData(18, 0, TimeSlots.Evening)]
    [InlineData(23, 59, TimeSlots.Evening)]
    // 00:00–04:59 wraps back onto Evening rather than forming a fifth bucket.
    [InlineData(0, 0, TimeSlots.Evening)]
    [InlineData(4, 59, TimeSlots.Evening)]
    public void ForTime_maps_each_boundary_to_the_documented_slot(int hour, int minute, TimeSlots expected)
    {
        TimeSlotSet.ForTime(new TimeOnly(hour, minute)).Should().Be(expected);
    }

    [Fact]
    public void ForTime_covers_every_minute_of_the_day_with_exactly_one_slot()
    {
        for (var minutes = 0; minutes < 24 * 60; minutes++)
        {
            var slot = TimeSlotSet.ForTime(new TimeOnly(minutes / 60, minutes % 60));

            slot.Should().NotBe(TimeSlots.None, "every wall-clock minute belongs to a slot");
            TimeSlotSet.All.Should().Contain(slot);
        }
    }

    [Fact]
    public void Matches_is_true_only_when_the_place_advertises_that_slot()
    {
        var slots = TimeSlots.Morning | TimeSlots.Evening;

        TimeSlotSet.Matches(slots, new TimeOnly(9, 0)).Should().BeTrue();
        TimeSlotSet.Matches(slots, new TimeOnly(20, 0)).Should().BeTrue();
        TimeSlotSet.Matches(slots, new TimeOnly(12, 0)).Should().BeFalse();
        TimeSlotSet.Matches(slots, new TimeOnly(15, 0)).Should().BeFalse();
    }

    [Fact]
    public void ToNames_lists_the_set_slots_in_chronological_order()
    {
        TimeSlotSet.ToNames(TimeSlots.Evening | TimeSlots.Morning)
            .Should().Equal("Morning", "Evening");
    }

    [Fact]
    public void ToNames_of_an_empty_mask_is_empty()
    {
        TimeSlotSet.ToNames(TimeSlots.None).Should().BeEmpty();
    }

    [Fact]
    public void ToNames_round_trips_every_combination()
    {
        for (var mask = 1; mask <= 15; mask++)
        {
            var slots = (TimeSlots)mask;
            var names = TimeSlotSet.ToNames(slots);

            var rebuilt = names.Aggregate(
                TimeSlots.None,
                (acc, name) => acc | Enum.Parse<TimeSlots>(name));

            rebuilt.Should().Be(slots);
        }
    }
}
