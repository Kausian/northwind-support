using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>Body of POST /api/tickets.</summary>
public sealed record CreateTicketRequest(
    string Title,
    string Description,
    int CustomerId,
    int CategoryId,
    TicketPriority? RequestedPriority);

/// <summary>Body of PATCH /api/tickets/{id}/status.</summary>
public sealed record UpdateTicketStatusRequest(TicketStatus Status);

/// <summary>Body of PATCH /api/tickets/{id}/assignment. A null agent unassigns the ticket.</summary>
public sealed record AssignTicketRequest(int? AgentId);

/// <summary>Body of POST /api/tickets/{id}/escalate.</summary>
/// <param name="Reason">Why the ticket is being escalated; 5-500 characters once trimmed.</param>
/// <remarks>Who escalated is never part of the request: it is the signed-in agent (AU-8).</remarks>
public sealed record EscalateTicketRequest(string Reason);
