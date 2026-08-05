using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

public enum ExpenseCategory
{
    Transport = 0,
    Lodging = 1,
    Food = 2,
    Tickets = 3,
    Other = 4,
}

public enum SplitType
{
    Equal = 0,
    Custom = 1,
}

public sealed class Expense : Entity
{
    public Guid TripId { get; set; }

    public required string Title { get; set; }

    /// <summary>Minor units, always positive (spec §5.3).</summary>
    public long Amount { get; set; }

    /// <summary>Must equal the trip currency in v1.</summary>
    public required string Currency { get; set; }

    public Guid PaidByMemberId { get; set; }

    /// <summary>Calendar date in the trip timezone — never a DateTime (spec §7.10).</summary>
    public DateOnly Date { get; set; }

    public ExpenseCategory Category { get; set; }

    public SplitType SplitType { get; set; }

    public List<ExpenseShare> Shares { get; } = [];
}

/// <summary>
/// What one member owes for one expense.
/// <para>
/// Spec §7.7: shares are frozen at creation. A member joining later does not
/// change what was already agreed and possibly already settled — recomputing
/// history would silently rewrite what people owe each other.
/// </para>
/// </summary>
public sealed class ExpenseShare
{
    public Guid ExpenseId { get; set; }

    public Guid MemberId { get; set; }

    /// <summary>Minor units, never negative.</summary>
    public long ShareAmount { get; set; }
}

public static class ExpenseDefaults
{
    public const int TitleMaxLength = 120;
}
