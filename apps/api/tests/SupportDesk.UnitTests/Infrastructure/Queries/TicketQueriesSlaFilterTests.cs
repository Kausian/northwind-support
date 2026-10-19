using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Queries.SearchTickets;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

/// <summary>
/// The slaStatus filter, translated to SQL and run by a database, on tickets placed exactly on
/// BR-8's boundaries - and checked against the status <see cref="SlaEvaluator"/> gives each row.
/// </summary>
public sealed class TicketQueriesSlaFilterTests : IDisposable
{
    private static readonly DateTime Now = FixedClock.DefaultNow;
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4); // at risk for its last hour

    private readonly SqliteTestDatabase _database = new();

    public TicketQueriesSlaFilterTests() => Seed();

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(SlaStatus.NotApplicable, new[] { "NO-SLA" })]
    [InlineData(SlaStatus.WithinSla, new[] { "WITHIN-FRESH", "WITHIN-EDGE", "WITHIN-ODD" })]
    [InlineData(SlaStatus.AtRisk, new[] { "RISK-EDGE", "RISK-DUE-NOW", "RISK-ODD" })]
    [InlineData(SlaStatus.Breached, new[] { "BREACHED-OPEN", "BREACHED-LATE" })]
    [InlineData(SlaStatus.Met, new[] { "MET-EDGE", "MET-EARLY" })]
    public async Task SlaStatus_MatchesExactlyTheTicketsOnEachSideOfTheBoundaries(SlaStatus status, string[] expected)
    {
        var result = await Search(new TicketQuery { SlaStatus = status });

        Assert.Equal(expected.Order(), result.Items.Select(t => t.Reference).Order());
        Assert.Equal(expected.Length, result.TotalCount);
    }

    [Fact]
    public async Task SlaStatusFilter_AgreesWithTheStatusShownOnEveryRow()
    {
        var everything = await Search(new TicketQuery { PageSize = 100 });

        foreach (var status in Enum.GetValues<SlaStatus>())
        {
            var filtered = await Search(new TicketQuery { SlaStatus = status, PageSize = 100 });
            var labelled = everything.Items.Where(t => t.SlaStatus == status).Select(t => t.Reference);

            Assert.Equal(labelled.Order(), filtered.Items.Select(t => t.Reference).Order());
        }

        // Every ticket falls in exactly one status.
        var total = 0;
        foreach (var status in Enum.GetValues<SlaStatus>())
        {
            total += (await Search(new TicketQuery { SlaStatus = status })).TotalCount;
        }

        Assert.Equal(everything.TotalCount, total);
    }

    [Fact]
    public async Task SlaStatus_CombinesWithOtherFilters()
    {
        var result = await Search(new TicketQuery { SlaStatus = SlaStatus.AtRisk, Priority = TicketPriority.High });

        Assert.Equal(["RISK-DUE-NOW", "RISK-EDGE"], result.Items.Select(t => t.Reference).Order());
    }

    [Fact]
    public async Task SlaStatus_CombinesWithStatusAndSearch()
    {
        var result = await Search(new TicketQuery
        {
            SlaStatus = SlaStatus.Breached,
            Status = TicketStatus.Resolved,
            Search = "late"
        });

        Assert.Equal(["BREACHED-LATE"], result.Items.Select(t => t.Reference));
    }

    [Fact]
    public async Task SlaStatus_IsCountedAndPagedInTheDatabase()
    {
        var query = new TicketQuery { SlaStatus = SlaStatus.AtRisk, PageSize = 2, SortBy = "dueAtUtc", SortDirection = "asc" };

        var first = await Search(query);
        var second = await Search(query with { Page = 2 });

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(["RISK-DUE-NOW", "RISK-ODD"], first.Items.Select(t => t.Reference));
        Assert.Equal(["RISK-EDGE"], second.Items.Select(t => t.Reference));
    }

    [Fact]
    public async Task SlaStatus_UsesTheConfiguredThreshold()
    {
        // At 50%, a 4 h ticket is at risk for its last 2 hours: WITHIN-EDGE (1 h + 1 ms left) joins.
        var policy = new SlaPolicy(new SlaSettings
        {
            BaseWindowHours = TestSla.Settings().BaseWindowHours,
            PremiumCustomerMultiplier = 0.5,
            MinimumWindowHours = 1,
            AtRiskThresholdPercent = 50
        });

        var result = await Search(new TicketQuery { SlaStatus = SlaStatus.AtRisk }, policy);

        Assert.Contains("WITHIN-EDGE", result.Items.Select(t => t.Reference));
        Assert.DoesNotContain("WITHIN-FRESH", result.Items.Select(t => t.Reference));
    }

    [Fact]
    public async Task Now_IsTakenToTheMillisecond_SoTheFilterAndTheRowsAgree()
    {
        // 0.4 ms after RISK-DUE-NOW's due time: compared at stored precision it is still due "now".
        var clock = new FixedClock(Now.AddTicks(4_000));

        var atRisk = await Search(new TicketQuery { SlaStatus = SlaStatus.AtRisk }, clock: clock);
        var all = await Search(new TicketQuery { PageSize = 100 }, clock: clock);

        Assert.Contains("RISK-DUE-NOW", atRisk.Items.Select(t => t.Reference));
        Assert.Equal(SlaStatus.AtRisk, all.Items.Single(t => t.Reference == "RISK-DUE-NOW").SlaStatus);
    }

    private async Task<PagedResult<TicketListItemDto>> Search(
        TicketQuery query, SlaPolicy? policy = null, FixedClock? clock = null)
    {
        await using var db = _database.CreateContext();
        var handler = new SearchTicketsQueryHandler(new TicketQueries(db, clock ?? new FixedClock(), policy ?? TestSla.Policy()));

        return await handler.HandleAsync(query, CancellationToken.None);
    }

    private void Seed()
    {
        using var db = _database.CreateContext();

        var category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        var customer = new Customer("Fabrikam Inc", "help@fabrikam.test", null, CustomerTier.Standard, Now);
        db.AddRange(category, customer);
        db.SaveChanges();

        Ticket Open(string reference, TicketPriority priority, DateTime due, TimeSpan window) =>
            Ticket.Raise(reference, $"Ticket {reference}", "Details.", customer.Id, category.Id, priority, due - window, window);

        Ticket Resolved(string reference, string title, DateTime due, DateTime resolvedAt)
        {
            var ticket = Ticket.Raise(reference, title, "Details.", customer.Id, category.Id, TicketPriority.Medium, due - FourHours, FourHours);
            ticket.ChangeStatus(TicketStatus.Resolved, resolvedAt);
            return ticket;
        }

        db.AddRange(
            Ticket.Raise("NO-SLA", "No due date", "Details.", customer.Id, category.Id, TicketPriority.Low, Now.AddHours(-1)),
            Open("WITHIN-FRESH", TicketPriority.Low, Now.AddHours(4), FourHours),
            Open("WITHIN-EDGE", TicketPriority.Medium, Now.AddHours(1).AddMilliseconds(1), FourHours),
            Open("RISK-EDGE", TicketPriority.High, Now.AddHours(1), FourHours),
            Open("RISK-DUE-NOW", TicketPriority.High, Now, FourHours),
            Open("BREACHED-OPEN", TicketPriority.High, Now.AddMilliseconds(-1), FourHours),
            // 25% of 1001 s truncates to a 250 s lead.
            Open("RISK-ODD", TicketPriority.Low, Now.AddSeconds(250), TimeSpan.FromSeconds(1001)),
            Open("WITHIN-ODD", TicketPriority.Low, Now.AddSeconds(251), TimeSpan.FromSeconds(1001)),
            Resolved("MET-EDGE", "Resolved exactly at the due time", Now.AddHours(-2), Now.AddHours(-2)),
            Resolved("MET-EARLY", "Resolved early", Now.AddHours(2), Now.AddHours(-1)),
            Resolved("BREACHED-LATE", "Resolved late", Now.AddHours(-2), Now.AddHours(-2).AddMilliseconds(1)));

        db.SaveChanges();
    }
}
