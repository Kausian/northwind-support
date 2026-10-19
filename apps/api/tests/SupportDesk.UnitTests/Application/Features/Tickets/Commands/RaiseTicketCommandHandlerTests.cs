using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static readonly Category Security =
        new Category("Security", requiresSpecialist: true, forcesCriticalPriority: true).WithId(5);

    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Categories
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Category", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_SetsTheDueDateFromCreationTime()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        Assert.Equal(FixedClock.DefaultNow.AddHours(8), context.AddedTicket?.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_ForAPremiumCustomer_HalvesTheWindow()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCustomer(CustomerTier.Premium);

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket?.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_InACategoryThatForcesCritical_IgnoresTheRequestedPriority()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCategory(Security);
        context.AgentWorkloads(new AgentWorkload(3, "Priya Nair", true, 8, 4, [4, 5]));

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low), CancellationToken.None);

        Assert.Equal(TicketPriority.Critical, context.AddedTicket?.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket?.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_AssignsTheLeastBusyEligibleAgent()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCategory(Security);
        context.AgentWorkloads(
            new AgentWorkload(1, "Alex Turner", true, 10, 0, [1, 3]), // not a Security specialist
            new AgentWorkload(3, "Priya Nair", true, 8, 4, [4, 5]),
            new AgentWorkload(5, "Marcus Doyle", true, 6, 6, [2, 5])); // at the limit

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(3, context.AddedTicket?.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_WhenNobodyIsEligible_RaisesItUnassignedWithTheReason()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCategory(Security);
        context.AgentWorkloads(new AgentWorkload(5, "Marcus Doyle", true, 6, 6, [2, 5]));

        var response = await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Null(context.AddedTicket.AssignedAgentId);
        Assert.NotNull(context.AddedTicket.DueAtUtc);
        context.VerifySaved(Times.Once());
        Assert.Equal(
            "Unassigned: no active Security specialist is below their open-ticket limit.",
            response.Triage?.Assignment);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheTriageReasons()
    {
        var context = new TicketCommandTestContext();
        context.ExistingCustomer(CustomerTier.Premium);
        context.ExistingCategory(Security);
        context.AgentWorkloads(new AgentWorkload(3, "Priya Nair", true, 8, 4, [4, 5]));

        var response = await context.RaiseTicket.HandleAsync(Request(TicketPriority.Medium), CancellationToken.None);

        Assert.NotNull(response.Triage);
        Assert.Equal("Security tickets are always Critical (requested Medium).", response.Triage.Priority);
        Assert.Equal("Critical priority: 4 h window, reduced to 2 h for a Premium customer.", response.Triage.SlaWindow);
        Assert.Equal(
            "Assigned to Priya Nair: fewest open tickets (4 of 8) among 1 eligible agent with the Security specialization.",
            response.Triage.Assignment);
    }
}
