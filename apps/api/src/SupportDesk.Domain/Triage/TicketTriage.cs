using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Triage;

/// <summary>
/// Decides priority, due date and owner for a new ticket (BR-1 to BR-5) by combining the
/// individual rules. Each rule stays usable on its own, so escalation can reuse them.
/// </summary>
public sealed class TicketTriage(SlaPolicy slaPolicy)
{
    public TriageDecision Decide(
        TicketPriority? requestedPriority,
        Category category,
        CustomerTier customerTier,
        IEnumerable<AgentWorkload> agents,
        DateTime nowUtc)
    {
        var priority = PriorityRule.Decide(requestedPriority, category);
        var window = slaPolicy.WindowFor(priority.Priority, customerTier);
        var assignment = AgentSelector.Select(agents, category);

        return new TriageDecision(
            priority.Priority,
            window.DueFrom(nowUtc),
            assignment.Agent?.AgentId,
            priority.Reason,
            window.Reason,
            assignment.Reason);
    }
}

/// <summary>What triage decided for a new ticket, with a human-readable reason for each part.</summary>
public sealed record TriageDecision(
    TicketPriority Priority,
    DateTime DueAtUtc,
    int? AssignedAgentId,
    string PriorityReason,
    string SlaWindowReason,
    string AssignmentReason);
