using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Queries.SearchTickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

/// <summary>
/// The ticket list filters, run through the search handler and translated to SQL by EF Core, so
/// these prove the database does the filtering, counting and paging - not C#.
/// </summary>
public sealed class TicketQueriesFilteringTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Ids _ids;

    public TicketQueriesFilteringTests()
    {
        _ids = Seed();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task NoFilters_ReturnsEveryTicket()
    {
        var result = await Search(new TicketQuery());

        Assert.Equal(8, result.TotalCount);
    }

    [Theory]
    [InlineData(TicketPriority.High, new[] { "TCK-0002", "TCK-0003", "TCK-0004" })]
    [InlineData(TicketPriority.Low, new[] { "TCK-0001" })]
    [InlineData(TicketPriority.Critical, new[] { "TCK-0007" })]
    public async Task Priority_IsAnExactMatch(TicketPriority priority, string[] expected)
    {
        var result = await Search(new TicketQuery { Priority = priority });

        AssertReferences(expected, result);
    }

    [Fact]
    public async Task Status_IsAnExactMatch()
    {
        var result = await Search(new TicketQuery { Status = TicketStatus.InProgress });

        AssertReferences(["TCK-0002", "TCK-0004"], result);
    }

    [Fact]
    public async Task Category_IsAnExactMatch()
    {
        var result = await Search(new TicketQuery { CategoryId = _ids.Billing });

        AssertReferences(["TCK-0002", "TCK-0004", "TCK-0006"], result);
    }

    [Fact]
    public async Task Customer_IsAnExactMatch()
    {
        var result = await Search(new TicketQuery { CustomerId = _ids.Fabrikam });

        AssertReferences(["TCK-0002", "TCK-0003", "TCK-0006"], result);
    }

    [Fact]
    public async Task AssignedAgent_IsAnExactMatch()
    {
        var result = await Search(new TicketQuery { AssignedAgentId = _ids.Sara });

        AssertReferences(["TCK-0002", "TCK-0005"], result);
    }

    [Fact]
    public async Task UnassignedOnly_ReturnsOnlyTicketsWithoutAnAgent()
    {
        var result = await Search(new TicketQuery { UnassignedOnly = true });

        AssertReferences(["TCK-0001", "TCK-0004", "TCK-0006", "TCK-0008"], result);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
    }

    [Fact]
    public async Task UnassignedOnlyFalse_DoesNotFilter()
    {
        var result = await Search(new TicketQuery { UnassignedOnly = false });

        Assert.Equal(8, result.TotalCount);
    }

    [Fact]
    public async Task Filters_CombineWithAnd()
    {
        var result = await Search(new TicketQuery
        {
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.High,
            CategoryId = _ids.Billing,
            UnassignedOnly = true
        });

        AssertReferences(["TCK-0004"], result);
    }

    [Fact]
    public async Task Filters_CombineWithSearch()
    {
        var result = await Search(new TicketQuery { CustomerId = _ids.Fabrikam, Priority = TicketPriority.High, Search = "invoice" });

        AssertReferences(["TCK-0002"], result);
    }

    [Fact]
    public async Task Filters_ThatContradictEachOther_ReturnNothing()
    {
        var result = await Search(new TicketQuery { AssignedAgentId = _ids.Sara, UnassignedOnly = true });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Search_MatchesCustomerName_IgnoringCase()
    {
        var result = await Search(new TicketQuery { Search = "fabrikam" });

        AssertReferences(["TCK-0002", "TCK-0003", "TCK-0006"], result);
    }

    [Fact]
    public async Task Search_MatchesTitle_IgnoringCase()
    {
        var result = await Search(new TicketQuery { Search = "INVOICE" });

        AssertReferences(["TCK-0002"], result);
    }

    [Fact]
    public async Task Search_MatchesReference()
    {
        var result = await Search(new TicketQuery { Search = "tck-0003" });

        AssertReferences(["TCK-0003"], result);
    }

    [Fact]
    public async Task Search_IsTrimmed()
    {
        var result = await Search(new TicketQuery { Search = "   invoice \t" });

        AssertReferences(["TCK-0002"], result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Search_WhenBlank_IsIgnored(string? search)
    {
        var result = await Search(new TicketQuery { Search = search });

        Assert.Equal(8, result.TotalCount);
    }

    [Theory]
    [InlineData("100%", "TCK-0004")] // '%' would otherwise also match "100 tickets".
    [InlineData("user_id", "TCK-0006")] // '_' would otherwise also match "userXid".
    [InlineData("[urgent]", "TCK-0007")] // '[...]' is a character set in SQL Server's LIKE.
    public async Task Search_TreatsLikeWildcardsAsPlainText(string search, string expected)
    {
        var result = await Search(new TicketQuery { Search = search });

        AssertReferences([expected], result);
    }

    [Fact]
    public async Task Paging_CountsAndPagesTheFilteredSet()
    {
        var query = new TicketQuery { Priority = TicketPriority.High, PageSize = 2, SortDirection = "asc" };

        var first = await Search(query);
        var second = await Search(query with { Page = 2 });

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(["TCK-0002", "TCK-0003"], first.Items.Select(t => t.Reference));

        Assert.Equal(3, second.TotalCount);
        Assert.Equal(["TCK-0004"], second.Items.Select(t => t.Reference));
    }

    private async Task<PagedResult<TicketListItemDto>> Search(TicketQuery query)
    {
        await using var db = _database.CreateContext();
        var handler = new SearchTicketsQueryHandler(new TicketQueries(db, new FixedClock()));

        return await handler.HandleAsync(query, CancellationToken.None);
    }

    private static void AssertReferences(string[] expected, PagedResult<TicketListItemDto> result)
    {
        Assert.Equal(expected.Order(), result.Items.Select(t => t.Reference).Order());
        Assert.Equal(expected.Length, result.TotalCount);
    }

    /// <summary>
    /// Eight tickets spread so that every filter keeps some and drops some. Created an hour
    /// apart in reference order, so "oldest first" is reference order.
    /// </summary>
    private Ids Seed()
    {
        using var db = _database.CreateContext();
        var now = FixedClock.DefaultNow;

        var general = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        var billing = new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false);
        var contoso = new Customer("Contoso Ltd", "it@contoso.test", null, CustomerTier.Premium, now);
        var fabrikam = new Customer("Fabrikam Inc", "help@fabrikam.test", null, CustomerTier.Standard, now);
        var tailspin = new Customer("Tailspin Toys", "ops@tailspin.test", null, CustomerTier.Standard, now);
        var sara = new Agent("Sara Lindqvist", "sara@northwind.test", 6, now);
        var ben = new Agent("Ben Osei", "ben@northwind.test", 8, now);

        db.AddRange(general, billing, contoso, fabrikam, tailspin, sara, ben);
        db.SaveChanges();

        var tickets = new[]
        {
            NewTicket("TCK-0001", "Printer jams on every page", contoso, general, TicketPriority.Low, TicketStatus.New, null),
            NewTicket("TCK-0002", "Invoice total is wrong", fabrikam, billing, TicketPriority.High, TicketStatus.InProgress, sara),
            NewTicket("TCK-0003", "Cannot sign in", fabrikam, general, TicketPriority.High, TicketStatus.Open, ben),
            NewTicket("TCK-0004", "Refund 100% of the order", tailspin, billing, TicketPriority.High, TicketStatus.InProgress, null),
            NewTicket("TCK-0005", "Password reset email missing", contoso, general, TicketPriority.Medium, TicketStatus.Resolved, sara),
            NewTicket("TCK-0006", "Export drops the user_id column", fabrikam, billing, TicketPriority.Medium, TicketStatus.New, null),
            NewTicket("TCK-0007", "[Urgent] Export of 100 tickets fails for userXid", tailspin, general, TicketPriority.Critical, TicketStatus.Open, ben),
            NewTicket("TCK-0008", "Dashboard is slow", tailspin, general, TicketPriority.Medium, TicketStatus.Closed, null)
        };

        db.AddRange(tickets);
        db.SaveChanges();

        return new Ids(billing.Id, fabrikam.Id, sara.Id);

        Ticket NewTicket(
            string reference, string title, Customer customer, Category category,
            TicketPriority priority, TicketStatus status, Agent? agent)
        {
            var createdAt = now.AddHours(int.Parse(reference[^4..], System.Globalization.CultureInfo.InvariantCulture));
            var ticket = Ticket.Raise(
                reference, title, "Details.", customer.Id, category.Id, priority, createdAt);

            ticket.AssignTo(agent?.Id, createdAt);

            if (status != TicketStatus.New)
            {
                ticket.ChangeStatus(status, createdAt);
            }

            return ticket;
        }
    }

    private sealed record Ids(int Billing, int Fabrikam, int Sara);
}
