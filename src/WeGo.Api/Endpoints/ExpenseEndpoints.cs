using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Services;

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
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await expenses.CreateAsync(tripId, caller.MemberId, request, cancellationToken);
            return result.ToHttp(expense =>
                Results.Created($"/trips/{tripId}/expenses/{expense.Id}", expense.ToResponse()));
        })
        .WithName("CreateExpense");

        group.MapDelete("/expenses/{expenseId:guid}", async (
            Guid tripId,
            Guid expenseId,
            ExpenseService expenses,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await expenses.DeleteAsync(tripId, expenseId, caller.MemberId, cancellationToken);
            return result.ToHttp(expense => Results.Ok(expense.ToResponse()));
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
