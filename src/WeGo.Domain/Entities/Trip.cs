using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

public sealed class Trip : Entity
{
    public required string Name { get; set; }

    public required string Destination { get; set; }

    /// <summary>Calendar date in <see cref="TimeZoneId"/> — never a DateTime (spec §7.10).</summary>
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>IANA identifier, e.g. <c>Asia/Bangkok</c>.</summary>
    public string TimeZoneId { get; set; } = TripDefaults.TimeZoneId;

    /// <summary>ISO 4217 alpha-3. Frozen after creation (see DECISIONS.md).</summary>
    public string Currency { get; set; } = TripDefaults.Currency;

    /// <summary>Minor units of <see cref="Currency"/> (spec §5.3: money is always <c>long</c>).</summary>
    public long? BudgetAmount { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Planning;

    /// <summary>8 chars, cryptographically random, unique, stored uppercase.</summary>
    public required string InviteCode { get; set; }

    public List<Member> Members { get; } = [];

    public List<Place> Places { get; } = [];

    /// <summary>Inclusive day count of the trip.</summary>
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;

    public bool ContainsDate(DateOnly date) => date >= StartDate && date <= EndDate;
}

public static class TripDefaults
{
    public const string TimeZoneId = "Asia/Bangkok";
    public const string Currency = "VND";
    public const int MaxMembers = 10;
    public const int MaxSpanDays = 60;
    public const int NameMaxLength = 80;
    public const int DestinationMaxLength = 120;
}
