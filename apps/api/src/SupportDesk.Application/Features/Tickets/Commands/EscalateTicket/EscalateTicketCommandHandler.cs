using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Triage;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

/// <summary>
/// Escalates a ticket (BR-7): one priority level up, a new due date from now, and an owner who
/// is eligible for the new state - recorded in the ticket's escalation history.
/// </summary>
public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    TicketEscalator escalator,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ITicketQueries ticketQueries,
    ILogger<EscalateTicketCommandHandler> logger)
{
    /// <param name="escalatedBy">The signed-in agent, from their token - never from the request body.</param>
    /// <exception cref="NotFoundException">The ticket does not exist.</exception>
    /// <exception cref="Domain.Exceptions.BusinessRuleViolationException">The ticket is Critical, Resolved or Closed.</exception>
    public async Task<EscalationResultDto> HandleAsync(int id, string reason, string escalatedBy, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);

        // Fails fast with a 409 before anything else is loaded.
        ticket.EnsureCanBeEscalated();

        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);

        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);

        var outcome = escalator.Escalate(
            ticket, category, customer.Tier, await agents.GetWorkloadsAsync(ct), reason, escalatedBy, clock.UtcNow);

        // The ticket's change and its history row go to the database in one SaveChanges, which
        // EF Core wraps in a single transaction: both are saved or neither is.
        await tickets.AddEscalationAsync(outcome.Escalation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} escalated by {EscalatedBy} from {From} to {To}. {SlaWindow} {Assignment}",
            ticket.Reference, outcome.Escalation.EscalatedBy, outcome.Escalation.FromPriority,
            outcome.Escalation.ToPriority, outcome.SlaWindowReason, outcome.AssignmentReason);

        var escalation = await ticketQueries.GetEscalationAsync(outcome.Escalation.Id, ct)
            ?? throw new NotFoundException("Escalation", outcome.Escalation.Id);

        return new EscalationResultDto(await getTicket.HandleAsync(id, ct), escalation);
    }
}
