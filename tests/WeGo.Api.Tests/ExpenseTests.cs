using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Contracts;
using WeGo.Api.Tests.Infrastructure;
using WeGo.Domain.Common;

namespace WeGo.Api.Tests;

public sealed class ExpenseTests(WeGoAppFactory factory) : IClassFixture<WeGoAppFactory>
{
    private static readonly DateOnly Day = new(2026, 3, 1);

    /// <summary>A two-person trip, which is what v1 is actually for.</summary>
    private async Task<(HttpClient Owner, HttpClient Joiner, TripResponse Trip, Guid OwnerId, Guid JoinerId)>
        TwoPersonTripAsync(string label)
    {
        var owner = factory.CreateApiClient();
        var created = await owner.CreateTripAsync(
            ownerDisplayName: $"Quan{label}", name: $"Money {label}", startDate: Day, endDate: Day.AddDays(3));

        var joiner = factory.CreateApiClient();
        var joined = await joiner.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = created.Trip.InviteCode,
            displayName = $"Linh{label}",
        }, ApiClient.Json);
        await joined.ShouldBeAsync(HttpStatusCode.OK);

        var joinedSession = await joined.Content.ReadFromJsonAsync<TripSessionResponse>(ApiClient.Json);
        var trip = await owner.GetFromJsonAsync<TripResponse>($"/trips/{created.Trip.Id}", ApiClient.Json);

        return (owner, joiner, trip!, created.Session.MemberId, joinedSession!.Session.MemberId);
    }

    private static Task<HttpResponseMessage> AddExpenseAsync(
        HttpClient client,
        Guid tripId,
        long amount,
        Guid paidBy,
        string splitType = "Equal",
        object[]? shares = null,
        string title = "Xăng xe",
        string? currency = null) =>
        client.PostAsJsonAsync($"/trips/{tripId}/expenses", new
        {
            title,
            amount,
            currency,
            paidByMemberId = paidBy,
            date = Day,
            category = "Transport",
            splitType,
            shares,
        }, ApiClient.Json);

    [Fact]
    public async Task An_equal_split_reconciles_exactly_with_the_payer_taking_the_odd_dong()
    {
        // Spec §7.6's worked example, end to end.
        var (owner, _, trip, ownerId, joinerId) = await TwoPersonTripAsync("A");

        var response = await AddExpenseAsync(owner, trip.Id, 100_001, ownerId);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var expense = await response.Content.ReadFromJsonAsync<ExpenseResponse>(ApiClient.Json);

        expense!.Shares.Sum(s => s.ShareAmount).Should().Be(100_001);
        expense.Shares.Single(s => s.MemberId == ownerId).ShareAmount.Should().Be(50_001);
        expense.Shares.Single(s => s.MemberId == joinerId).ShareAmount.Should().Be(50_000);
    }

    [Fact]
    public async Task The_balance_shows_who_owes_whom_in_one_line()
    {
        var (owner, _, trip, ownerId, joinerId) = await TwoPersonTripAsync("B");
        await AddExpenseAsync(owner, trip.Id, 100_000, ownerId);

        var balance = await owner.GetFromJsonAsync<BalanceResponse>(
            $"/trips/{trip.Id}/balance", ApiClient.Json);

        balance!.TotalSpent.Should().Be(100_000);
        balance.Balances.Single(b => b.MemberId == ownerId).Net.Should().Be(50_000);
        balance.Balances.Single(b => b.MemberId == joinerId).Net.Should().Be(-50_000);

        balance.Transfers.Should().ContainSingle();
        balance.Transfers[0].FromMemberId.Should().Be(joinerId);
        balance.Transfers[0].ToMemberId.Should().Be(ownerId);
        balance.Transfers[0].Amount.Should().Be(50_000);
    }

    [Fact]
    public async Task A_payer_can_still_be_a_net_debtor()
    {
        // Reviewer step 5 asks for this sign check explicitly.
        var (owner, joiner, trip, ownerId, joinerId) = await TwoPersonTripAsync("C");

        await AddExpenseAsync(owner, trip.Id, 20_000, ownerId, title: "Cà phê");
        await AddExpenseAsync(joiner, trip.Id, 300_000, joinerId, title: "Khách sạn");

        var balance = await owner.GetFromJsonAsync<BalanceResponse>(
            $"/trips/{trip.Id}/balance", ApiClient.Json);

        balance!.Balances.Single(b => b.MemberId == ownerId).Net
            .Should().BeNegative("Quan paid something but owes far more");
        balance.Transfers.Should().ContainSingle();
        balance.Transfers[0].FromMemberId.Should().Be(ownerId);
        balance.Transfers[0].Amount.Should().Be(140_000);
    }

    [Fact]
    public async Task The_settlement_brings_every_balance_to_zero()
    {
        var (owner, joiner, trip, ownerId, joinerId) = await TwoPersonTripAsync("D");

        await AddExpenseAsync(owner, trip.Id, 123_457, ownerId, title: "Ăn trưa");
        await AddExpenseAsync(joiner, trip.Id, 76_543, joinerId, title: "Vé");

        var balance = await owner.GetFromJsonAsync<BalanceResponse>(
            $"/trips/{trip.Id}/balance", ApiClient.Json);

        var net = balance!.Balances.ToDictionary(b => b.MemberId, b => b.Net);
        foreach (var transfer in balance.Transfers)
        {
            net[transfer.FromMemberId] += transfer.Amount;
            net[transfer.ToMemberId] -= transfer.Amount;
        }

        net.Values.Should().OnlyContain(v => v == 0);
        _ = joinerId;
    }

    [Fact]
    public async Task Custom_shares_that_do_not_sum_to_the_amount_are_refused()
    {
        var (owner, _, trip, ownerId, joinerId) = await TwoPersonTripAsync("E");

        var response = await AddExpenseAsync(
            owner, trip.Id, 100_000, ownerId, splitType: "Custom",
            shares:
            [
                new { memberId = ownerId, shareAmount = 60_000L },
                new { memberId = joinerId, shareAmount = 30_000L },
            ]);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.Should().Be(ErrorCodes.SharesSumMismatch);
    }

    [Fact]
    public async Task Custom_shares_summing_exactly_are_accepted()
    {
        var (owner, _, trip, ownerId, joinerId) = await TwoPersonTripAsync("F");

        var response = await AddExpenseAsync(
            owner, trip.Id, 100_000, ownerId, splitType: "Custom",
            shares:
            [
                new { memberId = ownerId, shareAmount = 70_000L },
                new { memberId = joinerId, shareAmount = 30_000L },
            ]);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var expense = await response.Content.ReadFromJsonAsync<ExpenseResponse>(ApiClient.Json);
        expense!.Shares.Single(s => s.MemberId == ownerId).ShareAmount.Should().Be(70_000);
    }

    [Fact]
    public async Task A_negative_custom_share_is_refused()
    {
        var (owner, _, trip, ownerId, joinerId) = await TwoPersonTripAsync("G");

        var response = await AddExpenseAsync(
            owner, trip.Id, 100_000, ownerId, splitType: "Custom",
            shares:
            [
                new { memberId = ownerId, shareAmount = 150_000L },
                new { memberId = joinerId, shareAmount = -50_000L },
            ]);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_share_naming_a_member_of_another_trip_is_refused()
    {
        // Reviewer step 3: a foreign id in the body, not just in the route.
        var (owner, _, trip, ownerId, _) = await TwoPersonTripAsync("H");

        var strangerClient = factory.CreateApiClient();
        var stranger = await strangerClient.CreateTripAsync(
            ownerDisplayName: "Stranger", name: "Other trip");

        var response = await AddExpenseAsync(
            owner, trip.Id, 100_000, ownerId, splitType: "Custom",
            shares:
            [
                new { memberId = ownerId, shareAmount = 50_000L },
                new { memberId = stranger.Session.MemberId, shareAmount = 50_000L },
            ]);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "shares");
    }

    [Fact]
    public async Task A_payer_from_another_trip_is_refused()
    {
        var (owner, _, trip, _, _) = await TwoPersonTripAsync("I");

        var strangerClient = factory.CreateApiClient();
        var stranger = await strangerClient.CreateTripAsync(
            ownerDisplayName: "Stranger2", name: "Other trip 2");

        var response = await AddExpenseAsync(owner, trip.Id, 100_000, stranger.Session.MemberId);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "paidByMemberId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_amount_is_refused(long amount)
    {
        var (owner, _, trip, ownerId, _) = await TwoPersonTripAsync($"J{amount}");

        var response = await AddExpenseAsync(owner, trip.Id, amount, ownerId);

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "amount");
    }

    [Fact]
    public async Task An_expense_in_another_currency_is_refused_in_v1()
    {
        var (owner, _, trip, ownerId, _) = await TwoPersonTripAsync("K");

        var response = await AddExpenseAsync(owner, trip.Id, 100_000, ownerId, currency: "USD");

        await response.ShouldBeAsync(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors!.Should().Contain(e => e.Field == "currency");
    }

    [Fact]
    public async Task Shares_are_frozen_when_a_member_joins_later()
    {
        // Spec §7.7: existing Equal expenses are NOT recomputed.
        var owner = factory.CreateApiClient();
        var created = await owner.CreateTripAsync(ownerDisplayName: "Solo payer", name: "Frozen");
        var ownerId = created.Session.MemberId;

        await AddExpenseAsync(owner, created.Trip.Id, 100_000, ownerId);

        var joiner = factory.CreateApiClient();
        await joiner.PostAsJsonAsync("/trips/join", new
        {
            inviteCode = created.Trip.InviteCode,
            displayName = "Latecomer",
        }, ApiClient.Json);

        var expenses = await owner.GetFromJsonAsync<List<ExpenseResponse>>(
            $"/trips/{created.Trip.Id}/expenses", ApiClient.Json);

        expenses.Should().NotBeNull();
        expenses!.Should().ContainSingle();
        expenses![0].Shares.Should().ContainSingle("the expense was split when there was one member");
        expenses![0].Shares[0].ShareAmount.Should().Be(100_000);
    }

    [Fact]
    public async Task Deleting_an_expense_removes_it_and_its_shares()
    {
        var (owner, _, trip, ownerId, _) = await TwoPersonTripAsync("L");
        var created = await (await AddExpenseAsync(owner, trip.Id, 100_000, ownerId))
            .Content.ReadFromJsonAsync<ExpenseResponse>(ApiClient.Json);

        await (await owner.DeleteAsync($"/trips/{trip.Id}/expenses/{created!.Id}"))
            .ShouldBeAsync(HttpStatusCode.OK);

        await factory.WithDbAsync(async db =>
        {
            (await db.Expenses.CountAsync(e => e.Id == created.Id)).Should().Be(0);
            (await db.ExpenseShares.CountAsync(s => s.ExpenseId == created.Id)).Should().Be(0);
        });

        var balance = await owner.GetFromJsonAsync<BalanceResponse>(
            $"/trips/{trip.Id}/balance", ApiClient.Json);
        balance!.TotalSpent.Should().Be(0);
        balance.Transfers.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_expense_mutation_is_logged()
    {
        var (owner, _, trip, ownerId, _) = await TwoPersonTripAsync("M");
        var created = await (await AddExpenseAsync(owner, trip.Id, 50_000, ownerId))
            .Content.ReadFromJsonAsync<ExpenseResponse>(ApiClient.Json);
        await owner.DeleteAsync($"/trips/{trip.Id}/expenses/{created!.Id}");

        var actions = await factory.WithDbAsync(db => db.ActivityLogs
            .Where(a => a.EntityId == created.Id)
            .Select(a => a.Action)
            .ToListAsync());

        actions.Should().Contain(ActivityAction.ExpenseCreated);
        actions.Should().Contain(ActivityAction.ExpenseDeleted);
    }

    [Fact]
    public async Task An_empty_trip_balances_to_nothing()
    {
        var (owner, _, trip, _, _) = await TwoPersonTripAsync("N");

        var balance = await owner.GetFromJsonAsync<BalanceResponse>(
            $"/trips/{trip.Id}/balance", ApiClient.Json);

        balance!.TotalSpent.Should().Be(0);
        balance.Transfers.Should().BeEmpty();
        balance.Balances.Should().OnlyContain(b => b.Net == 0);
    }

    [Fact]
    public async Task Money_survives_the_round_trip_as_an_exact_integer()
    {
        var (owner, _, trip, ownerId, _) = await TwoPersonTripAsync("O");

        // A value a double could not hold exactly.
        const long Awkward = 9_007_199_254_740_993;
        var response = await AddExpenseAsync(owner, trip.Id, Awkward, ownerId);

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var expense = await response.Content.ReadFromJsonAsync<ExpenseResponse>(ApiClient.Json);
        expense!.Amount.Should().Be(Awkward);
        expense.Shares.Sum(s => s.ShareAmount).Should().Be(Awkward);
    }
}
