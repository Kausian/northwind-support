namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// How urgent a ticket is. The numeric values are ordered, lowest urgency first, and are
/// persisted as integers - do not renumber them.
/// </summary>
public enum TicketPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public static class TicketPriorityExtensions
{
    /// <summary>The priority one level up: Low to Medium, Medium to High, High to Critical.</summary>
    /// <exception cref="InvalidOperationException">The priority is already Critical.</exception>
    public static TicketPriority OneLevelUp(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => TicketPriority.Medium,
        TicketPriority.Medium => TicketPriority.High,
        TicketPriority.High => TicketPriority.Critical,
        _ => throw new InvalidOperationException($"{priority} is the highest priority.")
    };
}
