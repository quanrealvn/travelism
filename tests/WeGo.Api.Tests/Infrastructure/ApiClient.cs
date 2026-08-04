using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WeGo.Api.Contracts;

namespace WeGo.Api.Tests.Infrastructure;

/// <summary>The parts of a ProblemDetails body the tests assert on (spec §6).</summary>
public sealed record ProblemBody(
    int Status,
    string? Title,
    string? Detail,
    string? Code,
    ProblemFieldError[]? Errors);

public sealed record ProblemFieldError(string Field, string Code, string Message);

/// <summary>
/// Thin helpers so tests read as intent ("create a trip, then attack it")
/// rather than as HTTP plumbing.
/// </summary>
public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<TripSessionResponse> CreateTripAsync(
        this HttpClient client,
        string ownerDisplayName = "Quan",
        string name = "Mộc Châu weekend",
        string destination = "Mộc Châu, Vietnam",
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? timeZoneId = null,
        string? currency = null,
        long? budgetAmount = null)
    {
        var start = startDate ?? new DateOnly(2026, 3, 1);
        var response = await client.PostAsJsonAsync("/trips", new
        {
            name,
            destination,
            startDate = start,
            endDate = endDate ?? start.AddDays(2),
            timeZoneId,
            currency,
            budgetAmount,
            ownerDisplayName,
        }, Json);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TripSessionResponse>(Json))!;
    }

    public static async Task<PlaceResponse> CreatePlaceAsync(
        this HttpClient client,
        Guid tripId,
        string name = "Thác Dải Yếm",
        double lat = 20.8333,
        double lng = 104.6667,
        string category = "Sight",
        string[]? timeSlots = null,
        int estimatedDurationMinutes = 90,
        long? estimatedCost = null,
        string? openHoursText = null)
    {
        var response = await client.PostAsJsonAsync($"/trips/{tripId}/places", new
        {
            name,
            lat,
            lng,
            category,
            timeSlots = timeSlots ?? ["Morning", "Afternoon"],
            estimatedDurationMinutes,
            estimatedCost,
            openHoursText,
        }, Json);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PlaceResponse>(Json))!;
    }

    /// <summary>Sends a raw JSON string, for bodies a typed object could not express.</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, string json) =>
        client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, string json) =>
        client.PatchAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    public static async Task<ProblemBody> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json", "spec §6 mandates RFC 7807 for every error");

        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemBody>(body, Json);

        problem.Should().NotBeNull();
        problem!.Code.Should().NotBeNullOrEmpty("every error carries a stable machine-readable code");
        body.Should().NotContain("   at ", "stack traces must never reach a client");
        return problem;
    }

    /// <summary>Asserts the status code, surfacing the response body when it does not match.</summary>
    public static async Task ShouldBeAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new Xunit.Sdk.XunitException(
            $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} "
            + $"{response.StatusCode} from {response.RequestMessage?.Method} "
            + $"{response.RequestMessage?.RequestUri}.\nBody: {body}");
    }
}
