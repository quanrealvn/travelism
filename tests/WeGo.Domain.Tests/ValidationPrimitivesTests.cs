using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Members;
using WeGo.Domain.Money;

namespace WeGo.Domain.Tests;

public sealed class StringInputTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\t\r\n", null)]
    [InlineData(" hi ", "hi")]
    public void Normalize_trims_and_collapses_blank_to_null(string? input, string? expected)
    {
        StringInput.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Required_returns_the_trimmed_value_when_valid()
    {
        var result = new ValidationResult();

        StringInput.Required(result, "name", "  Quan  ", 1, 40).Should().Be("Quan");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Required_measures_length_after_trimming()
    {
        var result = new ValidationResult();

        // Five spaces around a two-character name is not a seven-character name.
        StringInput.Required(result, "name", "     ab     ", 3, 40).Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.TooShort);
    }

    [Fact]
    public void Optional_clears_blank_input_without_recording_an_error()
    {
        var result = new ValidationResult();

        StringInput.Optional(result, "note", "   ", 100).Should().BeNull();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Optional_rejects_input_over_the_maximum()
    {
        var result = new ValidationResult();

        StringInput.Optional(result, "note", new string('x', 101), 100).Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.TooLong);
    }
}

public sealed class EnumInputTests
{
    [Theory]
    [InlineData("Food")]
    [InlineData("food")]
    [InlineData("FOOD")]
    [InlineData("  Food  ")]
    public void Required_parses_case_insensitively_and_trims(string input)
    {
        var result = new ValidationResult();

        EnumInput.Required<PlaceCategory>(result, "category", input).Should().Be(PlaceCategory.Food);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("99")]
    [InlineData("-1")]
    public void Required_rejects_numeric_strings(string input)
    {
        // Enum.TryParse accepts these and would yield an undefined member for
        // values outside the declared range.
        var result = new ValidationResult();

        EnumInput.Required<PlaceCategory>(result, "category", input).Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.Invalid);
    }

    [Fact]
    public void Required_reports_a_missing_value_as_required()
    {
        var result = new ValidationResult();

        EnumInput.Required<PlaceCategory>(result, "category", null).Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.Required);
    }

    [Fact]
    public void Required_lists_the_allowed_values_in_the_message()
    {
        var result = new ValidationResult();

        EnumInput.Required<PlaceCategory>(result, "category", "Museum");

        result.Errors[0].Message.Should().Contain("Food").And.Contain("Sight");
    }

    [Fact]
    public void Optional_treats_a_missing_value_as_no_opinion()
    {
        var result = new ValidationResult();

        EnumInput.Optional<PlaceCategory>(result, "category", null).Should().BeNull();
        result.IsValid.Should().BeTrue();
    }
}

public sealed class CurrencyInfoTests
{
    [Theory]
    [InlineData("VND", 0)]
    [InlineData("vnd", 0)]
    [InlineData("JPY", 0)]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    public void GetExponent_knows_the_zero_decimal_currencies(string currency, int expected)
    {
        CurrencyInfo.GetExponent(currency).Should().Be(expected);
    }

    [Theory]
    [InlineData("VND", true)]
    [InlineData("vnd", true)]
    [InlineData("VN", false)]
    [InlineData("VNDD", false)]
    [InlineData("V1D", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWellFormed_requires_exactly_three_letters(string? currency, bool expected)
    {
        CurrencyInfo.IsWellFormed(currency).Should().Be(expected);
    }
}

public sealed class MemberRulesTests
{
    [Fact]
    public void ValidateDisplayName_accepts_a_name_at_the_maximum_length()
    {
        var (name, result) = MemberRules.ValidateDisplayName(new string('n', 40));

        result.IsValid.Should().BeTrue();
        name.Should().HaveLength(40);
    }

    [Fact]
    public void ValidateDisplayName_rejects_a_name_one_character_too_long()
    {
        var (name, result) = MemberRules.ValidateDisplayName(new string('n', 41));

        name.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.TooLong);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void ValidateDisplayName_rejects_blank_names(string? input)
    {
        var (name, result) = MemberRules.ValidateDisplayName(input);

        name.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.Required);
    }

    [Theory]
    [InlineData("quan", true)]
    [InlineData("QUAN", true)]
    [InlineData("QuAn", true)]
    [InlineData("quan2", false)]
    public void IsNameTaken_compares_case_insensitively(string candidate, bool expected)
    {
        var members = new[] { Member("Quan") };

        MemberRules.IsNameTaken(members, candidate).Should().Be(expected);
    }

    [Fact]
    public void IsNameTaken_folds_case_for_non_ascii_names_too()
    {
        // The database's NOCASE collation only folds ASCII, which is why the
        // authoritative check lives here.
        MemberRules.IsNameTaken([Member("Quân")], "QUÂN").Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void IsTripFull_caps_the_roster_at_ten(int count, bool expected)
    {
        MemberRules.IsTripFull(count).Should().Be(expected);
    }

    private static Member Member(string displayName) => new()
    {
        Id = Guid.NewGuid(),
        TripId = Guid.NewGuid(),
        DisplayName = displayName,
    };
}
