using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Members");
        builder.HasKey(m => m.Id);

        // NOCASE gives the database a case-insensitive uniqueness backstop for
        // spec §5.7. The authoritative check is OrdinalIgnoreCase in the service
        // (NOCASE only folds ASCII), so this index catches races, not normal input.
        builder.Property(m => m.DisplayName)
            .IsRequired()
            .HasMaxLength(MemberDefaults.DisplayNameMaxLength)
            .UseCollation("NOCASE");

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(m => new { m.TripId, m.DisplayName }).IsUnique();
        builder.HasIndex(m => m.TripId);
    }
}
