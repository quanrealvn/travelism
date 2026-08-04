using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

public sealed class GeoTests
{
    [Fact]
    public void DistanceKm_between_a_point_and_itself_is_zero()
    {
        Geo.DistanceKm(20.8386, 104.6383, 20.8386, 104.6383).Should().Be(0);
    }

    [Fact]
    public void DistanceKm_is_symmetric()
    {
        var there = Geo.DistanceKm(20.8386, 104.6383, 21.0285, 105.8542);
        var back = Geo.DistanceKm(21.0285, 105.8542, 20.8386, 104.6383);

        there.Should().BeApproximately(back, 0.000001);
    }

    [Fact]
    public void DistanceKm_matches_a_known_separation()
    {
        // Mộc Châu to Hà Nội is about 130 km in a straight line.
        var distance = Geo.DistanceKm(20.8386, 104.6383, 21.0285, 105.8542);

        distance.Should().BeApproximately(128, 5);
    }

    [Fact]
    public void DistanceKm_spans_continents()
    {
        // Mộc Châu to Kaohsiung, Taiwan — the sort of result that looks
        // plausible in a list until you see how far away it is.
        var distance = Geo.DistanceKm(20.8386, 104.6383, 22.6273, 120.3014);

        distance.Should().BeApproximately(1630, 60);
    }

    [Fact]
    public void DistanceKm_handles_a_quarter_of_the_globe()
    {
        // Equator to north pole is a quarter of the circumference, ~10,007 km.
        var distance = Geo.DistanceKm(0, 0, 90, 0);

        distance.Should().BeApproximately(10007, 5);
    }

    [Fact]
    public void DistanceKm_handles_antipodal_points_without_losing_precision()
    {
        // Half the circumference, ~20,015 km. An Asin-based formula can trip
        // over rounding here and return NaN.
        var distance = Geo.DistanceKm(0, 0, 0, 180);

        distance.Should().BeApproximately(20015, 10);
        double.IsNaN(distance).Should().BeFalse();
    }

    [Fact]
    public void DistanceKm_works_across_the_antimeridian()
    {
        // 1 degree apart, but either side of the +/-180 line.
        var distance = Geo.DistanceKm(0, 179.5, 0, -179.5);

        distance.Should().BeApproximately(111, 2);
    }

    [Fact]
    public void DistanceKm_works_in_the_southern_hemisphere()
    {
        // Auckland to Wellington, about 493 km.
        var distance = Geo.DistanceKm(-36.8485, 174.7633, -41.2865, 174.7762);

        distance.Should().BeApproximately(493, 10);
    }
}
