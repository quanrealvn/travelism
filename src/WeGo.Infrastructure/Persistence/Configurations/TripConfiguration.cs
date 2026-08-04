using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trips");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(TripDefaults.NameMaxLength);
        builder.Property(t => t.Destination).IsRequired().HasMaxLength(TripDefaults.DestinationMaxLength);
        builder.Property(t => t.TimeZoneId).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Currency).IsRequired().HasMaxLength(3);

        // Enums are persisted by name, not ordinal: reordering a member in a
        // later milestone must not silently reinterpret existing rows.
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(t => t.InviteCode)
            .IsRequired()
            .HasMaxLength(16)
            .UseCollation("NOCASE");

        builder.HasIndex(t => t.InviteCode).IsUnique();

        builder.HasMany(t => t.Members)
            .WithOne()
            .HasForeignKey(m => m.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Places)
            .WithOne()
            .HasForeignKey(p => p.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Members).UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Navigation(t => t.Places).UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}
