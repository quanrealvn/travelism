namespace WeGo.Domain.Abstractions;

/// <summary>
/// Single source of "now" for the whole system. Everything that stamps a
/// timestamp goes through this so tests can freeze time and so no production
/// code ever reaches for <c>DateTime.Now</c> / <c>DateTime.Today</c>
/// (both are server-timezone dependent — see spec §7.10).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
