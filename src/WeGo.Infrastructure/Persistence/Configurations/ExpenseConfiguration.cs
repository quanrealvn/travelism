using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence.Configurations;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).IsRequired().HasMaxLength(ExpenseDefaults.TitleMaxLength);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.SplitType).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(e => new { e.TripId, e.Date });

        builder.HasMany(e => e.Shares)
            .WithOne()
            .HasForeignKey(s => s.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Shares).UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}

public sealed class ExpenseShareConfiguration : IEntityTypeConfiguration<ExpenseShare>
{
    public void Configure(EntityTypeBuilder<ExpenseShare> builder)
    {
        builder.ToTable("ExpenseShares");

        // Composite key: one share per member per expense, enforced by the
        // database rather than by the service remembering to check.
        builder.HasKey(s => new { s.ExpenseId, s.MemberId });

        builder.HasIndex(s => s.MemberId);
    }
}
