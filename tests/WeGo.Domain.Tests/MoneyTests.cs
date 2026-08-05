using WeGo.Domain.Money;

namespace WeGo.Domain.Tests;

/// <summary>
/// Spec §9: "Equal-split rounding property test (∀ amount, memberCount ≤ 10:
/// Σshares == amount, |share_i − share_j| ≤ 1)".
/// </summary>
public sealed class ExpenseSplitTests
{
    private static IReadOnlyList<Guid> Members(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Fact]
    public void An_even_split_gives_everyone_the_same()
    {
        var members = Members(2);

        var shares = ExpenseSplit.Equal(100_000, members[0], members);

        shares.Select(s => s.ShareAmount).Should().Equal(50_000, 50_000);
    }

    [Fact]
    public void The_payer_absorbs_the_odd_dong()
    {
        // Spec §7.6, the worked example: 100,001 VND between two people.
        var members = Members(2);

        var shares = ExpenseSplit.Equal(100_001, members[0], members);

        shares.Single(s => s.MemberId == members[0]).ShareAmount.Should().Be(50_001);
        shares.Single(s => s.MemberId == members[1]).ShareAmount.Should().Be(50_000);
        shares.Sum(s => s.ShareAmount).Should().Be(100_001);
    }

    [Fact]
    public void The_remainder_follows_the_payer_not_the_first_member()
    {
        var members = Members(3);

        var shares = ExpenseSplit.Equal(100, members[2], members);

        shares.Single(s => s.MemberId == members[2]).ShareAmount.Should().Be(34);
        shares.Where(s => s.MemberId != members[2]).Should().OnlyContain(s => s.ShareAmount == 33);
    }

    [Fact]
    public void A_payer_outside_the_split_leaves_the_total_exact()
    {
        // Paying for a group you are not part of is unusual but must still
        // reconcile; the remainder falls to the first member.
        var members = Members(3);
        var outsider = Guid.NewGuid();

        var shares = ExpenseSplit.Equal(100, outsider, members);

        shares.Sum(s => s.ShareAmount).Should().Be(100);
        shares[0].ShareAmount.Should().Be(34);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(10)]
    public void Every_member_count_reconciles_exactly(int memberCount)
    {
        var members = Members(memberCount);

        // A wide sweep of awkward amounts, including primes and values just
        // either side of a clean division.
        long[] amounts =
        [
            1, 2, 3, 7, 9, 10, 11, 99, 100, 101, 999, 1_000, 1_001,
            100_000, 100_001, 100_009, 1_234_567, 9_999_999,
            long.MaxValue / 16,
        ];

        foreach (var amount in amounts)
        {
            var shares = ExpenseSplit.Equal(amount, members[0], members);

            shares.Should().HaveCount(memberCount);
            shares.Sum(s => s.ShareAmount).Should().Be(amount, "Σshares must equal the amount exactly");

            var max = shares.Max(s => s.ShareAmount);
            var min = shares.Min(s => s.ShareAmount);
            (max - min).Should().BeLessThanOrEqualTo(
                1, "no member may be more than one minor unit out of step");
        }
    }

    [Fact]
    public void Nobody_is_given_a_negative_share()
    {
        for (var memberCount = 1; memberCount <= 10; memberCount++)
        {
            var members = Members(memberCount);
            var shares = ExpenseSplit.Equal(1, members[0], members);

            shares.Should().OnlyContain(s => s.ShareAmount >= 0);
            shares.Sum(s => s.ShareAmount).Should().Be(1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_amount_is_rejected(long amount)
    {
        var members = Members(2);

        var act = () => ExpenseSplit.Equal(amount, members[0], members);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Splitting_between_nobody_is_rejected()
    {
        var act = () => ExpenseSplit.Equal(100, Guid.NewGuid(), []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Custom_shares_summing_to_the_amount_are_valid()
    {
        var shares = new[]
        {
            new MemberShare(Guid.NewGuid(), 30_000),
            new MemberShare(Guid.NewGuid(), 70_000),
        };

        ExpenseSplit.IsValidCustom(100_000, shares, out var total).Should().BeTrue();
        total.Should().Be(100_000);
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(100_001)]
    public void Custom_shares_that_miss_by_one_are_rejected(long total)
    {
        var shares = new[] { new MemberShare(Guid.NewGuid(), total) };

        ExpenseSplit.IsValidCustom(100_000, shares, out _).Should().BeFalse();
    }

    [Fact]
    public void A_negative_custom_share_is_rejected_even_if_the_total_matches()
    {
        // Otherwise one member could be given a credit that another silently funds.
        var shares = new[]
        {
            new MemberShare(Guid.NewGuid(), 150_000),
            new MemberShare(Guid.NewGuid(), -50_000),
        };

        ExpenseSplit.IsValidCustom(100_000, shares, out _).Should().BeFalse();
    }

    [Fact]
    public void A_zero_share_is_allowed()
    {
        // Someone not taking part in one expense is normal.
        var shares = new[]
        {
            new MemberShare(Guid.NewGuid(), 100_000),
            new MemberShare(Guid.NewGuid(), 0),
        };

        ExpenseSplit.IsValidCustom(100_000, shares, out _).Should().BeTrue();
    }

    [Fact]
    public void An_empty_share_list_is_rejected()
    {
        ExpenseSplit.IsValidCustom(100_000, [], out _).Should().BeFalse();
    }
}

public sealed class SettlementTests
{
    private static readonly Guid Quan = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Linh = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Minh = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_settled_group_needs_no_transfers()
    {
        var transfers = Settlement.Compute(
        [
            new MemberBalance(Quan, Paid: 100_000, Owed: 100_000),
            new MemberBalance(Linh, Paid: 50_000, Owed: 50_000),
        ]);

        transfers.Should().BeEmpty();
    }

    [Fact]
    public void Two_members_collapse_to_one_line()
    {
        // Spec §5.3 calls this out explicitly.
        var transfers = Settlement.Compute(
        [
            new MemberBalance(Quan, Paid: 100_000, Owed: 50_000),
            new MemberBalance(Linh, Paid: 0, Owed: 50_000),
        ]);

        transfers.Should().ContainSingle();
        transfers[0].Should().Be(new Transfer(Linh, Quan, 50_000));
    }

    [Fact]
    public void The_payer_can_still_owe_money_overall()
    {
        // Reviewer step 5 asks for this case by name: paying for something does
        // not stop you being a net debtor, and the sign must not flip.
        var transfers = Settlement.Compute(
        [
            new MemberBalance(Quan, Paid: 10_000, Owed: 90_000),
            new MemberBalance(Linh, Paid: 170_000, Owed: 90_000),
        ]);

        transfers.Should().ContainSingle();
        transfers[0].FromMemberId.Should().Be(Quan, "Quan paid, but owes far more than they paid");
        transfers[0].ToMemberId.Should().Be(Linh);
        transfers[0].Amount.Should().Be(80_000);
    }

    [Fact]
    public void Every_transfer_amount_is_positive()
    {
        var transfers = Settlement.Compute(
        [
            new MemberBalance(Quan, Paid: 300_000, Owed: 100_000),
            new MemberBalance(Linh, Paid: 0, Owed: 100_000),
            new MemberBalance(Minh, Paid: 0, Owed: 100_000),
        ]);

        transfers.Should().OnlyContain(t => t.Amount > 0);
        transfers.Should().OnlyContain(t => t.FromMemberId != t.ToMemberId);
    }

    [Fact]
    public void The_transfers_settle_everyone_exactly()
    {
        var balances = new[]
        {
            new MemberBalance(Quan, Paid: 500_000, Owed: 100_000),
            new MemberBalance(Linh, Paid: 0, Owed: 250_000),
            new MemberBalance(Minh, Paid: 100_000, Owed: 250_000),
        };

        var transfers = Settlement.Compute(balances);

        // Applying the transfers must bring every net balance to zero.
        var net = balances.ToDictionary(b => b.MemberId, b => b.Net);
        foreach (var transfer in transfers)
        {
            net[transfer.FromMemberId] += transfer.Amount;
            net[transfer.ToMemberId] -= transfer.Amount;
        }

        net.Values.Should().OnlyContain(v => v == 0);
    }

    [Fact]
    public void The_largest_debtor_pays_the_largest_creditor_first()
    {
        var transfers = Settlement.Compute(
        [
            new MemberBalance(Quan, Paid: 0, Owed: 100_000),
            new MemberBalance(Linh, Paid: 0, Owed: 10_000),
            new MemberBalance(Minh, Paid: 110_000, Owed: 0),
        ]);

        transfers[0].FromMemberId.Should().Be(Quan);
        transfers[0].Amount.Should().Be(100_000);
    }

    [Fact]
    public void An_odd_amount_still_settles_to_zero()
    {
        // The rounding remainder from an Equal split must not leave a stray unit.
        var balances = new[]
        {
            new MemberBalance(Quan, Paid: 100_001, Owed: 50_001),
            new MemberBalance(Linh, Paid: 0, Owed: 50_000),
        };

        var transfers = Settlement.Compute(balances);

        transfers.Should().ContainSingle().Which.Amount.Should().Be(50_000);
    }

    [Fact]
    public void The_result_is_stable_for_equal_amounts()
    {
        // Without a tie-break the list would reorder between requests and look
        // like something had changed.
        MemberBalance[] balances =
        [
            new(Quan, Paid: 0, Owed: 50_000),
            new(Linh, Paid: 0, Owed: 50_000),
            new(Minh, Paid: 100_000, Owed: 0),
        ];

        var first = Settlement.Compute(balances);
        var second = Settlement.Compute(balances.Reverse().ToList());

        first.Should().BeEquivalentTo(second, options => options.WithStrictOrdering());
    }

    [Fact]
    public void An_empty_group_settles_to_nothing()
    {
        Settlement.Compute([]).Should().BeEmpty();
    }

    [Fact]
    public void A_lone_member_owing_themselves_needs_no_transfer()
    {
        Settlement.Compute([new MemberBalance(Quan, Paid: 100_000, Owed: 100_000)])
            .Should().BeEmpty();
    }
}
