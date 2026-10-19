using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

/// <summary>BR-7 on the aggregate: what an escalation changes and what it records.</summary>
public class TicketEscalationTests
{
    private static readonly DateTime Raised = FixedClock.DefaultNow;
    private static readonly DateTime Later = Raised.AddHours(3);
    private static readonly TimeSpan EightHours = TimeSpan.FromHours(8);

    private static Ticket OpenTicket(TicketPriority priority, int? agentId = 4) =>
        new TicketBuilder()
            .WithPriority(priority)
            .WithStatus(TicketStatus.InProgress)
            .AssignedTo(agentId)
            .WithSlaWindow(TimeSpan.FromHours(24))
            .Build()
            .WithId(41);

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void Escalate_RaisesThePriorityExactlyOneLevel(TicketPriority from, TicketPriority to)
    {
        var ticket = OpenTicket(from);

        var escalation = ticket.Escalate(EightHours, 4, "Customer is blocked", "sara", Later);

        Assert.Equal(to, ticket.Priority);
        Assert.Equal(from, escalation.FromPriority);
        Assert.Equal(to, escalation.ToPriority);
    }

    [Fact]
    public void Escalate_RestartsTheSlaWindowFromTheEscalation()
    {
        var ticket = OpenTicket(TicketPriority.Medium);

        ticket.Escalate(EightHours, 4, "Customer is blocked", "sara", Later);

        Assert.Equal(Later.AddHours(8), ticket.DueAtUtc);
        Assert.Equal(8 * 3600, ticket.SlaWindowSeconds);
        Assert.Equal(Later, ticket.UpdatedAtUtc);
    }

    [Fact]
    public void Escalate_RecordsTheCompleteBeforeAndAfter()
    {
        var ticket = OpenTicket(TicketPriority.Medium, agentId: 4);
        var dueBefore = ticket.DueAtUtc;

        var escalation = ticket.Escalate(EightHours, 2, "  Customer is blocked  ", "  sara  ", Later);

        Assert.Equal(41, escalation.TicketId);
        Assert.Equal(TicketPriority.Medium, escalation.FromPriority);
        Assert.Equal(TicketPriority.High, escalation.ToPriority);
        Assert.Equal(4, escalation.FromAgentId);
        Assert.Equal(2, escalation.ToAgentId);
        Assert.Equal(dueBefore, escalation.FromDueAtUtc);
        Assert.Equal(Later.AddHours(8), escalation.ToDueAtUtc);
        Assert.Equal("Customer is blocked", escalation.Reason);
        Assert.Equal("sara", escalation.EscalatedBy);
        Assert.Equal(Later, escalation.EscalatedAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
    }

    [Fact]
    public void Escalate_CanLeaveTheTicketUnassigned()
    {
        var ticket = OpenTicket(TicketPriority.Low, agentId: 4);

        var escalation = ticket.Escalate(EightHours, null, "Customer is blocked", "sara", Later);

        Assert.Null(ticket.AssignedAgentId);
        Assert.Equal(4, escalation.FromAgentId);
        Assert.Null(escalation.ToAgentId);
    }

    [Fact]
    public void Escalate_WhenCritical_IsRefusedAndChangesNothing()
    {
        var ticket = OpenTicket(TicketPriority.Critical);
        var dueBefore = ticket.DueAtUtc;

        var exception = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(EightHours, 4, "Customer is blocked", "sara", Later));

        Assert.Equal("Ticket TCK-0001 is already Critical and cannot be escalated further.", exception.Message);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
        Assert.Equal(dueBefore, ticket.DueAtUtc);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Escalate_WhenResolvedOrClosed_IsRefusedNamingTheStatus(TicketStatus status)
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).WithStatus(status).Build();

        var exception = Assert.Throws<BusinessRuleViolationException>(
            () => ticket.Escalate(EightHours, 4, "Customer is blocked", "sara", Later));

        Assert.Equal($"Ticket TCK-0001 is {status} and cannot be escalated.", exception.Message);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
    }

    [Fact]
    public void Escalate_WhenResolvedAndCritical_ReportsTheStatus()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Critical).WithStatus(TicketStatus.Resolved).Build();

        var exception = Assert.Throws<BusinessRuleViolationException>(ticket.EnsureCanBeEscalated);

        Assert.Contains("is Resolved", exception.Message);
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress)]
    public void EnsureCanBeEscalated_AllowsEveryOpenStatus(TicketStatus status)
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.High).WithStatus(status).Build();

        ticket.EnsureCanBeEscalated();
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("abcd")]
    [InlineData("  abcd  ")]
    public void Escalate_WithATooShortReason_Throws(string reason)
    {
        var ticket = OpenTicket(TicketPriority.Low);

        Assert.ThrowsAny<ArgumentException>(() => ticket.Escalate(EightHours, 4, reason, "sara", Later));
        Assert.Equal(TicketPriority.Low, ticket.Priority);
    }

    [Fact]
    public void Escalate_AcceptsReasonsAtBothLengthLimits()
    {
        OpenTicket(TicketPriority.Low).Escalate(EightHours, 4, "abcde", "sara", Later);
        OpenTicket(TicketPriority.Low).Escalate(EightHours, 4, new string('x', 500), "sara", Later);
    }

    [Fact]
    public void Escalate_WithATooLongReason_Throws() =>
        Assert.ThrowsAny<ArgumentException>(
            () => OpenTicket(TicketPriority.Low).Escalate(EightHours, 4, new string('x', 501), "sara", Later));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Escalate_WithoutAnActor_Throws(string actor) =>
        Assert.ThrowsAny<ArgumentException>(
            () => OpenTicket(TicketPriority.Low).Escalate(EightHours, 4, "Customer is blocked", actor, Later));

    [Fact]
    public void Raise_WithAWindow_IsDueThatLongAfterCreation()
    {
        var ticket = new TicketBuilder().WithSlaWindow(TimeSpan.FromHours(4)).Build();

        Assert.Equal(Raised.AddHours(4), ticket.DueAtUtc);
        Assert.Equal(4 * 3600, ticket.SlaWindowSeconds);
    }

    [Fact]
    public void Raise_WithAWindowOfPartSeconds_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TicketBuilder().WithSlaWindow(TimeSpan.FromMilliseconds(1500)).Build());

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void OneLevelUp_StepsOneLevel(TicketPriority from, TicketPriority to) =>
        Assert.Equal(to, from.OneLevelUp());

    [Fact]
    public void OneLevelUp_FromCritical_Throws() =>
        Assert.Throws<InvalidOperationException>(() => TicketPriority.Critical.OneLevelUp());
}
