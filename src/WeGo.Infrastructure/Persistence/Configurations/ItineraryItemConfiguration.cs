using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class ItineraryItemConfiguration : IEntityTypeConfiguration<ItineraryItem>
{
    public void Configure(EntityTypeBuilder<ItineraryItem> builder)
    {
        builder.ToTable("ItineraryItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Note).HasMaxLength(ItineraryItemDefaults.NoteMaxLength);

        builder.HasOne(i => i.Place)
            .WithMany()
            .HasForeignKey(i => i.PlaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.TripId, i.Date });

        // Spec §6: a place may recur across the trip but at most once per date.
        builder.HasIndex(i => new { i.TripId, i.PlaceId, i.Date }).IsUnique();

        // Mirrors the Place filter. Place is the required end of this relationship,
        // so without a matching filter EF would happily return an item whose Place
        // navigation silently came back null. Force-delete already removes these
        // rows (spec §5.6), which makes this a belt-and-braces guarantee rather
        // than a load-bearing filter.
        builder.HasQueryFilter(i => i.Place!.IsDeleted == false);
    }
}
