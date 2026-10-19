using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.Domain.Triage;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketCommandHandlerTests
{
    private static Ticket OpenTicket(TicketPriority priority = TicketPriority.Medium, int? agentId = 1) =>
        new TicketBuilder()
            .WithPriority(priority)
            .WithStatus(TicketStatus.Open)
            .AssignedTo(agentId)
            .WithSlaWindow(TimeSpan.FromHours(24))
            .Build()
            .WithId(41);

    [Fact]
    public async Task HandleAsync_EscalatesAndSavesTheChangeWithItsHistoryOnce()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(OpenTicket());
        context.Clock.Advance(TimeSpan.FromHours(2));

        var result = await context.EscalateTicket.HandleAsync(41, "Customer is blocked", "sara", CancellationToken.None);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(context.Clock.UtcNow.AddHours(8), ticket.DueAtUtc);
        Assert.NotNull(context.AddedEscalation);
        Assert.Equal("sara", context.AddedEscalation.EscalatedBy);
        Assert.Equal(context.Clock.UtcNow, context.AddedEscalation.EscalatedAtUtc);
        Assert.NotNull(result.Ticket);
        Assert.NotNull(result.Escalation);

        // One SaveChanges for both: EF Core commits them in a single transaction.
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_UsesTheCustomersTier()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCustomer(CustomerTier.Premium);
        var ticket = context.ExistingTicket(OpenTicket());

        await context.EscalateTicket.HandleAsync(41, "Customer is blocked", "sara", CancellationToken.None);

        Assert.Equal(context.Clock.UtcNow.AddHours(4), ticket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_ReassignsUsingTheTicketsCategory()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCategory(new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false).WithId(2));
        context.AgentWorkloads(
            new AgentWorkload(1, "Alex Turner", true, 10, 5, [1, 3]),
            new AgentWorkload(4, "Sara Lindqvist", true, 6, 2, [1, 2]));
        var ticket = context.ExistingTicket(OpenTicket(agentId: 1));

        await context.EscalateTicket.HandleAsync(41, "Customer is blocked", "sara", CancellationToken.None);

        Assert.Equal(4, ticket.AssignedAgentId);
        Assert.Equal(1, context.AddedEscalation?.FromAgentId);
        Assert.Equal(4, context.AddedEscalation?.ToAgentId);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTicketDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.EscalateTicket.HandleAsync(999, "Customer is blocked", "sara", CancellationToken.None));

        Assert.Contains("Ticket", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCritical_IsAConflictAndSavesNothing()
    {
        var context = new TicketCommandTestContext();
        context.ExistingTicket(OpenTicket(TicketPriority.Critical));

        var exception = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => context.EscalateTicket.HandleAsync(41, "Customer is blocked", "sara", CancellationToken.None));

        Assert.Contains("already Critical", exception.Message);
        Assert.Null(context.AddedEscalation);
        context.VerifySaved(Times.Never());
        context.Agents.Verify(a => a.GetWorkloadsAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task HandleAsync_WhenResolvedOrClosed_IsAConflictAndSavesNothing(TicketStatus status)
    {
        var context = new TicketCommandTestContext();
        context.ExistingTicket(new TicketBuilder().WithStatus(status).Build().WithId(41));

        var exception = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => context.EscalateTicket.HandleAsync(41, "Customer is blocked", "sara", CancellationToken.None));

        Assert.Contains($"is {status}", exception.Message);
        context.VerifySaved(Times.Never());
    }
}
