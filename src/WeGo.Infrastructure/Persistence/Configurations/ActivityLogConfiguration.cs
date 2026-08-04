using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("ActivityLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(32);
        builder.Property(a => a.SummaryText).IsRequired().HasMaxLength(500);

        builder.HasIndex(a => new { a.TripId, a.At });
    }
}
