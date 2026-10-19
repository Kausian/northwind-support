using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Triage;

/// <summary>
/// BR-4 and BR-5: who may take a ticket in a category, and which of them gets it.
/// </summary>
public static class AgentSelector
{
    /// <summary>Active, below their open-ticket limit, and specialised where the category requires it.</summary>
    public static bool IsEligible(AgentWorkload agent, Category category)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(category);

        return agent.IsActive
            && agent.HasCapacity
            && (!category.RequiresSpecialist || agent.IsSpecialisedIn(category.Id));
    }

    /// <summary>
    /// The eligible agent with the fewest open tickets; ties go to the lowest agent id, so the
    /// same data always gives the same answer. Nobody being eligible is a normal outcome.
    /// </summary>
    public static AgentSelection Select(IEnumerable<AgentWorkload> agents, Category category)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(category);

        var eligible = agents
            .Where(agent => IsEligible(agent, category))
            .OrderBy(agent => agent.OpenTicketCount)
            .ThenBy(agent => agent.AgentId)
            .ToList();

        if (eligible.Count == 0)
        {
            var who = category.RequiresSpecialist ? $"active {category.Name} specialist" : "active agent";
            return new AgentSelection(null, $"Unassigned: no {who} is below their open-ticket limit.");
        }

        var chosen = eligible[0];
        var tied = eligible.Count(agent => agent.OpenTicketCount == chosen.OpenTicketCount) > 1;

        var pool = eligible.Count == 1 ? "1 eligible agent" : $"{eligible.Count} eligible agents";
        if (category.RequiresSpecialist)
        {
            pool += $" with the {category.Name} specialization";
        }

        var reason = $"Assigned to {chosen.FullName}: fewest open tickets " +
            $"({chosen.OpenTicketCount} of {chosen.MaxOpenTickets}) among {pool}" +
            (tied ? ", tie broken by lowest agent id." : ".");

        return new AgentSelection(chosen, reason);
    }
}

/// <summary>The agent chosen for a ticket, or null when nobody is eligible, and why.</summary>
public sealed record AgentSelection(AgentWorkload? Agent, string Reason);
