using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;

namespace WeGo.Api.Tests;

/// <summary>
/// A description and reference links on a place, so the wishlist records why
/// something is on it rather than only where it is.
/// </summary>
public sealed class PlaceDetailTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    private static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        Guid tripId,
        string? description = null,
        object[]? references = null,
        string name = "Thác Dải Yếm") =>
        client.PostAsJsonAsync($"/trips/{tripId}/places", new
        {
            name,
            lat = 20.8333,
            lng = 104.6667,
            category = "Sight",
            timeSlots = new[] { "Morning" },
            estimatedDurationMinutes = 90,
            description,
            references,
        }, ApiClient.Json);

    [Fact]
    public async Task A_place_can_be_created_with_a_description_and_links()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Describer");

        var response = await CreateAsync(
            client,
            trip.Trip.Id,
            description: "Thác đẹp nhất Mộc Châu, đi buổi sáng thì mát.",
            references:
            [
                new { url = "https://vnexpress.net/thac-dai-yem", label = "Bài VnExpress" },
                new { url = "https://www.google.com/maps/place/X", label = (string?)null },
            ]);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var place = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        place!.Description.Should().Contain("Mộc Châu");
        place.References.Should().HaveCount(2);
        place.References[0].DisplayName.Should().Be("Bài VnExpress");
        place.References[1].DisplayName.Should().Be(
            "google.com", "a link with no label falls back to its host");
    }

    [Fact]
    public async Task A_place_without_either_is_still_valid()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Minimal");

        var response = await CreateAsync(client, trip.Trip.Id);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var place = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        place!.Description.Should().BeNull();
        place.References.Should().BeEmpty();
    }

    [Theory]
    // These execute when clicked; a saved link must never be able to run script
    // in another member's session.
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public async Task A_dangerous_or_malformed_link_is_refused(string url)
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: $"Bad{url.Length}");

        var response = await CreateAsync(
            client, trip.Trip.Id, references: [new { url, label = "Click me" }]);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "references");
    }

    [Fact]
    public async Task Nothing_is_saved_when_a_link_is_refused()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "NoPartial");

        await CreateAsync(client, trip.Trip.Id, references: [new { url = "javascript:alert(1)", label = "x" }]);

        var places = await client.GetFromJsonAsync<List<PlaceResponse>>(
            $"/trips/{trip.Trip.Id}/places", ApiClient.Json);

        places!.Should().BeEmpty("a rejected link must not leave a half-created place");
    }

    [Fact]
    public async Task Links_keep_the_order_they_were_given()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Ordered");

        var response = await CreateAsync(client, trip.Trip.Id, references:
        [
            new { url = "https://first.example.com", label = "1" },
            new { url = "https://second.example.com", label = "2" },
            new { url = "https://third.example.com", label = "3" },
        ]);

        var place = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        place!.References.Select(r => r.Label).Should().Equal("1", "2", "3");
    }

    [Fact]
    public async Task A_description_can_be_added_to_an_existing_place()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Editor");
        var place = await client.CreatePlaceAsync(trip.Trip.Id);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{place.Id}",
            """{"description":"Nhớ mang áo mưa"}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json))!
            .Description.Should().Be("Nhớ mang áo mưa");
    }

    [Fact]
    public async Task A_description_can_be_cleared_with_an_explicit_null()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Clearer3");
        var created = await (await CreateAsync(client, trip.Trip.Id, description: "tạm thời"))
            .Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{created!.Id}",
            """{"description":null}""");

        (await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json))!
            .Description.Should().BeNull();
    }

    [Fact]
    public async Task Links_are_replaced_wholesale_when_sent()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Replacer");
        var created = await (await CreateAsync(client, trip.Trip.Id, references:
            [
                new { url = "https://old-a.example.com", label = "A" },
                new { url = "https://old-b.example.com", label = "B" },
            ]))
            .Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{created!.Id}",
            """{"references":[{"url":"https://new.example.com","label":"New"}]}""");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        updated!.References.Should().ContainSingle().Which.Label.Should().Be("New");

        // The replaced rows are gone, not orphaned.
        var rows = await factory.WithDbAsync(db => db.PlaceReferences
            .CountAsync(r => r.PlaceId == created.Id));
        rows.Should().Be(1);
    }

    [Fact]
    public async Task Links_are_left_alone_when_the_field_is_not_mentioned()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Untouched");
        var created = await (await CreateAsync(client, trip.Trip.Id, references:
                [new { url = "https://keep.example.com", label = "Keep" }]))
            .Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{created!.Id}",
            """{"name":"Renamed only"}""");

        var updated = await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);
        updated!.Name.Should().Be("Renamed only");
        updated.References.Should().ContainSingle().Which.Label.Should().Be("Keep");
    }

    [Fact]
    public async Task All_links_can_be_removed_with_an_empty_list()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "Emptier");
        var created = await (await CreateAsync(client, trip.Trip.Id, references:
                [new { url = "https://gone.example.com", label = "Gone" }]))
            .Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        var response = await client.PatchJsonAsync(
            $"/trips/{trip.Trip.Id}/places/{created!.Id}",
            """{"references":[]}""");

        (await response.Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json))!
            .References.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_place_takes_its_links_with_it()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "CascadeLinks");
        var created = await (await CreateAsync(client, trip.Trip.Id, references:
                [new { url = "https://doomed.example.com", label = "Doomed" }]))
            .Content.ReadFromJsonAsync<PlaceResponse>(ApiClient.Json);

        await client.DeleteAsync($"/trips/{trip.Trip.Id}/places/{created!.Id}");

        // The place is soft-deleted, so its links stay attached to it rather
        // than being orphaned — restoring the place would restore its sources.
        var rows = await factory.WithDbAsync(db => db.PlaceReferences
            .CountAsync(r => r.PlaceId == created.Id));
        rows.Should().Be(1);

        var visible = await client.GetFromJsonAsync<List<PlaceResponse>>(
            $"/trips/{trip.Trip.Id}/places", ApiClient.Json);
        visible!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_description_over_the_limit_is_refused()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "TooLong");

        var response = await CreateAsync(client, trip.Trip.Id, description: new string('x', 2001));

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "description");
    }

    [Fact]
    public async Task The_snapshot_carries_descriptions_and_links()
    {
        var client = factory.CreateApiClient();
        var trip = await client.CreateTripAsync(ownerDisplayName: "SnapDetail");
        await CreateAsync(client, trip.Trip.Id, description: "Ghi chú", references:
            [new { url = "https://example.com", label = "Nguồn" }]);

        var snapshot = await client.GetFromJsonAsync<SnapshotResponse>(
            $"/trips/{trip.Trip.Id}/snapshot", ApiClient.Json);

        var place = snapshot!.Places.Should().ContainSingle().Subject;
        place.Description.Should().Be("Ghi chú");
        place.References.Should().ContainSingle().Which.DisplayName.Should().Be("Nguồn");
    }
}
