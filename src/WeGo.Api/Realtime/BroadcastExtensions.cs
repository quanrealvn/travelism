using WeGo.Api.Common;

namespace WeGo.Api.Realtime;

public static class BroadcastExtensions
{
    /// <summary>
    /// Maps a service result to a response, announcing it first if it succeeded.
    /// <para>
    /// The broadcast happens here rather than inside the service, and that is
    /// what makes spec §5.8's "after SaveChanges commits" structurally true: a
    /// <see cref="Result{T}"/> only carries a value when the write already
    /// committed, so a rolled-back change has nothing to announce.
    /// </para>
    /// </summary>
    public static async Task<IResult> BroadcastThenRespond<T>(
        this Result<T> result,
        ITripBroadcaster broadcaster,
        Guid tripId,
        Guid byMemberId,
        string eventName,
        string entityType,
        Func<T, Guid> entityId,
        Func<T, object?> payload,
        Func<T, IResult> onSuccess,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            var value = result.Value!;
            await broadcaster
                .BroadcastAsync(
                    tripId, eventName, entityType, entityId(value), payload(value),
                    byMemberId, cancellationToken)
                .ConfigureAwait(false);
        }

        return result.ToHttp(onSuccess);
    }
}
