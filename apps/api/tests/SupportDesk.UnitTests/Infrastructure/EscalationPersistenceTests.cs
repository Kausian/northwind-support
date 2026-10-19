using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure;

/// <summary>
/// Escalation against a real database: the ticket's change and its history row are saved
/// together or not at all, and the history reads back complete and newest first.
/// </summary>
public sealed class EscalationPersistenceTests : IDisposable
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    private readonly SqliteTestDatabase _database = new();
    private readonly int _ticketId;
    private readonly int _alexId;
    private readonly int _saraId;

    public EscalationPersistenceTests()
    {
        using var db = _database.CreateContext();

        var category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        var customer = new Customer("Fabrikam Inc", "help@fabrikam.test", null, CustomerTier.Standard, Now);
        var alex = new Agent("Alex Turner", "alex@northwind.test", 10, Now);
        var sara = new Agent("Sara Lindqvist", "sara@northwind.test", 6, Now);
        db.AddRange(category, customer, alex, sara);
        db.SaveChanges();

        var ticket = Ticket.Raise("TCK-0001", "Cannot sign in", "Details.", customer.Id, category.Id, TicketPriority.Low, Now, TimeSpan.FromHours(72), alex.Id);
        db.Add(ticket);
        db.SaveChanges();

        (_ticketId, _alexId, _saraId) = (ticket.Id, alex.Id, sara.Id);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Escalating_SavesTheTicketAndItsHistoryTogether()
    {
        await EscalateAsync(TimeSpan.FromHours(24), _saraId, "First escalation", "sara", Now.AddHours(1));

        await using var db = _database.CreateContext();
        var ticket = await db.Set<Ticket>().AsNoTracking().SingleAsync(t => t.Id == _ticketId);
        var row = await db.Set<TicketEscalation>().AsNoTracking().SingleAsync();

        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Equal(_saraId, ticket.AssignedAgentId);
        Assert.Equal(Now.AddHours(25), ticket.DueAtUtc);
        Assert.Equal(24 * 3600, ticket.SlaWindowSeconds);

        Assert.Equal(_ticketId, row.TicketId);
        Assert.Equal((TicketPriority.Low, TicketPriority.Medium), (row.FromPriority, row.ToPriority));
        Assert.Equal((_alexId, _saraId), (row.FromAgentId!.Value, row.ToAgentId!.Value));
        Assert.Equal(Now.AddHours(72), row.FromDueAtUtc);
        Assert.Equal(Now.AddHours(25), row.ToDueAtUtc);
        Assert.Equal("First escalation", row.Reason);
        Assert.Equal("sara", row.EscalatedBy);
        Assert.Equal(Now.AddHours(1), row.EscalatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, row.EscalatedAtUtc.Kind);
    }

    [Fact]
    public async Task Escalating_WhenTheSaveFails_KeepsNeitherTheChangeNorTheHistory()
    {
        // An agent id that does not exist breaks a foreign key, so the save is rejected.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => EscalateAsync(TimeSpan.FromHours(24), 9999, "Doomed escalation", "sara", Now.AddHours(1)));

        await using var db = _database.CreateContext();
        var ticket = await db.Set<Ticket>().AsNoTracking().SingleAsync(t => t.Id == _ticketId);

        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal(_alexId, ticket.AssignedAgentId);
        Assert.Equal(Now.AddHours(72), ticket.DueAtUtc);
        Assert.Empty(db.Set<TicketEscalation>());
    }

    [Fact]
    public async Task History_ReadsBackEveryEscalationNewestFirst_WithAgentNames()
    {
        await EscalateAsync(TimeSpan.FromHours(24), _alexId, "Low to Medium", "alex", Now.AddHours(1));
        await EscalateAsync(TimeSpan.FromHours(8), _saraId, "Medium to High", "sara", Now.AddHours(2));
        await EscalateAsync(TimeSpan.FromHours(4), null, "High to Critical", "priya", Now.AddHours(3));

        await using var db = _database.CreateContext();
        var history = await new TicketQueries(db, new FixedClock(), TestSla.Policy()).GetEscalationsAsync(_ticketId, CancellationToken.None);

        Assert.NotNull(history);
        Assert.Equal(["High to Critical", "Medium to High", "Low to Medium"], history.Select(e => e.Reason));

        var newest = history[0];
        Assert.Equal((TicketPriority.High, TicketPriority.Critical), (newest.FromPriority, newest.ToPriority));
        Assert.Equal("Sara Lindqvist", newest.FromAgent?.FullName);
        Assert.Null(newest.ToAgent);
        Assert.Equal(Now.AddHours(10), newest.FromDueAtUtc);
        Assert.Equal(Now.AddHours(7), newest.ToDueAtUtc);
        Assert.Equal("priya", newest.EscalatedBy);
        Assert.Equal(Now.AddHours(3), newest.EscalatedAtUtc);

        Assert.Equal("Alex Turner", history[2].FromAgent?.FullName);
        Assert.Equal("Alex Turner", history[2].ToAgent?.FullName);
    }

    [Fact]
    public async Task History_OfAnUnknownTicket_IsNull()
    {
        await using var db = _database.CreateContext();

        Assert.Null(await new TicketQueries(db, new FixedClock(), TestSla.Policy()).GetEscalationsAsync(9999, CancellationToken.None));
    }

    [Fact]
    public async Task History_OfATicketNeverEscalated_IsEmpty()
    {
        await using var db = _database.CreateContext();

        Assert.Empty((await new TicketQueries(db, new FixedClock(), TestSla.Policy()).GetEscalationsAsync(_ticketId, CancellationToken.None))!);
    }

    /// <summary>What the handler does: load, escalate, stage the history, one SaveChanges.</summary>
    private async Task EscalateAsync(TimeSpan window, int? agentId, string reason, string by, DateTime at)
    {
        await using var db = _database.CreateContext();
        var tickets = new TicketRepository(db);

        var ticket = await tickets.GetByIdAsync(_ticketId, CancellationToken.None);
        var escalation = ticket!.Escalate(window, agentId, reason, by, at);

        await tickets.AddEscalationAsync(escalation, CancellationToken.None);
        await db.SaveChangesAsync();
    }
}
