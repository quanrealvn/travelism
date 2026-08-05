using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Realtime;
using WeGo.Api.Services;
using WeGo.Domain.Entities;

namespace WeGo.Api.Endpoints;

public static class ExpenseEndpoints
{
    public static void MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trips/{tripId:guid}").AddEndpointFilter<TripMemberFilter>();

        group.MapGet("/expenses", async (
            Guid tripId,
            ExpenseService expenses,
            CancellationToken cancellationToken) =>
        {
            var all = await expenses.ListAsync(tripId, cancellationToken);
            return Results.Ok(all.Select(e => e.ToResponse()).ToList());
        })
        .WithName("ListExpenses");

        group.MapPost("/expenses", async (
            Guid tripId,
            CreateExpenseRequest request,
            ExpenseService expenses,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await expenses.CreateAsync(tripId, caller.MemberId, request, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.ExpenseChanged, nameof(Expense),
                expense => expense.Id,
                expense => expense.ToResponse(),
                expense => Results.Created($"/trips/{tripId}/expenses/{expense.Id}", expense.ToResponse()),
                cancellationToken);
        })
        .WithName("CreateExpense");

        group.MapDelete("/expenses/{expenseId:guid}", async (
            Guid tripId,
            Guid expenseId,
            ExpenseService expenses,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await expenses.DeleteAsync(tripId, expenseId, caller.MemberId, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.ExpenseChanged, nameof(Expense),
                expense => expense.Id,
                expense => expense.ToResponse(),
                expense => Results.Ok(expense.ToResponse()),
                cancellationToken);
        })
        .WithName("DeleteExpense");

        group.MapGet("/balance", async (
            Guid tripId,
            ExpenseService expenses,
            CancellationToken cancellationToken) =>
        {
            var balance = await expenses.BalanceAsync(tripId, cancellationToken);
            return Results.Ok(balance.ToResponse());
        })
        .WithName("GetBalance");
    }
}
