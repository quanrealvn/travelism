using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

/// <summary>
/// Everything a client needs to rebuild its state in one call (spec §5.8).
/// <para>
/// This is what a reconnecting client fetches. Rather than replaying missed
/// events — which needs a durable log and an ordering guarantee neither side
/// has — it simply asks for the current truth and replaces what it had.
/// </para>
/// </summary>
public sealed class SnapshotService(WeGoDbContext db, ExpenseService expenses)
{
    /// <summary>
    /// Reads the whole trip in a fixed number of queries, regardless of size.
    /// Each collection is one query with its own Include; nothing is loaded
    /// per row, so a trip with 50 places costs the same round trips as one
    /// with 5 (reviewer step 11).
    /// </summary>
    public async Task<SnapshotResponse> GetAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        var members = await db.Members
            .AsNoTracking()
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var places = await db.Places
            .AsNoTracking()
            .Include(p => p.Likes)
            .Where(p => p.TripId == tripId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await db.ItineraryItems
            .AsNoTracking()
            .Include(i => i.Place)
            .Where(i => i.TripId == tripId)
            .OrderBy(i => i.Date)
            .ThenBy(i => i.StartTime == null)
            .ThenBy(i => i.StartTime)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tripExpenses = await expenses.ListAsync(tripId, cancellationToken).ConfigureAwait(false);
        var balance = await expenses.BalanceAsync(tripId, cancellationToken).ConfigureAwait(false);

        return new SnapshotResponse(
            trip.ToResponse(members),
            places.Select(p => p.ToResponse()).ToList(),
            items.Select(i => i.ToResponse()).ToList(),
            tripExpenses.Select(e => e.ToResponse()).ToList(),
            balance.ToResponse());
    }
}
