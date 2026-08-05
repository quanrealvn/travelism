namespace WeGo.Domain.Money;

/// <summary>What one member owes for one expense, in minor units.</summary>
public sealed record MemberShare(Guid MemberId, long ShareAmount);

/// <summary>
/// Spec §5.3. Splitting money, in integer minor units throughout.
/// <para>
/// There is no floating point anywhere here, and that is the whole point:
/// 100,001 ₫ split two ways is 50,000 and 50,001, never 50,000.5 twice.
/// </para>
/// </summary>
public static class ExpenseSplit
{
    /// <summary>
    /// Splits an amount evenly, handing the rounding remainder out one minor
    /// unit at a time, starting with the payer.
    /// <para>
    /// Spec §5.3 says the remainder goes "(±1 per unit) to the payer's share",
    /// while §9 requires that no two shares differ by more than 1. For three or
    /// more members those cannot both hold if the payer takes the whole
    /// remainder: 101 ₫ three ways would give the payer 35 and the others 33.
    /// </para>
    /// <para>
    /// Distributing per unit satisfies both, and matches §5.3's own "±1 per
    /// unit" wording. The payer is first in line, so for the two-member case
    /// this trip actually has, the behaviour is exactly §7.6's worked example:
    /// 100,001 ₫ becomes 50,001 for the payer and 50,000 for the other. See
    /// DECISIONS.md.
    /// </para>
    /// </summary>
    /// <returns>Shares in the order the members were given.</returns>
    public static IReadOnlyList<MemberShare> Equal(long amount, Guid payerId, IReadOnlyList<Guid> memberIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (memberIds.Count == 0)
        {
            throw new ArgumentException("An expense needs at least one member to split between.", nameof(memberIds));
        }

        var baseShare = amount / memberIds.Count;
        var remainder = (int)(amount - (baseShare * memberIds.Count));

        // The payer may not be in the list (they can pay for a group they are
        // not splitting with); distribution then starts at the first member, so
        // the total still reconciles exactly.
        var start = memberIds.ToList().IndexOf(payerId);
        if (start < 0)
        {
            start = 0;
        }

        var amounts = new long[memberIds.Count];
        Array.Fill(amounts, baseShare);

        // The remainder is always smaller than the member count, so nobody is
        // handed a second extra unit and the spread stays within one.
        for (var i = 0; i < remainder; i++)
        {
            amounts[(start + i) % memberIds.Count] += 1;
        }

        return memberIds
            .Select((memberId, index) => new MemberShare(memberId, amounts[index]))
            .ToList();
    }

    /// <summary>
    /// Whether client-supplied shares are usable: they must sum to exactly the
    /// amount, and none may be negative.
    /// </summary>
    public static bool IsValidCustom(long amount, IReadOnlyList<MemberShare> shares, out long total)
    {
        total = 0;
        foreach (var share in shares)
        {
            if (share.ShareAmount < 0)
            {
                return false;
            }

            // Integer addition; the amounts are bounded by validation long
            // before they could approach overflow.
            total += share.ShareAmount;
        }

        return shares.Count > 0 && total == amount;
    }
}
