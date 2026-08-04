using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class PlaceLikeConfiguration : IEntityTypeConfiguration<PlaceLike>
{
    public void Configure(EntityTypeBuilder<PlaceLike> builder)
    {
        builder.ToTable("PlaceLikes");

        // Composite key: liking twice cannot create a second row, so idempotency
        // (spec §4) is a database invariant rather than a race the app must win.
        builder.HasKey(l => new { l.PlaceId, l.MemberId });

        builder.HasIndex(l => l.MemberId);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(l => l.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
