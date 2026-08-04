using Microsoft.EntityFrameworkCore;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Domain;
using WeGo.Domain.Abstractions;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Members;
using WeGo.Domain.Trips;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

/// <summary>A trip and its roster, for plain reads.</summary>
public sealed record TripWithMembers(Trip Trip, IReadOnlyList<Member> Members);

/// <summary>Result of creating or joining a trip: the trip plus the identity to put in the cookie.</summary>
public sealed record TripWithSession(Trip Trip, Member Member, IReadOnlyList<Member> Members);

public sealed class TripService(WeGoDbContext db, IClock clock, ActivityLogWriter activityLog)
{
    public async Task<Result<TripWithSession>> CreateAsync(
        CreateTripRequest request,
        CancellationToken cancellationToken)
    {
        var (draft, tripValidation) = TripRules.Validate(
            request.Name,
            request.Destination,
            request.StartDate,
            request.EndDate,
            request.TimeZoneId,
            request.Currency,
            request.BudgetAmount);

        var (ownerName, nameValidation) =
            MemberRules.ValidateDisplayName(request.OwnerDisplayName, "ownerDisplayName");

        if (!tripValidation.IsValid || !nameValidation.IsValid)
        {
            var combined = new ValidationResult();
            combined.AddRange(tripValidation.Errors);
            combined.AddRange(nameValidation.Errors);
            return Failure.Validation(combined);
        }

        // Both are non-null once validation passed; the compiler cannot see that.
        var validDraft = draft!;
        var validOwnerName = ownerName!;

        var now = clock.UtcNow;
        var tripId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var owner = new Member
        {
            Id = memberId,
            TripId = tripId,
            DisplayName = validOwnerName,
            Role = MemberRole.Owner,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByMemberId = memberId,
        };

        // Spec §7.11: retry on invite-code collision, then give up rather than
        // looping forever. The unique index is the real arbiter — a pre-check
        // alone loses to a concurrent insert.
        for (var attempt = 1; attempt <= InviteCodeGenerator.MaxGenerationAttempts; attempt++)
        {
            var trip = new Trip
            {
                Id = tripId,
                Name = validDraft.Name,
                Destination = validDraft.Destination,
                StartDate = validDraft.StartDate,
                EndDate = validDraft.EndDate,
                TimeZoneId = validDraft.TimeZoneId,
                Currency = validDraft.Currency,
                BudgetAmount = validDraft.BudgetAmount,
                Status = TripStatus.Planning,
                InviteCode = InviteCodeGenerator.Generate(),
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByMemberId = memberId,
            };

            db.ChangeTracker.Clear();
            db.Trips.Add(trip);
            db.Members.Add(owner);
            activityLog.Add(
                tripId,
                memberId,
                ActivityAction.TripCreated,
                nameof(Trip),
                tripId,
                $"{validOwnerName} created trip “{trip.Name}”.");

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result<TripWithSession>.Ok(new TripWithSession(trip, owner, [owner]));
            }
            catch (DbUpdateException ex)
                when (SqliteErrorDetection.IsUniqueConstraintViolation(ex, "InviteCode")
                      && attempt < InviteCodeGenerator.MaxGenerationAttempts)
            {
                // Collision: fall through and try a fresh code.
            }
        }

        return new Failure(
            StatusCodes.Status500InternalServerError,
            ErrorCodes.InviteCodeGenerationFailed,
            "Could not allocate a unique invite code. Please retry.");
    }

    public async Task<Result<TripWithSession>> JoinAsync(
        JoinTripRequest request,
        CancellationToken cancellationToken)
    {
        var (displayName, nameValidation) = MemberRules.ValidateDisplayName(request.DisplayName);

        var rawCode = StringInput.Normalize(request.InviteCode);
        if (rawCode is null)
        {
            nameValidation.Add("inviteCode", FieldErrorCodes.Required, "'inviteCode' is required.");
        }

        if (!nameValidation.IsValid)
        {
            return Failure.Validation(nameValidation);
        }

        var code = InviteCodeGenerator.Normalize(rawCode!);

        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.InviteCode == code, cancellationToken)
            .ConfigureAwait(false);

        // Spec §5.7: an unknown code answers exactly like a missing trip, so a
        // caller cannot probe which codes exist.
        if (trip is null)
        {
            return Failure.NotFound("No trip matches that invite code.");
        }

        var members = await db.Members
            .Where(m => m.TripId == trip.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (MemberRules.IsTripFull(members.Count))
        {
            return Failure.Conflict(
                ErrorCodes.TripFull,
                $"This trip already has the maximum of {TripDefaults.MaxMembers} members.");
        }

        // Spec §5.7: display names are unique within a trip, case-insensitively.
        // Checked with OrdinalIgnoreCase rather than the database's NOCASE index
        // so that non-ASCII names (e.g. "Quân" vs "QUÂN") are caught too.
        if (MemberRules.IsNameTaken(members, displayName!))
        {
            return Failure.Conflict(
                ErrorCodes.NameTaken,
                $"“{displayName}” is already used on this trip. Pick another name.");
        }

        var now = clock.UtcNow;
        var member = new Member
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            DisplayName = displayName!,
            Role = MemberRole.Editor,
            CreatedAt = now,
            UpdatedAt = now,
        };
        member.UpdatedByMemberId = member.Id;

        db.Members.Add(member);
        activityLog.Add(
            trip.Id,
            member.Id,
            ActivityAction.MemberJoined,
            nameof(Member),
            member.Id,
            $"{member.DisplayName} joined the trip.");

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (SqliteErrorDetection.IsUniqueConstraintViolation(ex, "DisplayName"))
        {
            // Lost a race with a concurrent join using the same name.
            return Failure.Conflict(
                ErrorCodes.NameTaken,
                $"“{displayName}” is already used on this trip. Pick another name.");
        }

        members.Add(member);

        // Spec §4: adding a member never retro-demotes an already Confirmed
        // place. Nothing to do here — that is exactly the absence of a demotion
        // pass, and PlaceStateMachineTests pins the behaviour.
        return Result<TripWithSession>.Ok(new TripWithSession(trip, member, members));
    }

    public async Task<Result<TripWithMembers>> GetAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (trip is null)
        {
            return Failure.NotFound("Trip not found.");
        }

        var members = await db.Members
            .AsNoTracking()
            .Where(m => m.TripId == tripId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<TripWithMembers>.Ok(new TripWithMembers(trip, members));
    }

    public async Task<Result<TripWithMembers>> UpdateAsync(
        Guid tripId,
        Guid actingMemberId,
        UpdateTripRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (trip is null)
        {
            return Failure.NotFound("Trip not found.");
        }

        var (draft, validation) = TripRules.Validate(
            request.Name.Or(trip.Name),
            request.Destination.Or(trip.Destination),
            request.StartDate.IsSet ? request.StartDate.Value : trip.StartDate,
            request.EndDate.IsSet ? request.EndDate.Value : trip.EndDate,
            request.TimeZoneId.Or(trip.TimeZoneId),
            trip.Currency,
            request.BudgetAmount.IsSet ? request.BudgetAmount.Value : trip.BudgetAmount);

        var status = trip.Status;
        if (request.Status.IsSet)
        {
            var parsed = EnumInput.Required<TripStatus>(validation, "status", request.Status.Value);
            if (parsed is not null)
            {
                status = parsed.Value;
            }
        }

        if (!validation.IsValid || draft is null)
        {
            return Failure.Validation(validation);
        }

        // Spec §6: narrowing the dates must not silently orphan scheduled items.
        if (draft.StartDate != trip.StartDate || draft.EndDate != trip.EndDate)
        {
            var items = await db.ItineraryItems
                .AsNoTracking()
                .Where(i => i.TripId == tripId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var orphaned = TripRules.FindItemsOutsideRange(items, draft.StartDate, draft.EndDate);
            if (orphaned.Count > 0)
            {
                return Failure.Conflict(
                    ErrorCodes.ItemsOutOfRange,
                    $"{orphaned.Count} itinerary item(s) fall outside the new date range. "
                        + "Move or delete them first.",
                    new Dictionary<string, object?> { ["itemIds"] = orphaned });
            }
        }

        trip.Name = draft.Name;
        trip.Destination = draft.Destination;
        trip.StartDate = draft.StartDate;
        trip.EndDate = draft.EndDate;
        trip.TimeZoneId = draft.TimeZoneId;
        trip.BudgetAmount = draft.BudgetAmount;
        trip.Status = status;
        trip.UpdatedAt = clock.UtcNow;
        trip.UpdatedByMemberId = actingMemberId;

        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.TripUpdated,
            nameof(Trip),
            tripId,
            $"Trip “{trip.Name}” was updated.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var members = await db.Members
            .AsNoTracking()
            .Where(m => m.TripId == tripId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<TripWithMembers>.Ok(new TripWithMembers(trip, members));
    }
}
