using Microsoft.EntityFrameworkCore;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Domain.Abstractions;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Money;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

/// <summary>An expense and the settlement view over a whole trip.</summary>
public sealed record BalanceView(
    IReadOnlyList<MemberBalance> Balances,
    IReadOnlyList<Transfer> Transfers,
    long TotalSpent,
    string Currency,
    int CurrencyExponent);

public sealed class ExpenseService(WeGoDbContext db, IClock clock, ActivityLogWriter activityLog)
{
    public async Task<IReadOnlyList<Expense>> ListAsync(Guid tripId, CancellationToken cancellationToken) =>
        await db.Expenses
            .AsNoTracking()
            .Include(e => e.Shares)
            .Where(e => e.TripId == tripId)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Result<Expense>> CreateAsync(
        Guid tripId,
        Guid actingMemberId,
        CreateExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        var memberIds = await db.Members
            .AsNoTracking()
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var validation = new ValidationResult();

        var title = StringInput.Required(
            validation, "title", request.Title, 1, ExpenseDefaults.TitleMaxLength);

        if (request.Amount is null)
        {
            validation.Add("amount", FieldErrorCodes.Required, "'amount' is required.");
        }
        else if (request.Amount <= 0)
        {
            validation.Add("amount", FieldErrorCodes.OutOfRange, "'amount' must be greater than zero.");
        }

        if (request.Date is null)
        {
            validation.Add("date", FieldErrorCodes.Required, "'date' is required (format YYYY-MM-DD).");
        }

        var category = EnumInput.Required<ExpenseCategory>(validation, "category", request.Category);
        var splitType = EnumInput.Required<SplitType>(validation, "splitType", request.SplitType);

        /*
         * Who the expense is actually split between.
         *
         * Not everyone on the trip: on a real trip somebody drives four people
         * to one place and two of them skip the next, and dividing every bill by
         * the whole group quietly charges people for things they were not at.
         *
         * Null means everyone, which is what the field's absence has always
         * meant, so nothing that already works changes.
         */
        var participants = request.Participants is null
            ? memberIds
            : request.Participants.Distinct().ToList();

        if (request.Participants is not null)
        {
            if (participants.Count == 0)
            {
                validation.Add(
                    "participants",
                    FieldErrorCodes.Invalid,
                    "An expense has to be split between at least one person.");
            }

            var strangers = participants.Where(id => !memberIds.Contains(id)).ToList();
            if (strangers.Count > 0)
            {
                validation.Add(
                    "participants",
                    FieldErrorCodes.Invalid,
                    "Everyone sharing an expense has to belong to this trip.");
            }
        }

        // Spec §5.3: the payer must belong to the trip. Checked before anything
        // else touches it, so a foreign id can never reach the shares.
        if (request.PaidByMemberId is null)
        {
            validation.Add("paidByMemberId", FieldErrorCodes.Required, "'paidByMemberId' is required.");
        }
        else if (!memberIds.Contains(request.PaidByMemberId.Value))
        {
            validation.Add(
                "paidByMemberId", FieldErrorCodes.Invalid, "That member does not belong to this trip.");
        }

        // Spec §5.3: v1 keeps every expense in the trip currency, so a balance
        // is a single number rather than a conversion problem.
        var currency = StringInput.Normalize(request.Currency) ?? trip.Currency;
        if (!string.Equals(currency, trip.Currency, StringComparison.OrdinalIgnoreCase))
        {
            validation.Add(
                "currency",
                FieldErrorCodes.Invalid,
                $"Expenses must be in the trip currency ({trip.Currency}) in v1.");
        }

        if (!validation.IsValid || title is null || category is null || splitType is null)
        {
            return Failure.Validation(validation);
        }

        var amount = request.Amount!.Value;
        var payerId = request.PaidByMemberId!.Value;
        var date = request.Date!.Value;

        var shares = splitType.Value == SplitType.Equal
            ? ExpenseSplit.Equal(amount, payerId, participants)
            : ReadCustomShares(request.Shares);

        if (splitType.Value == SplitType.Custom)
        {
            var customFailure = ValidateCustomShares(amount, shares, memberIds);
            if (customFailure is not null)
            {
                return customFailure;
            }
        }

        var now = clock.UtcNow;
        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            Title = title,
            Amount = amount,
            Currency = trip.Currency,
            PaidByMemberId = payerId,
            Date = date,
            Category = category.Value,
            SplitType = splitType.Value,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByMemberId = actingMemberId,
        };

        foreach (var share in shares)
        {
            expense.Shares.Add(new ExpenseShare
            {
                ExpenseId = expense.Id,
                MemberId = share.MemberId,
                ShareAmount = share.ShareAmount,
            });
        }

        db.Expenses.Add(expense);
        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.ExpenseCreated,
            nameof(Expense),
            expense.Id,
            $"đã thêm khoản chi “{expense.Title}”.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Expense>.Ok(expense);
    }

    /// <summary>Spec §5.6: expenses are hard-deleted, and logged.</summary>
    public async Task<Result<Expense>> DeleteAsync(
        Guid tripId,
        Guid expenseId,
        Guid actingMemberId,
        CancellationToken cancellationToken)
    {
        var expense = await db.Expenses
            .Include(e => e.Shares)
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (expense is null)
        {
            return Failure.NotFound("Expense not found.");
        }

        db.Expenses.Remove(expense);
        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.ExpenseDeleted,
            nameof(Expense),
            expense.Id,
            $"đã xoá khoản chi “{expense.Title}”.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Expense>.Ok(expense);
    }

    /// <summary>Spec §5.3: per member paid − owed, plus the minimal transfers.</summary>
    public async Task<BalanceView> BalanceAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        var memberIds = await db.Members
            .AsNoTracking()
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Include(e => e.Shares)
            .Where(e => e.TripId == tripId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var paid = memberIds.ToDictionary(id => id, _ => 0L);
        var owed = memberIds.ToDictionary(id => id, _ => 0L);

        foreach (var expense in expenses)
        {
            if (paid.ContainsKey(expense.PaidByMemberId))
            {
                paid[expense.PaidByMemberId] += expense.Amount;
            }

            foreach (var share in expense.Shares)
            {
                if (owed.ContainsKey(share.MemberId))
                {
                    owed[share.MemberId] += share.ShareAmount;
                }
            }
        }

        var balances = memberIds
            .Select(id => new MemberBalance(id, paid[id], owed[id]))
            .ToList();

        return new BalanceView(
            balances,
            Settlement.Compute(balances),
            expenses.Sum(e => e.Amount),
            trip.Currency,
            CurrencyInfo.GetExponent(trip.Currency));
    }

    private static IReadOnlyList<MemberShare> ReadCustomShares(IReadOnlyList<ExpenseShareRequest>? shares) =>
        shares?
            .Where(s => s.MemberId is not null)
            .Select(s => new MemberShare(s.MemberId!.Value, s.ShareAmount ?? 0))
            .ToList()
        ?? [];

    private static Failure? ValidateCustomShares(
        long amount,
        IReadOnlyList<MemberShare> shares,
        IReadOnlyList<Guid> memberIds)
    {
        // Spec §5.3: every share member must belong to the trip. Without this a
        // caller could park part of an expense against somebody else's trip.
        var stranger = shares.FirstOrDefault(s => !memberIds.Contains(s.MemberId));
        if (stranger is not null)
        {
            var validation = new ValidationResult();
            validation.Add("shares", FieldErrorCodes.Invalid, "A share names a member outside this trip.");
            return Failure.Validation(validation);
        }

        if (shares.Select(s => s.MemberId).Distinct().Count() != shares.Count)
        {
            var validation = new ValidationResult();
            validation.Add("shares", FieldErrorCodes.Invalid, "A member appears twice in the shares.");
            return Failure.Validation(validation);
        }

        if (!ExpenseSplit.IsValidCustom(amount, shares, out var total))
        {
            return Failure.Unprocessable(
                ErrorCodes.SharesSumMismatch,
                $"Shares total {total} but the expense is {amount}. They must match exactly.",
                new Dictionary<string, object?> { ["sharesTotal"] = total, ["amount"] = amount });
        }

        return null;
    }
}
