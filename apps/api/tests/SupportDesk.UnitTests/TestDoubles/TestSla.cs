using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>The SLA settings the assignment specifies, as in appsettings.Development.json.</summary>
public static class TestSla
{
    public static SlaSettings Settings(
        double premiumMultiplier = 0.5,
        double minimumWindowHours = 1,
        double criticalHours = 4) => new()
        {
            BaseWindowHours = new Dictionary<TicketPriority, double>
            {
                [TicketPriority.Low] = 72,
                [TicketPriority.Medium] = 24,
                [TicketPriority.High] = 8,
                [TicketPriority.Critical] = criticalHours
            },
            PremiumCustomerMultiplier = premiumMultiplier,
            MinimumWindowHours = minimumWindowHours,
            AtRiskThresholdPercent = 25
        };

    public static SlaPolicy Policy() => new(Settings());
}
