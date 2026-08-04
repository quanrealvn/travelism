using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

public sealed class PlaceLinkTests
{
    [Theory]
    [InlineData("20.8386, 104.6383")]
    [InlineData("20.8386,104.6383")]
    [InlineData("  20.8386 , 104.6383  ")]
    [InlineData("20.8386 104.6383")]
    public void Parse_reads_a_bare_coordinate_pair(string input)
    {
        var parsed = PlaceLink.Parse(input);

        parsed.Should().NotBeNull();
        parsed!.Lat.Should().BeApproximately(20.8386, 0.00001);
        parsed.Lng.Should().BeApproximately(104.6383, 0.00001);
        parsed.Name.Should().BeNull();
    }

    [Fact]
    public void Parse_reads_negative_coordinates()
    {
        var parsed = PlaceLink.Parse("-36.8485, 174.7633");

        parsed!.Lat.Should().BeApproximately(-36.8485, 0.00001);
        parsed.Lng.Should().BeApproximately(174.7633, 0.00001);
    }

    [Fact]
    public void Parse_prefers_the_place_pin_over_the_viewport_centre()
    {
        // In a /maps/place/ link "@…" is where the map happened to be looking
        // and "!3d…!4d…" is the pin itself. They differ here by ~200 m, which is
        // the difference between the right café and the road outside it.
        const string Url =
            "https://www.google.com/maps/place/Th%C3%A1c+D%E1%BA%A3i+Y%E1%BA%BFm/"
            + "@20.8200000,104.5900000,17z/data=!3m1!4b1!4m6!3m5!1s0x0:0x0!8m2!3d20.817975!4d104.591686";

        var parsed = PlaceLink.Parse(Url);

        parsed.Should().NotBeNull();
        parsed!.Lat.Should().BeApproximately(20.817975, 0.000001);
        parsed.Lng.Should().BeApproximately(104.591686, 0.000001);
    }

    [Fact]
    public void Parse_recovers_the_vietnamese_place_name_from_the_path()
    {
        const string Url =
            "https://www.google.com/maps/place/Th%C3%A1c+D%E1%BA%A3i+Y%E1%BA%BFm/"
            + "@20.817975,104.591686,17z/data=!4m6!3m5!8m2!3d20.817975!4d104.591686";

        PlaceLink.Parse(Url)!.Name.Should().Be("Thác Dải Yếm");
    }

    [Fact]
    public void Parse_falls_back_to_the_viewport_when_there_is_no_pin()
    {
        var parsed = PlaceLink.Parse("https://www.google.com/maps/@20.8386,104.6383,15z");

        parsed.Should().NotBeNull();
        parsed!.Lat.Should().BeApproximately(20.8386, 0.00001);
        parsed.Name.Should().BeNull();
    }

    [Theory]
    [InlineData("https://maps.google.com/?q=20.8386,104.6383")]
    [InlineData("https://www.google.com/maps?q=20.8386,104.6383")]
    [InlineData("https://www.google.com/maps/search/?api=1&query=20.8386,104.6383")]
    [InlineData("https://www.google.com/maps?ll=20.8386,104.6383&z=15")]
    [InlineData("https://www.google.com/maps/search/?api=1&query=20.8386%2C104.6383")]
    public void Parse_reads_the_query_parameter_forms(string url)
    {
        var parsed = PlaceLink.Parse(url);

        parsed.Should().NotBeNull();
        parsed!.Lat.Should().BeApproximately(20.8386, 0.00001);
        parsed.Lng.Should().BeApproximately(104.6383, 0.00001);
    }

    [Theory]
    // We render OpenStreetMap, so its own share links should work too.
    [InlineData("https://www.openstreetmap.org/#map=17/20.8386/104.6383")]
    [InlineData("https://www.openstreetmap.org/?mlat=20.8386&mlon=104.6383#map=17/20.8386/104.6383")]
    public void Parse_reads_openstreetmap_links(string url)
    {
        var parsed = PlaceLink.Parse(url);

        parsed.Should().NotBeNull();
        parsed!.Lat.Should().BeApproximately(20.8386, 0.00001);
        parsed.Lng.Should().BeApproximately(104.6383, 0.00001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Thác Dải Yếm")]
    [InlineData("https://www.google.com/maps/place/Somewhere")]
    [InlineData("https://example.com/not-a-map")]
    [InlineData("just some text with numbers 12 and 34")]
    public void Parse_returns_null_when_there_is_no_location(string? input)
    {
        PlaceLink.Parse(input).Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.google.com/maps/@91.0,104.6,15z")]
    [InlineData("https://www.google.com/maps/@20.8,181.0,15z")]
    [InlineData("200.0, 300.0")]
    public void Parse_rejects_out_of_range_coordinates(string input)
    {
        PlaceLink.Parse(input).Should().BeNull();
    }

    [Theory]
    [InlineData("0, 0")]
    [InlineData("https://www.google.com/maps/@0,0,15z")]
    public void Parse_rejects_null_island(string input)
    {
        // A link that carried no location at all decodes to (0,0) far more
        // often than someone means the Gulf of Guinea.
        PlaceLink.Parse(input).Should().BeNull();
    }

    [Fact]
    public void Parse_ignores_a_name_that_is_really_just_coordinates()
    {
        // An unnamed pin renders its own coordinates in the path segment.
        const string Url = "https://www.google.com/maps/place/20.8386,104.6383/@20.8386,104.6383,17z";

        var parsed = PlaceLink.Parse(Url);

        parsed.Should().NotBeNull();
        parsed!.Name.Should().BeNull("coordinates are not a name");
    }

    [Theory]
    [InlineData("https://maps.app.goo.gl/abc123", true)]
    [InlineData("https://goo.gl/maps/abc123", true)]
    [InlineData("http://maps.app.goo.gl/abc123", true)]
    [InlineData("https://MAPS.APP.GOO.GL/abc123", true)]
    public void TryGetExpandableUrl_accepts_the_google_shorteners(string input, bool expected)
    {
        PlaceLink.TryGetExpandableUrl(input, out var url).Should().Be(expected);
        url.Should().NotBeNull();
    }

    [Theory]
    // Every one of these would be a server-side request to somewhere the user
    // chose. Only the two Google shorteners are ever followed.
    [InlineData("http://localhost:5080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://evil.example.com/redirect")]
    [InlineData("https://maps.app.goo.gl.evil.com/abc")]
    [InlineData("https://notgoo.gl/maps/abc")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://maps.app.goo.gl/abc")]
    [InlineData("Thác Dải Yếm")]
    public void TryGetExpandableUrl_refuses_everything_else(string input)
    {
        PlaceLink.TryGetExpandableUrl(input, out var url).Should().BeFalse();
        url.Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.google.com/maps/place/X/@20.8,104.6,17z", true)]
    [InlineData("http://example.com", true)]
    [InlineData("  https://example.com  ", true)]
    [InlineData("Thác Dải Yếm", false)]
    [InlineData("20.8386, 104.6383", false)]
    public void LooksLikeUrl_separates_a_pasted_link_from_a_typed_name(string input, bool expected)
    {
        PlaceLink.LooksLikeUrl(input).Should().Be(expected);
    }
}
