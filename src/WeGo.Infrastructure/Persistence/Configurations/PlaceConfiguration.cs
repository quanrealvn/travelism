using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("Places");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(PlaceDefaults.NameMaxLength);
        builder.Property(p => p.OpenHoursText).HasMaxLength(PlaceDefaults.OpenHoursTextMaxLength);
        builder.Property(p => p.SkipReason).HasMaxLength(PlaceDefaults.SkipReasonMaxLength);
        builder.Property(p => p.Description).HasMaxLength(PlaceDefaults.DescriptionMaxLength);

        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        // TimeSlots is a [Flags] enum, so it is stored as its integer bitmask
        // rather than by name — the name of a combined value is not stable.
        builder.Property(p => p.TimeSlots).HasConversion<int>().IsRequired();

        builder.HasIndex(p => new { p.TripId, p.IsDeleted });

        builder.HasMany(p => p.Likes)
            .WithOne()
            .HasForeignKey(l => l.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.References)
            .WithOne()
            .HasForeignKey(r => r.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Likes).UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Navigation(p => p.References).UsePropertyAccessMode(PropertyAccessMode.Property);

        // Spec §6: soft-deleted places are excluded from every read by default.
        // Enforcing it as a model-level filter means a future endpoint cannot
        // leak them by forgetting a Where clause; the one caller that is allowed
        // to see them (?includeDeleted=true) opts in with IgnoreQueryFilters().
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
