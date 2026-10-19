using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Triage;

/// <summary>
/// BR-6: the SLA numbers, bound from the <c>Sla</c> configuration section so they can change
/// without touching code.
/// </summary>
public sealed class SlaSettings
{
    public const string SectionName = "Sla";

    /// <summary>BR-2: the response window per priority, in hours.</summary>
    public Dictionary<TicketPriority, double> BaseWindowHours { get; init; } = [];

    /// <summary>BR-3: what a Premium customer's window is multiplied by, e.g. 0.5 for half.</summary>
    public double PremiumCustomerMultiplier { get; init; }

    /// <summary>BR-3: the shortest a Premium-reduced window may become, in hours.</summary>
    public double MinimumWindowHours { get; init; }

    /// <summary>BR-8: an open ticket is at risk with this share of its window (or less) left.</summary>
    public double AtRiskThresholdPercent { get; init; }

    /// <summary>Everything wrong with these settings; empty when they are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            if (!BaseWindowHours.TryGetValue(priority, out var hours) || hours <= 0)
            {
                errors.Add($"{SectionName}:{nameof(BaseWindowHours)}:{priority} must be a positive number of hours.");
            }
        }

        if (PremiumCustomerMultiplier is <= 0 or > 1)
        {
            errors.Add($"{SectionName}:{nameof(PremiumCustomerMultiplier)} must be greater than 0 and at most 1.");
        }

        if (MinimumWindowHours <= 0)
        {
            errors.Add($"{SectionName}:{nameof(MinimumWindowHours)} must be a positive number of hours.");
        }

        if (AtRiskThresholdPercent is <= 0 or >= 100)
        {
            errors.Add($"{SectionName}:{nameof(AtRiskThresholdPercent)} must be between 0 and 100.");
        }

        return errors;
    }
}
