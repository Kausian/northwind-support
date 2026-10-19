using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(SupportDbContext db, IClock clock, SlaPolicy slaPolicy) : ITicketQueries
{
    /// <summary>Escapes wildcards in search text; supported by SQL Server and SQLite alike.</summary>
    private const string LikeEscapeCharacter = "\\";

    /// <remarks>
    /// Filters, then counts, sorts and pages - all in one database query each for the count and
    /// the page, so the count and the pager describe the filtered set.
    /// </remarks>
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct)
    {
        var now = CurrentTime();
        var atRisk = slaPolicy.AtRiskFraction;

        var tickets = ApplyFilters(TicketsWithLabels(), query, now, atRisk);

        var totalCount = await tickets.CountAsync(ct);

        var items = await ApplySort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.SlaWindowSeconds, x.Ticket.ResolvedAtUtc, now, atRisk)
            })
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = CurrentTime();
        var atRisk = slaPolicy.AtRiskFraction;

        return TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.SlaWindowSeconds, x.Ticket.ResolvedAtUtc, now, atRisk)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct)
    {
        var now = CurrentTime();
        var atRisk = slaPolicy.AtRiskFraction;

        return await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.SlaWindowSeconds, x.Ticket.ResolvedAtUtc, now, atRisk)
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TicketEscalationDto>?> GetEscalationsAsync(int ticketId, CancellationToken ct)
    {
        if (!await db.Set<Ticket>().AnyAsync(t => t.Id == ticketId, ct))
        {
            return null;
        }

        return await WithAgents(db.Set<TicketEscalation>().Where(e => e.TicketId == ticketId)).ToListAsync(ct);
    }

    public Task<TicketEscalationDto?> GetEscalationAsync(int escalationId, CancellationToken ct) =>
        WithAgents(db.Set<TicketEscalation>().Where(e => e.Id == escalationId)).FirstOrDefaultAsync(ct);

    /// <summary>
    /// "Now" to the millisecond - the precision every timestamp is stored with, and the scale EF
    /// sends the parameter at. Without this, SQL would compare a rounded "now" while the SLA status
    /// on each row used the full-precision one, and the two could disagree for a fraction of a
    /// millisecond around a due time.
    /// </summary>
    private DateTime CurrentTime()
    {
        var now = clock.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    /// <summary>
    /// Escalation history rows with the names of the agents before and after, newest first; the
    /// id settles two escalations stamped in the same millisecond. Sorted on the columns, before
    /// projecting, so the ORDER BY is in SQL.
    /// </summary>
    private IQueryable<TicketEscalationDto> WithAgents(IQueryable<TicketEscalation> escalations) =>
        from escalation in escalations.AsNoTracking()
        from fromAgent in db.Set<Agent>().Where(a => a.Id == escalation.FromAgentId).DefaultIfEmpty()
        from toAgent in db.Set<Agent>().Where(a => a.Id == escalation.ToAgentId).DefaultIfEmpty()
        orderby escalation.EscalatedAtUtc descending, escalation.Id descending
        select new TicketEscalationDto(
            escalation.Id,
            escalation.FromPriority,
            escalation.ToPriority,
            fromAgent == null ? null : new AgentSummaryDto(fromAgent.Id, fromAgent.FullName),
            toAgent == null ? null : new AgentSummaryDto(toAgent.Id, toAgent.FullName),
            escalation.FromDueAtUtc,
            escalation.ToDueAtUtc,
            escalation.Reason,
            escalation.EscalatedBy,
            escalation.EscalatedAtUtc);

    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels { Ticket = ticket, Customer = customer, Category = category, Agent = agent };

    /// <summary>
    /// Narrows the list to the filters that were supplied. Each one adds a condition to the SQL
    /// WHERE clause, so they combine with AND; a new filter is one more <c>if</c> here.
    /// </summary>
    private static IQueryable<TicketWithLabels> ApplyFilters(
        IQueryable<TicketWithLabels> source, TicketQuery query, DateTime now, double atRiskFraction)
    {
        if (query.Status is { } status)
        {
            source = source.Where(x => x.Ticket.Status == status);
        }

        if (query.Priority is { } priority)
        {
            source = source.Where(x => x.Ticket.Priority == priority);
        }

        if (query.CategoryId is { } categoryId)
        {
            source = source.Where(x => x.Ticket.CategoryId == categoryId);
        }

        if (query.CustomerId is { } customerId)
        {
            source = source.Where(x => x.Ticket.CustomerId == customerId);
        }

        if (query.AssignedAgentId is { } agentId)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == agentId);
        }

        if (query.UnassignedOnly)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == null);
        }

        if (query.SlaStatus is { } slaStatus)
        {
            source = WhereSlaStatus(source, slaStatus, now, atRiskFraction);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLikePattern(query.Search.Trim())}%";

            source = source.Where(x =>
                EF.Functions.Like(x.Ticket.Title, pattern, LikeEscapeCharacter) ||
                EF.Functions.Like(x.Ticket.Reference, pattern, LikeEscapeCharacter) ||
                EF.Functions.Like(x.Customer.Name, pattern, LikeEscapeCharacter));
        }

        return source;
    }

    /// <summary>
    /// Makes the user's text match literally inside a LIKE pattern: <c>%</c>, <c>_</c> and
    /// SQL Server's <c>[</c> character sets would otherwise act as wildcards.
    /// </summary>
    private static string EscapeLikePattern(string text) =>
        text
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscapeCharacter + "[", StringComparison.Ordinal);

    /// <summary>
    /// BR-8 as SQL: the same comparisons as <see cref="SlaEvaluator.Evaluate"/>, so the filter and
    /// the status shown on each row always agree. Plain column comparisons plus one DATEADD on the
    /// "now" parameter - translatable on SQL Server and SQLite alike, and nothing loaded into memory.
    /// </summary>
    /// <remarks>
    /// "At risk" means due within the at-risk share of the window: <c>due &lt;= now + lead</c>, where
    /// lead is <see cref="SlaEvaluator.AtRiskLeadSeconds"/> - the same truncating cast to int.
    /// A NULL due date makes every comparison false, so only NotApplicable matches those tickets.
    /// </remarks>
    private static IQueryable<TicketWithLabels> WhereSlaStatus(
        IQueryable<TicketWithLabels> source, SlaStatus status, DateTime now, double atRiskFraction) => status switch
        {
            SlaStatus.NotApplicable => source.Where(x => x.Ticket.DueAtUtc == null),

            SlaStatus.Met => source.Where(x =>
                x.Ticket.ResolvedAtUtc != null && x.Ticket.ResolvedAtUtc <= x.Ticket.DueAtUtc),

            SlaStatus.Breached => source.Where(x =>
                (x.Ticket.ResolvedAtUtc != null && x.Ticket.ResolvedAtUtc > x.Ticket.DueAtUtc) ||
                (x.Ticket.ResolvedAtUtc == null && x.Ticket.DueAtUtc < now)),

            SlaStatus.AtRisk => source.Where(x =>
                x.Ticket.ResolvedAtUtc == null &&
                x.Ticket.DueAtUtc >= now &&
                x.Ticket.DueAtUtc <= now.AddSeconds((int)(x.Ticket.SlaWindowSeconds!.Value * atRiskFraction))),

            SlaStatus.WithinSla => source.Where(x =>
                x.Ticket.ResolvedAtUtc == null &&
                x.Ticket.DueAtUtc > now.AddSeconds((int)(x.Ticket.SlaWindowSeconds!.Value * atRiskFraction))),

            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown SLA status.")
        };

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}
