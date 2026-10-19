using System.Globalization;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Triage;

/// <summary>
/// BR-2 and BR-3: how long the team has to respond to a ticket. Shared by ticket creation and
/// escalation, which both work the due date out from "now".
/// </summary>
public sealed class SlaPolicy
{
    private readonly SlaSettings _settings;

    /// <exception cref="ArgumentException">The settings are incomplete or out of range.</exception>
    public SlaPolicy(SlaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }

        _settings = settings;
    }

    /// <summary>
    /// The base window for the priority; for a Premium customer, that multiplied by the premium
    /// multiplier, but never below the minimum.
    /// </summary>
    public SlaWindow WindowFor(TicketPriority priority, CustomerTier tier)
    {
        var baseHours = _settings.BaseWindowHours[priority];
        var reason = $"{priority} priority: {Hours(baseHours)} window";

        if (tier != CustomerTier.Premium)
        {
            return new SlaWindow(TimeSpan.FromHours(baseHours), $"{reason}.");
        }

        var premiumHours = baseHours * _settings.PremiumCustomerMultiplier;
        reason += $", reduced to {Hours(premiumHours)} for a Premium customer";

        if (premiumHours < _settings.MinimumWindowHours)
        {
            premiumHours = _settings.MinimumWindowHours;
            reason += $", raised to the {Hours(premiumHours)} minimum";
        }

        return new SlaWindow(TimeSpan.FromHours(premiumHours), $"{reason}.");
    }

    private static string Hours(double hours) => $"{hours.ToString("0.##", CultureInfo.InvariantCulture)} h";
}

/// <summary>A response window and how it was worked out.</summary>
public sealed record SlaWindow(TimeSpan Length, string Reason)
{
    /// <summary>When a ticket whose window starts at <paramref name="startUtc"/> is due.</summary>
    public DateTime DueFrom(DateTime startUtc) => startUtc.Add(Length);
}
