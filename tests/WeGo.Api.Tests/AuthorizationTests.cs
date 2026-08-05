using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>
/// Spec §5.7 / reviewer step 3. A member of trip A must not be able to reach
/// trip B by any route, in either the path or the body.
/// </summary>
public sealed class AuthorizationTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    /// <summary>
    /// The complete trip-scoped route table. Written as data rather than as one
    /// test per route so that adding an endpoint without adding it here is
    /// visible: <see cref="Route_table_covers_every_trip_scoped_endpoint"/> fails.
    /// </summary>
    public static TheoryData<string, string> TripScopedRoutes()
    {
        var data = new TheoryData<string, string>();
        foreach (var (method, template) in RouteTable)
        {
            data.Add(method, template);
        }

        return data;
    }

    private static readonly (string Method, string Template)[] RouteTable =
    [
        ("GET", "/trips/{tripId}"),
        ("PATCH", "/trips/{tripId}"),
        ("DELETE", "/trips/{tripId}"),
        ("GET", "/trips/{tripId}/members"),
        ("GET", "/trips/{tripId}/places"),
        ("POST", "/trips/{tripId}/places"),
        ("GET", "/trips/{tripId}/places/search"),
        ("POST", "/trips/{tripId}/places/resolve-link"),
        ("GET", "/trips/{tripId}/places/{placeId}"),
        ("PATCH", "/trips/{tripId}/places/{placeId}"),
        ("DELETE", "/trips/{tripId}/places/{placeId}"),
        ("POST", "/trips/{tripId}/places/{placeId}/like"),
        ("DELETE", "/trips/{tripId}/places/{placeId}/like"),
        ("POST", "/trips/{tripId}/places/{placeId}/status"),
    ];

    [Theory]
    [MemberData(nameof(TripScopedRoutes))]
    public async Task Member_of_another_trip_is_refused_on_every_trip_scoped_route(
        string method,
        string template)
    {
        var attacker = factory.CreateApiClient();
        await attacker.CreateTripAsync(ownerDisplayName: "Attacker", name: "Attacker trip");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "Victim", name: "Victim trip");
        var victimPlace = await victimClient.CreatePlaceAsync(victim.Trip.Id);

        var url = template
            .Replace("{tripId}", victim.Trip.Id.ToString())
            .Replace("{placeId}", victimPlace.Id.ToString());

        var response = await attacker.SendAsync(BuildRequest(method, url));

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Theory]
    [MemberData(nameof(TripScopedRoutes))]
    public async Task Anonymous_caller_is_refused_on_every_trip_scoped_route(string method, string template)
    {
        var ownerClient = factory.CreateApiClient();
        var trip = await ownerClient.CreateTripAsync(ownerDisplayName: "Owner");
        var place = await ownerClient.CreatePlaceAsync(trip.Trip.Id);

        var anonymous = factory.CreateApiClient();
        var url = template
            .Replace("{tripId}", trip.Trip.Id.ToString())
            .Replace("{placeId}", place.Id.ToString());

        var response = await anonymous.SendAsync(BuildRequest(method, url));

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.Unauthenticated);
    }

    [Fact]
    public void Route_table_covers_every_trip_scoped_endpoint()
    {
        // Guards the table above against drift: a new trip-scoped endpoint that
        // is not listed would otherwise be silently untested for IDOR.
        var registered = factory.Services
            .GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("{tripId", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata
                    .GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()
                    ?.HttpMethods ?? [])
                .Select(method => (Method: method, Template: Normalize(e.RoutePattern.RawText!))))
            .Distinct()
            .ToList();

        var covered = RouteTable
            .Select(r => (r.Method, Template: Normalize(r.Template)))
            .ToHashSet();

        registered.Should().OnlyContain(
            r => covered.Contains(r),
            "every trip-scoped route must appear in the IDOR route table");
    }

    private static string Normalize(string template) => template
        .Replace(":guid", string.Empty, StringComparison.Ordinal)
        .TrimEnd('/');

    [Fact]
    public async Task Session_cookie_for_a_deleted_membership_stops_working()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Ghost");

        // The cookie stays valid-looking, but authority is re-derived from the
        // database on every request, so removing the row revokes access at once.
        await factory.WithDbAsync(async db =>
        {
            var member = await db.Members.FindAsync(trip.Session.MemberId);
            db.Members.Remove(member!);
            await db.SaveChangesAsync();
        });

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}");

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tampered_cookie_is_rejected()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync();

        var forged = factory.CreateApiClient();
        // A structurally plausible token for the victim trip, signed with nothing.
        forged.DefaultRequestHeaders.Add(
            "Cookie",
            $"wego_session={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{trip.Trip.Id:N}:{trip.Session.MemberId:N}"))}.AAAA");

        var response = await forged.GetAsync($"/trips/{trip.Trip.Id}");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Place_from_another_trip_is_not_reachable_through_your_own_trip_id()
    {
        // The route is authorised for trip A, but the place id belongs to trip B.
        // Scoping the lookup by trip id is what makes this a 404 and not a leak.
        var attackerClient = factory.CreateApiClient();
        var attacker = await attackerClient.CreateTripAsync(ownerDisplayName: "Attacker");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "Victim");
        var victimPlace = await victimClient.CreatePlaceAsync(victim.Trip.Id, name: "Secret spot");

        var response = await attackerClient.GetAsync($"/trips/{attacker.Trip.Id}/places/{victimPlace.Id}");

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Secret spot");
    }

    [Fact]
    public async Task Patching_a_place_through_the_wrong_trip_id_does_not_modify_it()
    {
        var attackerClient = factory.CreateApiClient();
        var attacker = await attackerClient.CreateTripAsync(ownerDisplayName: "Attacker2");

        var victimClient = factory.CreateApiClient();
        var victim = await victimClient.CreateTripAsync(ownerDisplayName: "Victim2");
        var victimPlace = await victimClient.CreatePlaceAsync(victim.Trip.Id, name: "Untouched");

        var response = await attackerClient.PatchJsonAsync(
            $"/trips/{attacker.Trip.Id}/places/{victimPlace.Id}",
            """{"name":"Vandalised"}""");

        await response.ShouldBeAsync(HttpStatusCode.NotFound);

        var reread = await victimClient.GetFromJsonAsync<WeGo.Api.Contracts.PlaceResponse>(
            $"/trips/{victim.Trip.Id}/places/{victimPlace.Id}", ApiClient.Json);
        reread!.Name.Should().Be("Untouched");
    }

    private static HttpRequestMessage BuildRequest(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (method is "POST" or "PATCH")
        {
            // A body that would be valid if the caller were authorised, so the
            // test proves authorisation fails first rather than validation.
            request.Content = JsonContent.Create(
                new
                {
                    name = "Probe",
                    lat = 21.0,
                    lng = 105.0,
                    category = "Food",
                    timeSlots = new[] { "Morning" },
                    estimatedDurationMinutes = 30,
                },
                options: ApiClient.Json);
        }

        return request;
    }
}
