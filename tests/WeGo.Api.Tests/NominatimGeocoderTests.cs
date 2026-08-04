using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WeGo.Infrastructure.Geocoding;

namespace WeGo.Api.Tests;

/// <summary>
/// Exercises the real Nominatim client against a captured HTTP pipeline — the
/// upstream service is never contacted. These pin the request shape, which is
/// where a Vietnamese place name is most easily corrupted.
/// </summary>
public sealed class NominatimGeocoderTests
{
    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastUserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (NominatimGeocoder Geocoder, CapturingHandler Handler) Build(
        string body = "[]",
        HttpStatusCode status = HttpStatusCode.OK,
        NominatimOptions? options = null)
    {
        var resolved = options ?? new NominatimOptions { MinIntervalMs = 0 };
        var handler = new CapturingHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri(resolved.BaseAddress) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(resolved.UserAgent);

        var geocoder = new NominatimGeocoder(
            client,
            new MemoryCache(new MemoryCacheOptions()),
            resolved,
            NullLogger<NominatimGeocoder>.Instance);

        return (geocoder, handler);
    }

    [Theory]
    // Nominatim resolves all of these correctly when they arrive as UTF-8, so
    // the client's only job is not to corrupt them on the way out. Verified
    // against the live service: each returns the right place in Mộc Châu.
    [InlineData("Thác Dải Yếm")]
    [InlineData("Đồi Chè Trái Tim")]
    [InlineData("Rừng thông bản Áng")]
    [InlineData("Quán ăn ngon 66")]
    public async Task A_vietnamese_query_reaches_the_wire_exactly_as_typed(string typed)
    {
        var (geocoder, handler) = Build();

        await geocoder.SearchAsync(typed, 5, null, CancellationToken.None);

        // Decoding what actually went out must give back what was asked for.
        // Mangling here is silent and expensive: a corrupted query returns
        // plausible results for somewhere else entirely.
        var parameters = System.Web.HttpUtility.ParseQueryString(handler.LastRequestUri!.Query);
        parameters["q"].Should().Be(typed);
    }

    [Fact]
    public async Task Accented_characters_are_utf8_percent_encoded_exactly_once()
    {
        var (geocoder, handler) = Build();

        await geocoder.SearchAsync("Đồi Chè", 5, null, CancellationToken.None);

        var query = handler.LastRequestUri!.Query;

        // "Đ" is U+0110 -> UTF-8 C4 90. A second escaping pass would render it
        // %25C4%2590, which Nominatim reads as the literal text "%C4%90".
        query.Should().Contain("%C4%90");
        query.Should().NotContain("%25");
    }

    [Fact]
    public async Task The_request_identifies_the_application()
    {
        var (geocoder, handler) = Build();

        await geocoder.SearchAsync("thac", 5, null, CancellationToken.None);

        // Nominatim rejects anonymous callers outright.
        handler.LastUserAgent.Should().NotBeNullOrWhiteSpace();
        handler.LastUserAgent.Should().Contain("WeGo");
    }

    [Fact]
    public async Task A_bias_point_becomes_a_non_excluding_viewbox()
    {
        var (geocoder, handler) = Build();

        await geocoder.SearchAsync("thac", 5, (20.5, 104.5), CancellationToken.None);

        var parameters = System.Web.HttpUtility.ParseQueryString(handler.LastRequestUri!.Query);
        parameters["viewbox"].Should().Be("104,21,105,20");
        parameters["bounded"].Should().Be("0", "the box ranks nearby hits higher, it must not exclude far ones");
    }

    [Fact]
    public async Task Results_are_mapped_from_the_upstream_payload()
    {
        const string Body = """
            [{"name":"Thác Dải Yếm","display_name":"Thác Dải Yếm, Mộc Châu, Việt Nam",
              "lat":"20.817975","lon":"104.591686","type":"waterfall"}]
            """;
        var (geocoder, _) = Build(Body);

        var results = await geocoder.SearchAsync("thac", 5, null, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Thác Dải Yếm");
        results[0].Lat.Should().Be(20.817975);
        results[0].Lng.Should().Be(104.591686);
        results[0].Kind.Should().Be("waterfall");
    }

    [Fact]
    public async Task A_row_with_unparseable_coordinates_is_dropped_not_guessed()
    {
        const string Body = """
            [{"name":"Broken","display_name":"Broken","lat":"not-a-number","lon":"104.5"},
             {"name":"Good","display_name":"Good","lat":"20.5","lon":"104.5"}]
            """;
        var (geocoder, _) = Build(Body);

        var results = await geocoder.SearchAsync("thac", 5, null, CancellationToken.None);

        results.Should().ContainSingle().Which.Name.Should().Be("Good");
    }

    [Fact]
    public async Task A_missing_name_falls_back_to_the_first_part_of_the_address()
    {
        const string Body = """
            [{"display_name":"Quán Cơm Bình Dân, Mộc Châu, Việt Nam","lat":"20.5","lon":"104.5"}]
            """;
        var (geocoder, _) = Build(Body);

        var results = await geocoder.SearchAsync("quan", 5, null, CancellationToken.None);

        results.Should().ContainSingle().Which.Name.Should().Be("Quán Cơm Bình Dân");
    }

    [Fact]
    public async Task An_upstream_error_status_becomes_a_typed_unavailable_failure()
    {
        var (geocoder, _) = Build(status: HttpStatusCode.InternalServerError, body: "boom");

        var act = async () => await geocoder.SearchAsync("thac", 5, null, CancellationToken.None);

        await act.Should().ThrowAsync<GeocodingUnavailableException>();
    }

    [Fact]
    public async Task An_identical_query_is_served_from_cache_without_a_second_request()
    {
        const string Body = """[{"name":"X","display_name":"X","lat":"20.5","lon":"104.5"}]""";
        var (geocoder, handler) = Build(Body);

        await geocoder.SearchAsync("thac", 5, null, CancellationToken.None);
        var firstUri = handler.LastRequestUri;

        await geocoder.SearchAsync("thac", 5, null, CancellationToken.None);

        handler.LastRequestUri.Should().BeSameAs(firstUri, "the second call must not hit the network");
    }
}
