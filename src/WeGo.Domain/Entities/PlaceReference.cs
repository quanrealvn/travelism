namespace WeGo.Domain.Entities;

/// <summary>
/// A link saved against a place — the blog post, the Google Maps page, the
/// Facebook listing that explains why it is on the list.
/// </summary>
public sealed class PlaceReference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PlaceId { get; set; }

    public required string Url { get; set; }

    /// <summary>Optional; the host stands in when absent.</summary>
    public string? Label { get; set; }

    /// <summary>Preserves the order they were entered in.</summary>
    public int SortOrder { get; set; }
}
