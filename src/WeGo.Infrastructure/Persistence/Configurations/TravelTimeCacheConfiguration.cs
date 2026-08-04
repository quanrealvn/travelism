using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class TravelTimeCacheConfiguration : IEntityTypeConfiguration<TravelTimeCache>
{
    public void Configure(EntityTypeBuilder<TravelTimeCache> builder)
    {
        builder.ToTable("TravelTimeCaches");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Mode).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(16).IsRequired();

        // Spec §3: unique on (From, To, Mode).
        builder.HasIndex(c => new { c.FromPlaceId, c.ToPlaceId, c.Mode }).IsUnique();

        // Spec §7.13: deleting a place must take its cache rows with it, in both
        // directions. Indexed separately so the delete is not a table scan.
        builder.HasIndex(c => c.FromPlaceId);
        builder.HasIndex(c => c.ToPlaceId);
        builder.HasIndex(c => c.TripId);
    }
}
