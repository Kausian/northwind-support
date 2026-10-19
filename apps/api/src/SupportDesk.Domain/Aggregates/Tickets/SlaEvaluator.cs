namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// BR-8: where a ticket stands against its due date. SLA status is always derived from the
/// ticket's dates, the length of its window and the current time - never stored.
/// </summary>
/// <remarks>
/// The ticket list filters on SLA status in SQL with the same comparisons (see
/// <c>TicketQueries</c>); <see cref="AtRiskLeadSeconds"/> is the one piece of arithmetic both share.
/// </remarks>
public static class SlaEvaluator
{
    /// <param name="dueAtUtc">When the team must respond by; null when no SLA applies.</param>
    /// <param name="slaWindowSeconds">How long the window that produced the due date was.</param>
    /// <param name="resolvedAtUtc">When the ticket was resolved; null while it is unresolved.</param>
    /// <param name="nowUtc">The current time, from <c>IClock</c>.</param>
    /// <param name="atRiskFraction">The share of the window (0-1) at or below which an open ticket is at risk.</param>
    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        int? slaWindowSeconds,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        double atRiskFraction)
    {
        if (dueAtUtc is not { } due)
        {
            return SlaStatus.NotApplicable;
        }

        if (resolvedAtUtc is { } resolved)
        {
            return resolved <= due ? SlaStatus.Met : SlaStatus.Breached;
        }

        if (nowUtc > due)
        {
            return SlaStatus.Breached;
        }

        // At risk once the time left is at most the at-risk share of the window, up to and
        // including the due moment itself.
        return slaWindowSeconds is { } window && due <= nowUtc.AddSeconds(AtRiskLeadSeconds(window, atRiskFraction))
            ? SlaStatus.AtRisk
            : SlaStatus.WithinSla;
    }

    /// <summary>
    /// How many whole seconds before the due date a ticket becomes at risk. Truncated to whole
    /// seconds the same way SQL's <c>CAST(... AS int)</c> truncates, so C# and the database agree.
    /// </summary>
    public static int AtRiskLeadSeconds(int slaWindowSeconds, double atRiskFraction) =>
        (int)(slaWindowSeconds * atRiskFraction);
}
