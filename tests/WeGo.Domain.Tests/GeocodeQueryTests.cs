using WeGo.Domain.Common;
using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

public sealed class GeocodeQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_a_blank_query(string? query)
    {
        var (valid, result) = GeocodeQuery.Validate(query);

        valid.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Field == "q" && e.Code == FieldErrorCodes.Required);
    }

    [Fact]
    public void Validate_rejects_a_single_character_query()
    {
        // One character matches nearly everything; the upstream lookup would be
        // expensive and the results useless.
        var (valid, result) = GeocodeQuery.Validate("a");

        valid.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.TooShort);
    }

    [Fact]
    public void Validate_accepts_a_query_at_the_minimum_length()
    {
        var (valid, result) = GeocodeQuery.Validate("hồ");

        result.IsValid.Should().BeTrue();
        valid.Should().Be("hồ");
    }

    [Fact]
    public void Validate_measures_length_after_trimming()
    {
        var (valid, _) = GeocodeQuery.Validate("        a       ");

        valid.Should().BeNull("padding does not make a one-character query longer");
    }

    [Fact]
    public void Validate_trims_the_accepted_query()
    {
        var (valid, _) = GeocodeQuery.Validate("  Thác Dải Yếm  ");

        valid.Should().Be("Thác Dải Yếm");
    }

    [Fact]
    public void Validate_rejects_a_query_over_the_maximum_length()
    {
        var (valid, result) = GeocodeQuery.Validate(new string('x', GeocodeQuery.MaxLength + 1));

        valid.Should().BeNull();
        result.Errors.Should().ContainSingle(e => e.Code == FieldErrorCodes.TooLong);
    }

    [Theory]
    [InlineData(null, GeocodeQuery.DefaultLimit)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(GeocodeQuery.MaxLimit, GeocodeQuery.MaxLimit)]
    [InlineData(1000, GeocodeQuery.MaxLimit)]
    public void ClampLimit_keeps_the_result_count_in_range(int? requested, int expected)
    {
        GeocodeQuery.ClampLimit(requested).Should().Be(expected);
    }
}
