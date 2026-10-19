using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Triage;

public class TicketTriageTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    private static readonly Category Outage =
        new Category("Outage", requiresSpecialist: true, forcesCriticalPriority: true).WithId(4);

    private readonly TicketTriage _triage = new(TestSla.Policy());

    [Fact]
    public void Decide_CombinesPriorityWindowAndOwner()
    {
        AgentWorkload[] agents =
        [
            new(2, "Ben Osei", true, 8, 3, [3, 4]),
            new(3, "Priya Nair", true, 8, 4, [4, 5])
        ];

        var decision = _triage.Decide(TicketPriority.Low, Outage, CustomerTier.Premium, agents, Now);

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Equal(Now.AddHours(2), decision.DueAtUtc);
        Assert.Equal(2, decision.AssignedAgentId);
        Assert.Equal("Outage tickets are always Critical (requested Low).", decision.PriorityReason);
        Assert.Equal("Critical priority: 4 h window, reduced to 2 h for a Premium customer.", decision.SlaWindowReason);
        Assert.StartsWith("Assigned to Ben Osei", decision.AssignmentReason);
    }

    [Fact]
    public void Decide_WithNobodyEligible_StillDecidesPriorityAndDueDate()
    {
        var decision = _triage.Decide(null, Outage, CustomerTier.Standard, [], Now);

        Assert.Null(decision.AssignedAgentId);
        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Equal(Now.AddHours(4), decision.DueAtUtc);
        Assert.StartsWith("Unassigned:", decision.AssignmentReason);
    }
}
