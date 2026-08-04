using WeGo.Domain.Abstractions;
using WeGo.Domain.Entities;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

/// <summary>
/// Spec §8: every mutation writes an ActivityLog row in the same transaction.
/// This only stages the row on the change tracker — the caller's single
/// SaveChanges commits the mutation and its log entry together or not at all.
/// </summary>
public sealed class ActivityLogWriter(WeGoDbContext db, IClock clock)
{
    public ActivityLog Add(
        Guid tripId,
        Guid memberId,
        ActivityAction action,
        string entityType,
        Guid entityId,
        string summaryText)
    {
        var now = clock.UtcNow;
        var entry = new ActivityLog
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            MemberId = memberId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            SummaryText = Truncate(summaryText, 500),
            At = now,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByMemberId = memberId,
        };

        db.ActivityLogs.Add(entry);
        return entry;
    }

    /// <summary>
    /// Summaries embed user-supplied names, so they are clamped to the column
    /// width here rather than letting a long place name fail the insert.
    /// </summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
