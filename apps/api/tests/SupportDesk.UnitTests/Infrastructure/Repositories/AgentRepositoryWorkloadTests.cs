using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Repositories;

/// <summary>
/// The workload query that assignment depends on, translated and run by a real database.
/// </summary>
public sealed class AgentRepositoryWorkloadTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task GetWorkloadsAsync_CountsOnlyOpenTicketsAndLoadsSpecializations()
    {
        var now = FixedClock.DefaultNow;
        int busyId, idleId, inactiveId, billingId, securityId;

        using (var db = _database.CreateContext())
        {
            var billing = new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false);
            var security = new Category("Security", requiresSpecialist: true, forcesCriticalPriority: true);
            var customer = new Customer("Fabrikam Inc", "help@fabrikam.test", null, CustomerTier.Standard, now);
            var busy = new Agent("Sara Lindqvist", "sara@northwind.test", 6, now);
            var idle = new Agent("Ben Osei", "ben@northwind.test", 8, now);
            var inactive = new Agent("Yuki Tanaka", "yuki@northwind.test", 8, now);
            inactive.Deactivate();

            db.AddRange(billing, security, customer, busy, idle, inactive);
            db.SaveChanges();

            busy.AddSpecialization(billing.Id);
            busy.AddSpecialization(security.Id);

            var statuses = new[]
            {
                TicketStatus.New, TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed
            };

            for (var i = 0; i < statuses.Length; i++)
            {
                var ticket = Ticket.Raise($"TCK-{i + 1:D4}", "A ticket", "Details.", customer.Id, billing.Id, TicketPriority.Medium, now);
                ticket.AssignTo(busy.Id, now);
                if (statuses[i] != TicketStatus.New)
                {
                    ticket.ChangeStatus(statuses[i], now);
                }

                db.Add(ticket);
            }

            // Unassigned tickets belong to nobody's workload.
            db.Add(Ticket.Raise("TCK-0099", "Unowned", "Details.", customer.Id, billing.Id, TicketPriority.Low, now));
            db.SaveChanges();

            (busyId, idleId, inactiveId, billingId, securityId) = (busy.Id, idle.Id, inactive.Id, billing.Id, security.Id);
        }

        await using var context = _database.CreateContext();
        var workloads = (await new AgentRepository(context).GetWorkloadsAsync(CancellationToken.None))
            .ToDictionary(w => w.AgentId);

        Assert.Equal(3, workloads.Count);

        var sara = workloads[busyId];
        Assert.Equal(3, sara.OpenTicketCount); // New, Open, InProgress - not Resolved or Closed.
        Assert.Equal(6, sara.MaxOpenTickets);
        Assert.True(sara.IsActive);
        Assert.Equal([billingId, securityId], sara.SpecializationCategoryIds.Order());

        Assert.Equal(0, workloads[idleId].OpenTicketCount);
        Assert.Empty(workloads[idleId].SpecializationCategoryIds);
        Assert.False(workloads[inactiveId].IsActive);
    }
}
