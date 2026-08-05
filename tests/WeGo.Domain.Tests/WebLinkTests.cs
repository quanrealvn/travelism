using WeGo.Domain.Common;
using WeGo.Domain.Places;

namespace WeGo.Domain.Tests;

public sealed class WebLinkTests
{
    [Theory]
    [InlineData("https://www.google.com/maps/place/X")]
    [InlineData("http://example.com")]
    [InlineData("https://vnexpress.net/du-lich/moc-chau-123.html")]
    [InlineData("https://example.com/path?q=1#frag")]
    public void A_normal_web_address_is_safe(string url)
    {
        WebLink.IsSafe(url).Should().BeTrue();
    }

    [Theory]
    // These execute when clicked. The allowlist is what stops a saved link
    // running script in another member's session.
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    public void A_dangerous_scheme_is_refused(string url)
    {
        WebLink.IsSafe(url).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    [InlineData("//protocol-relative.example.com")]
    [InlineData("https://")]
    public void Anything_that_is_not_an_absolute_web_address_is_refused(string? url)
    {
        WebLink.IsSafe(url).Should().BeFalse();
    }

    [Fact]
    public void A_url_longer_than_the_limit_is_refused()
    {
        var tooLong = "https://example.com/" + new string('a', WebLink.MaxUrlLength);

        WebLink.IsSafe(tooLong).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://www.google.com/maps", "google.com")]
    [InlineData("https://vnexpress.net/article", "vnexpress.net")]
    // Uri normalises the host to lower case, which is right: hosts are
    // case-insensitive, and a label that echoed the typing would look erratic.
    [InlineData("http://WWW.Example.COM/x", "example.com")]
    public void The_host_stands_in_for_a_missing_label(string url, string expected)
    {
        // Better than showing a 200-character URL in a list.
        WebLink.DisplayNameFor(url).Should().Be(expected);
    }

    [Fact]
    public void An_unparseable_url_falls_back_to_itself_rather_than_throwing()
    {
        WebLink.DisplayNameFor("nonsense").Should().Be("nonsense");
    }
}

public sealed class PlaceReferenceRulesTests
{
    private static (PlaceDraft? Draft, ValidationResult Result) ValidateWith(
        string? description = null,
        IReadOnlyList<ReferenceInput>? references = null) =>
        PlaceRules.Validate(
            "Thác Dải Yếm",
            20.8,
            104.6,
            "Sight",
            ["Morning"],
            90,
            null,
            null,
            description,
            references);

    [Fact]
    public void A_place_needs_neither_a_description_nor_links()
    {
        var (draft, result) = ValidateWith();

        result.IsValid.Should().BeTrue();
        draft!.Description.Should().BeNull();
        draft.References.Should().BeEmpty();
    }

    [Fact]
    public void A_description_is_trimmed_and_kept()
    {
        var (draft, result) = ValidateWith(description: "  Thác đẹp, đi buổi sáng mát  ");

        result.IsValid.Should().BeTrue();
        draft!.Description.Should().Be("Thác đẹp, đi buổi sáng mát");
    }

    [Fact]
    public void A_whitespace_only_description_becomes_null_rather_than_an_error()
    {
        var (draft, result) = ValidateWith(description: "    ");

        result.IsValid.Should().BeTrue();
        draft!.Description.Should().BeNull();
    }

    [Fact]
    public void A_description_over_the_limit_is_refused()
    {
        var (_, result) = ValidateWith(description: new string('x', 2001));

        result.Errors.Should().Contain(e => e.Field == "description");
    }

    [Fact]
    public void Links_are_kept_in_the_order_they_were_given()
    {
        var (draft, result) = ValidateWith(references:
        [
            new ReferenceInput("https://a.example.com", "Bài viết"),
            new ReferenceInput("https://b.example.com", null),
        ]);

        result.IsValid.Should().BeTrue();
        draft!.References.Select(r => r.Url).Should().Equal("https://a.example.com", "https://b.example.com");
        draft.References[0].Label.Should().Be("Bài viết");
        draft.References[1].Label.Should().BeNull();
    }

    [Fact]
    public void An_empty_link_row_is_dropped_rather_than_refused()
    {
        // A blank row in a form is somebody who changed their mind.
        var (draft, result) = ValidateWith(references:
        [
            new ReferenceInput("https://a.example.com", null),
            new ReferenceInput("   ", null),
            new ReferenceInput(null, "orphan label"),
        ]);

        result.IsValid.Should().BeTrue();
        draft!.References.Should().ContainSingle();
    }

    [Fact]
    public void A_duplicate_link_is_dropped()
    {
        var (draft, result) = ValidateWith(references:
        [
            new ReferenceInput("https://a.example.com", "First"),
            new ReferenceInput("https://A.example.com", "Same place"),
        ]);

        result.IsValid.Should().BeTrue();
        draft!.References.Should().ContainSingle("saving one source twice is clutter, not information");
    }

    [Fact]
    public void A_dangerous_link_is_refused_rather_than_silently_dropped()
    {
        // Silently dropping it would leave the member thinking it saved.
        var (draft, result) = ValidateWith(references:
        [
            new ReferenceInput("javascript:alert(1)", "Click me"),
        ]);

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "references");
    }

    [Fact]
    public void More_links_than_the_limit_are_refused()
    {
        var many = Enumerable.Range(0, WebLink.MaxPerPlace + 1)
            .Select(i => new ReferenceInput($"https://example.com/{i}", null))
            .ToList();

        var (draft, result) = ValidateWith(references: many);

        draft.Should().BeNull();
        result.Errors.Should().Contain(e => e.Field == "references");
    }

    [Fact]
    public void Exactly_the_maximum_number_of_links_is_allowed()
    {
        var many = Enumerable.Range(0, WebLink.MaxPerPlace)
            .Select(i => new ReferenceInput($"https://example.com/{i}", null))
            .ToList();

        var (draft, result) = ValidateWith(references: many);

        result.IsValid.Should().BeTrue();
        draft!.References.Should().HaveCount(WebLink.MaxPerPlace);
    }

    [Fact]
    public void A_label_over_the_limit_is_refused()
    {
        var (_, result) = ValidateWith(references:
        [
            new ReferenceInput("https://example.com", new string('x', WebLink.MaxLabelLength + 1)),
        ]);

        result.Errors.Should().Contain(e => e.Field == "references");
    }
}
