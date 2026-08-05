using Microsoft.EntityFrameworkCore;
using WeGo.Domain.Entities;

namespace WeGo.Infrastructure.Persistence;

public sealed class WeGoDbContext(DbContextOptions<WeGoDbContext> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<PlaceLike> PlaceLikes => Set<PlaceLike>();

    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();

    public DbSet<TravelTimeCache> TravelTimeCaches => Set<TravelTimeCache>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<ExpenseShare> ExpenseShares => Set<ExpenseShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WeGoDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Applied model-wide rather than per property: SQLite cannot sort EF's
        // default DateTimeOffset mapping, so any future timestamp column would
        // otherwise be a query waiting to fail at runtime.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>()
            .HaveMaxLength(33);

        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<NullableUtcDateTimeOffsetConverter>()
            .HaveMaxLength(33);
    }
}
