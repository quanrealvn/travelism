namespace WeGo.Domain.Money;

/// <summary>What one member paid and what they owed, in minor units.</summary>
public sealed record MemberBalance(Guid MemberId, long Paid, long Owed)
{
    /// <summary>Positive: the trip owes them. Negative: they owe the trip.</summary>
    public long Net => Paid - Owed;
}

/// <summary>One payment that would settle part of the debt.</summary>
public sealed record Transfer(Guid FromMemberId, Guid ToMemberId, long Amount);

/// <summary>
/// Spec §5.3: who should pay whom, in as few transfers as the greedy rule gives.
/// Integer minor units throughout.
/// </summary>
public static class Settlement
{
    /// <summary>
    /// Repeatedly has the largest debtor pay the largest creditor.
    /// <para>
    /// Each step settles at least one person completely, so the loop always
    /// shrinks and terminates. For two members it collapses to a single line,
    /// which is the case this trip actually has.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Transfer> Compute(IReadOnlyList<MemberBalance> balances)
    {
        // Ordered by id within each side so equal amounts resolve the same way
        // every time; an unstable settlement list would look like activity.
        var debtors = balances
            .Where(b => b.Net < 0)
            .Select(b => (b.MemberId, Amount: -b.Net))
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.MemberId)
            .ToList();

        var creditors = balances
            .Where(b => b.Net > 0)
            .Select(b => (b.MemberId, Amount: b.Net))
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.MemberId)
            .ToList();

        var transfers = new List<Transfer>();
        var debtorIndex = 0;
        var creditorIndex = 0;

        while (debtorIndex < debtors.Count && creditorIndex < creditors.Count)
        {
            var debtor = debtors[debtorIndex];
            var creditor = creditors[creditorIndex];

            var amount = Math.Min(debtor.Amount, creditor.Amount);
            if (amount > 0)
            {
                transfers.Add(new Transfer(debtor.MemberId, creditor.MemberId, amount));
            }

            debtors[debtorIndex] = (debtor.MemberId, debtor.Amount - amount);
            creditors[creditorIndex] = (creditor.MemberId, creditor.Amount - amount);

            if (debtors[debtorIndex].Amount == 0)
            {
                debtorIndex++;
            }

            if (creditors[creditorIndex].Amount == 0)
            {
                creditorIndex++;
            }
        }

        return transfers;
    }
}
