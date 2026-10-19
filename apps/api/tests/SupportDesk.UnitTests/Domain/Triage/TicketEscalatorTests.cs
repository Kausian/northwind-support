using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.Domain.Triage;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Triage;

/// <summary>
/// BR-7 end to end in the domain: the new window from the SLA policy and the owner from the
/// agent rules - the same units triage uses.
/// </summary>
public class TicketEscalatorTests
{
    private const int BillingId = 2;

    private static readonly DateTime EscalatedAt = FixedClock.DefaultNow.AddHours(5);

    private static readonly Category Billing =
        new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false).WithId(BillingId);

    private static readonly Category General =
        new Category("General", requiresSpecialist: false, forcesCriticalPriority: false).WithId(1);

    private readonly TicketEscalator _escalator = new(TestSla.Policy());

    private static Ticket Ticket(TicketPriority priority, int? agentId) =>
        new TicketBuilder()
            .WithPriority(priority)
            .WithStatus(TicketStatus.Open)
            .AssignedTo(agentId)
            .WithSlaWindow(TimeSpan.FromHours(24))
            .Build()
            .WithId(41);

    private static AgentWorkload Agent(int id, int open, int max = 10, bool active = true, params int[] specializations) =>
        new(id, $"Agent {id}", active, max, open, specializations);

    private EscalationOutcome Escalate(
        Ticket ticket, Category category, CustomerTier tier, params AgentWorkload[] agents) =>
        _escalator.Escalate(ticket, category, tier, agents, "Customer is blocked", "sara", EscalatedAt);

    [Theory]
    [InlineData(TicketPriority.Low, CustomerTier.Standard, 24)] // to Medium
    [InlineData(TicketPriority.Medium, CustomerTier.Standard, 8)] // to High
    [InlineData(TicketPriority.High, CustomerTier.Standard, 4)] // to Critical
    [InlineData(TicketPriority.Low, CustomerTier.Premium, 12)]
    [InlineData(TicketPriority.Medium, CustomerTier.Premium, 4)]
    [InlineData(TicketPriority.High, CustomerTier.Premium, 2)]
    public void Escalate_DueDateIsTheNewPrioritysWindowFromNow(TicketPriority from, CustomerTier tier, double hours)
    {
        var ticket = Ticket(from, agentId: 1);

        Escalate(ticket, General, tier, Agent(1, open: 3));

        Assert.Equal(EscalatedAt.AddHours(hours), ticket.DueAtUtc);
    }

    [Fact]
    public void Escalate_PremiumWindowRespectsTheMinimum()
    {
        var escalator = new TicketEscalator(new SlaPolicy(TestSla.Settings(criticalHours: 1.5)));
        var ticket = Ticket(TicketPriority.High, agentId: 1);

        var outcome = escalator.Escalate(ticket, General, CustomerTier.Premium, [Agent(1, open: 3)], "Customer is blocked", "sara", EscalatedAt);

        Assert.Equal(EscalatedAt.AddHours(1), ticket.DueAtUtc);
        Assert.Contains("raised to the 1 h minimum", outcome.SlaWindowReason);
    }

    [Fact]
    public void Escalate_KeepsTheCurrentAgentWhileStillEligible_EvenIfSomeoneIsLessBusy()
    {
        var ticket = Ticket(TicketPriority.Low, agentId: 1);

        var outcome = Escalate(ticket, General, CustomerTier.Standard, Agent(1, open: 7), Agent(2, open: 0));

        Assert.Equal(1, ticket.AssignedAgentId);
        Assert.Equal(1, outcome.Escalation.FromAgentId);
        Assert.Equal(1, outcome.Escalation.ToAgentId);
        Assert.Equal("Kept with Agent 1, who is still eligible.", outcome.AssignmentReason);
    }

    [Fact]
    public void Escalate_KeepsAnAgentAtTheirLimit_BecauseTheTicketIsAlreadyTheirs()
    {
        // 6 of 6 includes this ticket: without it they are at 5, below the limit.
        var ticket = Ticket(TicketPriority.Low, agentId: 5);

        Escalate(ticket, Billing, CustomerTier.Standard, Agent(5, open: 6, max: 6, specializations: BillingId));

        Assert.Equal(5, ticket.AssignedAgentId);
    }

    [Fact]
    public void Escalate_ReassignsWhenTheCurrentAgentIsInactive()
    {
        var ticket = Ticket(TicketPriority.Low, agentId: 6);

        var outcome = Escalate(
            ticket, General, CustomerTier.Standard, Agent(6, open: 1, active: false), Agent(2, open: 4), Agent(3, open: 2));

        Assert.Equal(3, ticket.AssignedAgentId);
        Assert.Equal(6, outcome.Escalation.FromAgentId);
        Assert.Equal(3, outcome.Escalation.ToAgentId);
        Assert.StartsWith("Agent 6 is no longer eligible. Assigned to Agent 3", outcome.AssignmentReason);
    }

    [Fact]
    public void Escalate_ReassignsWhenTheCurrentAgentIsNotASpecialist()
    {
        var ticket = Ticket(TicketPriority.Low, agentId: 1);

        Escalate(ticket, Billing, CustomerTier.Standard, Agent(1, open: 0), Agent(4, open: 5, specializations: BillingId));

        Assert.Equal(4, ticket.AssignedAgentId);
    }

    [Fact]
    public void Escalate_ReassignsWhenTheCurrentAgentIsOverTheirLimit()
    {
        // 8 open with a limit of 6: still over it without this ticket (the limit was lowered).
        var ticket = Ticket(TicketPriority.Low, agentId: 1);

        Escalate(ticket, General, CustomerTier.Standard, Agent(1, open: 8, max: 6), Agent(2, open: 3));

        Assert.Equal(2, ticket.AssignedAgentId);
    }

    [Fact]
    public void Escalate_AssignsAnUnassignedTicketWhenSomeoneIsEligible()
    {
        var ticket = Ticket(TicketPriority.Medium, agentId: null);

        var outcome = Escalate(ticket, General, CustomerTier.Standard, Agent(2, open: 3));

        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Null(outcome.Escalation.FromAgentId);
    }

    [Fact]
    public void Escalate_WithNobodyEligible_LeavesItUnassignedAndStillEscalates()
    {
        var ticket = Ticket(TicketPriority.Medium, agentId: 1);

        var outcome = Escalate(
            ticket, Billing, CustomerTier.Standard,
            Agent(1, open: 2, active: false, specializations: BillingId), Agent(5, open: 6, max: 6, specializations: BillingId));

        Assert.Null(ticket.AssignedAgentId);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(EscalatedAt.AddHours(8), ticket.DueAtUtc);
        Assert.Equal(
            "Agent 1 is no longer eligible. Unassigned: no active Billing specialist is below their open-ticket limit.",
            outcome.AssignmentReason);
    }

    [Fact]
    public void Escalate_WhenCritical_IsRefusedBeforeAnythingIsWorkedOut()
    {
        var ticket = Ticket(TicketPriority.Critical, agentId: 1);

        Assert.Throws<BusinessRuleViolationException>(() => Escalate(ticket, General, CustomerTier.Standard));
        Assert.Equal(1, ticket.AssignedAgentId);
    }
}
