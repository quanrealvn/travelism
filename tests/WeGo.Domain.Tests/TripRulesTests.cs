using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Trips;

namespace WeGo.Domain.Tests;

public sealed class TripRulesTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    private static (TripDraft? Draft, ValidationResult Result) ValidateWith(
        string? name = "Mộc Châu weekend",
        string? destination = "Mộc Châu, Vietnam",
        DateOnly? start = null,
        DateOnly? end = null,
        string? timeZoneId = "Asia/Bangkok",
        string? currency = "VND",
        long? budget = null) =>
        TripRules.Validate(
            name,
            destination,
            start ?? Start,
            end ?? Start.AddDays(2),
            timeZoneId,
            currency,
            budget);

    [Fact]
    public void Validate_accepts_a_well_formed_trip()
    {
        var (draft, result) = ValidateWith();

        result.IsValid.Should().BeTrue();
        draft.Should().NotBeNull();
        draft!.Name.Should().Be("Mộc Châu weekend");
        draft.Currency.Should().Be("VND");
    }

    [Fact]
    public void Validate_trims_surrounding_whitespace()
    {
        var (draft, result) = ValidateWith(name: "   Sa Pa trip \t ");

        result.IsValid.Should().BeTrue();
        draft!.Name.Should().Be("Sa Pa trip");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Validate_rejects_blank_names(string? name)
    {
        var (draft, result) = ValidateWith(name: name);

        draft.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Field == "name" && e.Code == FieldErrorCodes.Required);
    }

    [Fact]
    public void Validate_accepts_a_name_of_exactly_the_maximum_length()
    {
        var (_, result) = ValidateWith(name: new string('a', TripDefaults.NameMaxLength));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_a_name_one_character_over_the_maximum()
    {
        var (_, result) = ValidateWith(name: new string('a', TripDefaults.NameMaxLength + 1));

        result.Errors.Should().Contain(e => e.Field == "name" && e.Code == FieldErrorCodes.TooLong);
    }

    [Fact]
    public void Validate_rejects_an_end_date_before_the_start_date()
    {
        var (draft, result) = ValidateWith(start: Start, end: Start.AddDays(-1));

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "endDate" && e.Code == FieldErrorCodes.OutOfRange);
    }

    [Fact]
    public void Validate_accepts_a_single_day_trip()
    {
        var (_, result) = ValidateWith(start: Start, end: Start);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_accepts_a_span_of_exactly_sixty_days()
    {
        // Inclusive day count: 1 March + 59 days is the 60th day of the trip.
        var (_, result) = ValidateWith(start: Start, end: Start.AddDays(59));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_a_span_of_sixty_one_days()
    {
        var (_, result) = ValidateWith(start: Start, end: Start.AddDays(60));

        result.Errors.Should().Contain(e => e.Field == "endDate" && e.Code == FieldErrorCodes.OutOfRange);
    }

    [Fact]
    public void Validate_rejects_an_unknown_time_zone()
    {
        var (draft, result) = ValidateWith(timeZoneId: "Mars/Olympus_Mons");

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "timeZoneId" && e.Code == FieldErrorCodes.Invalid);
    }

    [Fact]
    public void Validate_defaults_a_missing_time_zone_and_currency()
    {
        var (draft, result) = ValidateWith(timeZoneId: null, currency: null);

        result.IsValid.Should().BeTrue();
        draft!.TimeZoneId.Should().Be(TripDefaults.TimeZoneId);
        draft.Currency.Should().Be(TripDefaults.Currency);
    }

    [Theory]
    [InlineData("vnd", "VND")]
    [InlineData("usd", "USD")]
    public void Validate_upper_cases_the_currency(string input, string expected)
    {
        var (draft, result) = ValidateWith(currency: input);

        result.IsValid.Should().BeTrue();
        draft!.Currency.Should().Be(expected);
    }

    [Theory]
    [InlineData("VN")]
    [InlineData("VNDD")]
    [InlineData("V1D")]
    public void Validate_rejects_a_malformed_currency(string currency)
    {
        var (_, result) = ValidateWith(currency: currency);

        result.Errors.Should().Contain(e => e.Field == "currency" && e.Code == FieldErrorCodes.Invalid);
    }

    [Fact]
    public void Validate_rejects_a_negative_budget()
    {
        var (_, result) = ValidateWith(budget: -1);

        result.Errors.Should().Contain(e => e.Field == "budgetAmount");
    }

    [Fact]
    public void Validate_accepts_a_zero_budget()
    {
        var (_, result) = ValidateWith(budget: 0);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_reports_every_bad_field_at_once()
    {
        var (_, result) = ValidateWith(name: " ", destination: " ", currency: "XX");

        result.Errors.Select(e => e.Field)
            .Should().BeEquivalentTo(["name", "destination", "currency"]);
    }

    [Fact]
    public void TopLevelCode_is_the_generic_validation_code_for_ordinary_failures()
    {
        var (_, result) = ValidateWith(name: null);

        result.TopLevelCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void FindItemsOutsideRange_returns_only_items_beyond_the_new_bounds()
    {
        var inside = Item(Start.AddDays(1));
        var before = Item(Start.AddDays(-1));
        var after = Item(Start.AddDays(9));

        var orphans = TripRules.FindItemsOutsideRange(
            [inside, before, after],
            Start,
            Start.AddDays(2));

        orphans.Should().BeEquivalentTo([before.Id, after.Id]);
    }

    [Fact]
    public void FindItemsOutsideRange_treats_the_range_bounds_as_inclusive()
    {
        var onStart = Item(Start);
        var onEnd = Item(Start.AddDays(2));

        var orphans = TripRules.FindItemsOutsideRange([onStart, onEnd], Start, Start.AddDays(2));

        orphans.Should().BeEmpty();
    }

    private static ItineraryItem Item(DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        TripId = Guid.NewGuid(),
        PlaceId = Guid.NewGuid(),
        Date = date,
    };
}
