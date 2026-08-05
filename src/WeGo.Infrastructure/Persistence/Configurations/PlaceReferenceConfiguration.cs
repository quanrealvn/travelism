using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;
using WeGo.Domain.Places;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class PlaceReferenceConfiguration : IEntityTypeConfiguration<PlaceReference>
{
    public void Configure(EntityTypeBuilder<PlaceReference> builder)
    {
        builder.ToTable("PlaceReferences");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Url).IsRequired().HasMaxLength(WebLink.MaxUrlLength);
        builder.Property(r => r.Label).HasMaxLength(WebLink.MaxLabelLength);

        builder.HasIndex(r => new { r.PlaceId, r.SortOrder });
    }
}
