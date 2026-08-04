using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

public sealed class PlaceRulesTests
{
    private static (PlaceDraft? Draft, ValidationResult Result) ValidateWith(
        string? name = "Thác Dải Yếm",
        double? lat = 20.8333,
        double? lng = 104.6667,
        string? category = "Sight",
        string?[]? timeSlots = null,
        int? duration = 90,
        long? cost = null,
        string? openHours = null) =>
        PlaceRules.Validate(
            name,
            lat,
            lng,
            category,
            timeSlots ?? ["Morning", "Afternoon"],
            duration,
            cost,
            openHours);

    [Fact]
    public void Validate_accepts_a_well_formed_place()
    {
        var (draft, result) = ValidateWith();

        result.IsValid.Should().BeTrue();
        draft!.TimeSlots.Should().Be(TimeSlots.Morning | TimeSlots.Afternoon);
        draft.Category.Should().Be(PlaceCategory.Sight);
    }

    [Theory]
    [InlineData(-90.0, 0.1)]
    [InlineData(90.0, 0.1)]
    [InlineData(0.1, -180.0)]
    [InlineData(0.1, 180.0)]
    public void Validate_accepts_coordinates_exactly_on_the_bounds(double lat, double lng)
    {
        var (_, result) = ValidateWith(lat: lat, lng: lng);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-90.0001, 0.1)]
    [InlineData(90.0001, 0.1)]
    [InlineData(0.1, -180.0001)]
    [InlineData(0.1, 180.0001)]
    public void Validate_rejects_coordinates_just_outside_the_bounds(double lat, double lng)
    {
        var (draft, result) = ValidateWith(lat: lat, lng: lng);

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Code == FieldErrorCodes.OutOfRange);
        result.TopLevelCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Validate_rejects_null_island_with_its_own_top_level_code()
    {
        var (draft, result) = ValidateWith(lat: 0, lng: 0);

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Code == FieldErrorCodes.Suspicious);
        result.TopLevelCode.Should().Be(ErrorCodes.SuspiciousCoordinates);
    }

    [Fact]
    public void Validate_allows_a_zero_on_one_axis_only()
    {
        // The equator and the prime meridian are real places; only their
        // intersection is treated as a client bug.
        ValidateWith(lat: 0, lng: 104.5).Result.IsValid.Should().BeTrue();
        ValidateWith(lat: 20.8, lng: 0).Result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_rejects_non_finite_coordinates(double lat)
    {
        var (draft, _) = ValidateWith(lat: lat);

        draft.Should().BeNull();
    }

    [Fact]
    public void Validate_reports_missing_coordinates_per_field()
    {
        var (_, result) = ValidateWith(lat: null, lng: null);

        result.Errors.Should().Contain(e => e.Field == "lat" && e.Code == FieldErrorCodes.Required);
        result.Errors.Should().Contain(e => e.Field == "lng" && e.Code == FieldErrorCodes.Required);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1440)]
    public void Validate_accepts_durations_on_the_bounds(int minutes)
    {
        ValidateWith(duration: minutes).Result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    [InlineData(0)]
    [InlineData(-30)]
    public void Validate_rejects_durations_outside_the_bounds(int minutes)
    {
        var (_, result) = ValidateWith(duration: minutes);

        result.Errors.Should().Contain(
            e => e.Field == "estimatedDurationMinutes" && e.Code == FieldErrorCodes.OutOfRange);
    }

    [Fact]
    public void Validate_rejects_an_empty_time_slot_list()
    {
        var (draft, result) = ValidateWith(timeSlots: []);

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "timeSlots" && e.Code == FieldErrorCodes.Required);
    }

    [Fact]
    public void Validate_rejects_a_null_time_slot_list()
    {
        var (draft, _) = PlaceRules.Validate("x", 1, 1, "Food", null, 30, null, null);

        draft.Should().BeNull();
    }

    [Fact]
    public void Validate_rejects_None_as_a_time_slot()
    {
        // 'None' would satisfy "at least one entry" while matching no time of day.
        var (draft, result) = ValidateWith(timeSlots: ["None"]);

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "timeSlots" && e.Code == FieldErrorCodes.Invalid);
    }

    [Theory]
    [InlineData("Midnight")]
    [InlineData("1")]
    [InlineData("")]
    public void Validate_rejects_unknown_time_slots_and_always_records_a_reason(string slot)
    {
        var (draft, result) = ValidateWith(timeSlots: [slot]);

        draft.Should().BeNull();
        // A null draft with an empty error list would render as a 422 with no
        // explanation, so the pairing is asserted explicitly.
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e => e.Field == "timeSlots");
    }

    [Fact]
    public void Validate_parses_time_slots_case_insensitively_and_deduplicates()
    {
        var (draft, result) = ValidateWith(timeSlots: ["morning", "MORNING", "evening"]);

        result.IsValid.Should().BeTrue();
        draft!.TimeSlots.Should().Be(TimeSlots.Morning | TimeSlots.Evening);
    }

    [Fact]
    public void Validate_rejects_an_unknown_category()
    {
        var (draft, result) = ValidateWith(category: "Museum");

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "category" && e.Code == FieldErrorCodes.Invalid);
    }

    [Fact]
    public void Validate_rejects_a_numeric_category_string()
    {
        // Enum.TryParse would accept "9" and store an undefined member.
        var (draft, _) = ValidateWith(category: "9");

        draft.Should().BeNull();
    }

    [Fact]
    public void Validate_rejects_a_negative_cost()
    {
        var (_, result) = ValidateWith(cost: -1);

        result.Errors.Should().Contain(e => e.Field == "estimatedCost");
    }

    [Fact]
    public void Validate_accepts_the_largest_representable_cost()
    {
        ValidateWith(cost: long.MaxValue).Result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_open_hours_over_the_maximum_length()
    {
        var (_, result) = ValidateWith(openHours: new string('h', PlaceDefaults.OpenHoursTextMaxLength + 1));

        result.Errors.Should().Contain(e => e.Field == "openHoursText" && e.Code == FieldErrorCodes.TooLong);
    }

    [Fact]
    public void Validate_normalises_whitespace_only_open_hours_to_null()
    {
        var (draft, result) = ValidateWith(openHours: "   ");

        result.IsValid.Should().BeTrue();
        draft!.OpenHoursText.Should().BeNull();
    }

    [Fact]
    public void CoordinatesChanged_detects_a_move_on_either_axis()
    {
        var place = new Place { Name = "x", TripId = Guid.NewGuid(), Lat = 20.5, Lng = 104.5 };

        PlaceRules.CoordinatesChanged(place, 20.5, 104.5).Should().BeFalse();
        PlaceRules.CoordinatesChanged(place, 20.5000001, 104.5).Should().BeTrue();
        PlaceRules.CoordinatesChanged(place, 20.5, 104.5000001).Should().BeTrue();
    }
}
