using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Triage;

/// <summary>
/// BR-7: escalates a ticket using the same SLA and assignment rules as triage - the window for
/// the new priority from <see cref="SlaPolicy"/>, the owner from <see cref="AgentSelector"/>.
/// </summary>
public sealed class TicketEscalator(SlaPolicy slaPolicy)
{
    /// <exception cref="Exceptions.BusinessRuleViolationException">The ticket is Resolved, Closed or Critical.</exception>
    public EscalationOutcome Escalate(
        Ticket ticket,
        Category category,
        CustomerTier customerTier,
        IEnumerable<AgentWorkload> agents,
        string reason,
        string escalatedBy,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        // Refuse before anything is worked out: the new priority is undefined for Critical.
        ticket.EnsureCanBeEscalated();

        var window = slaPolicy.WindowFor(ticket.Priority.OneLevelUp(), customerTier);
        var owner = AgentSelector.KeepOrReassign(ticket.AssignedAgentId, agents, category);

        var escalation = ticket.Escalate(window.Length, owner.Agent?.AgentId, reason, escalatedBy, nowUtc);

        return new EscalationOutcome(escalation, window.Reason, owner.Reason);
    }
}

/// <summary>The recorded escalation, and why the new due date and owner are what they are.</summary>
public sealed record EscalationOutcome(TicketEscalation Escalation, string SlaWindowReason, string AssignmentReason);
