using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Triage;

/// <summary>
/// BR-1: the priority a new ticket starts with.
/// </summary>
public static class PriorityRule
{
    /// <summary>The priority used when the customer did not ask for one.</summary>
    public const TicketPriority Default = TicketPriority.Medium;

    /// <summary>
    /// The requested priority, or <see cref="Default"/>; always Critical in a category flagged
    /// <see cref="Category.ForcesCriticalPriority"/>.
    /// </summary>
    public static PriorityDecision Decide(TicketPriority? requested, Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.ForcesCriticalPriority)
        {
            var reason = requested is null or TicketPriority.Critical
                ? $"{category.Name} tickets are always Critical."
                : $"{category.Name} tickets are always Critical (requested {requested}).";

            return new PriorityDecision(TicketPriority.Critical, reason);
        }

        return requested is { } priority
            ? new PriorityDecision(priority, $"Requested priority {priority}.")
            : new PriorityDecision(Default, $"No priority requested; defaulted to {Default}.");
    }
}

/// <summary>The priority chosen for a ticket and why.</summary>
public sealed record PriorityDecision(TicketPriority Priority, string Reason);
