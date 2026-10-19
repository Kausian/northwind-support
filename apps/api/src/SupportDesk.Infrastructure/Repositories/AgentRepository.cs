using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Triage;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class AgentRepository(SupportDbContext db) : IAgentRepository
{
    /// <summary>Loads the whole aggregate, specializations included.</summary>
    public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <remarks>
    /// One query: the open-ticket count is a correlated COUNT in SQL, not a load of tickets.
    /// Every agent is returned so the selection rules - not the query - decide eligibility; a
    /// support team is small enough for that to cost nothing.
    /// </remarks>
    public async Task<IReadOnlyList<AgentWorkload>> GetWorkloadsAsync(CancellationToken ct) =>
        await db.Set<Agent>()
            .AsNoTracking()
            .Select(a => new AgentWorkload(
                a.Id,
                a.FullName,
                a.IsActive,
                a.MaxOpenTickets,
                // Open as in Ticket.IsOpen, which EF cannot translate.
                db.Set<Ticket>().Count(t =>
                    t.AssignedAgentId == a.Id &&
                    t.Status != TicketStatus.Resolved &&
                    t.Status != TicketStatus.Closed),
                a.Specializations.Select(s => s.CategoryId).ToList()))
            .ToListAsync(ct);
}
