using System.Net;
using System.Net.Http.Json;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

/// <summary>
/// Reviewer step 4: hostile bodies must produce 422/409 with a ProblemDetails
/// <c>code</c> — never a 500, and never a bare framework error shape.
/// </summary>
public sealed class ErrorContractTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    [Fact]
    public async Task Unknown_fields_are_ignored_rather_than_rejected()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", """
            {
              "name": "Trip with extras",
              "destination": "Somewhere",
              "startDate": "2026-03-01",
              "endDate": "2026-03-03",
              "ownerDisplayName": "Quan",
              "totallyUnknownField": 42,
              "nested": { "also": "ignored" }
            }
            """);

        await response.ShouldBeAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Duplicate_keys_take_the_last_value_without_erroring()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", """
            {
              "name": "First name",
              "name": "Second name",
              "destination": "Somewhere",
              "startDate": "2026-03-01",
              "endDate": "2026-03-03",
              "ownerDisplayName": "Dup"
            }
            """);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TripSessionResponse>(ApiClient.Json);
        created!.Trip.Name.Should().Be("Second name");
    }

    [Fact]
    public async Task Missing_required_fields_report_per_field_errors()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", "{}");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.ValidationFailed);
        problem.Errors!.Select(e => e.Field).Should().Contain(
            ["name", "destination", "startDate", "endDate", "ownerDisplayName"]);
        problem.Errors.Should().OnlyContain(e => e.Code == FieldErrorCodes.Required);
    }

    [Fact]
    public async Task An_explicit_null_is_treated_the_same_as_a_missing_required_field()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", """
            {"name":null,"destination":null,"startDate":null,"endDate":null,"ownerDisplayName":null}
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"name\":")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task Malformed_bodies_produce_a_problem_details_rather_than_a_500(string body)
    {
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/trips", body);

        ((int)response.StatusCode).Should().BeInRange(400, 422);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.MalformedJson);
    }

    [Fact]
    public async Task A_wrongly_typed_field_does_not_crash_the_server()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Typer");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", """
            {"name":"X","lat":"not-a-number","lng":105,"category":"Food",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30}
            """);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.MalformedJson);
    }

    [Fact]
    public async Task Null_island_is_rejected_with_its_own_code()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "NullIsland");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", """
            {"name":"Nowhere","lat":0,"lng":0,"category":"Other",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30}
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.SuspiciousCoordinates);
    }

    [Theory]
    [InlineData(-90.1, 105.0)]
    [InlineData(90.1, 105.0)]
    [InlineData(20.0, -180.1)]
    [InlineData(20.0, 180.1)]
    public async Task Out_of_range_coordinates_are_unprocessable(double lat, double lng)
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: $"Coord{lat}{lng}");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", $$"""
            {"name":"Edge","lat":{{lat}},"lng":{{lng}},"category":"Other",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30}
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_money_value_at_the_edge_of_the_long_range_is_handled_without_overflow()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "BigSpender");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", $$"""
            {"name":"Expensive","lat":21,"lng":105,"category":"Other",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30,
             "estimatedCost":{{long.MaxValue}}}
            """);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var place = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        place!.EstimatedCost.Should().Be(long.MaxValue, "money is a long, so this round-trips exactly");
    }

    [Fact]
    public async Task A_money_value_beyond_the_long_range_is_a_parse_error_not_a_crash()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Overflower");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", """
            {"name":"Too much","lat":21,"lng":105,"category":"Other",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30,
             "estimatedCost":99999999999999999999999}
            """);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        await response.ReadProblemAsync();
    }

    [Fact]
    public async Task A_negative_cost_is_unprocessable()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Negative");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", """
            {"name":"Refund","lat":21,"lng":105,"category":"Other",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30,"estimatedCost":-1}
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "estimatedCost");
    }

    [Fact]
    public async Task An_unknown_enum_value_is_unprocessable_not_a_bad_request()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Enumerator");

        var response = await client.PostJsonAsync($"/trips/{trip.Trip.Id}/places", """
            {"name":"Museum","lat":21,"lng":105,"category":"Museum",
             "timeSlots":["Morning"],"estimatedDurationMinutes":30}
            """);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code.Should().Be(ErrorCodes.ValidationFailed);
        problem.Errors!.Should().Contain(e => e.Field == "category");
    }

    [Fact]
    public async Task An_unknown_endpoint_under_the_api_surface_returns_json_not_the_spa_shell()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Wanderer");

        var response = await client.GetAsync($"/trips/{trip.Trip.Id}/does-not-exist");

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_non_guid_trip_id_is_a_not_found_rather_than_a_crash()
    {
        var response = await factory.CreateApiClient().GetAsync("/trips/not-a-guid");

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
    }
}
