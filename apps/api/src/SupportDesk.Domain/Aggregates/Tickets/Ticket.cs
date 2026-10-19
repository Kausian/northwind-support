using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A support request raised by a customer. Every change to a ticket goes through one of its
/// methods, so its rules cannot be bypassed.
/// </summary>
/// <remarks>
/// The customer, the category and the assigned agent are other aggregates and are referenced
/// by id only.
/// </remarks>
public sealed class Ticket : AggregateRoot
{
    private Ticket()
    {
        // For EF Core.
    }

    /// <summary>Human-friendly identifier shown to customers, e.g. TCK-0042.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    /// <summary>Null while the ticket is waiting for an owner.</summary>
    public int? AssignedAgentId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the team has committed to responding by. Null when no SLA applies.</summary>
    public DateTime? DueAtUtc { get; private set; }

    /// <summary>
    /// How long the window that produced <see cref="DueAtUtc"/> was, in whole seconds; null
    /// exactly when there is no due date. BR-8 needs it to tell when a ticket becomes at risk,
    /// because after an escalation the window no longer starts at <see cref="CreatedAtUtc"/>.
    /// </summary>
    public int? SlaWindowSeconds { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>True while the ticket still needs work from an agent.</summary>
    public bool IsOpen => Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    /// <summary>
    /// Raises a new ticket for a customer. It starts as <see cref="TicketStatus.New"/>, due one
    /// <paramref name="slaWindow"/> from now and owned as triage decided; without a window or an
    /// owner it has no due date or no owner.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The window is not a positive whole number of seconds.</exception>
    public static Ticket Raise(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        TicketPriority priority,
        DateTime nowUtc,
        TimeSpan? slaWindow = null,
        int? assignedAgentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var ticket = new Ticket
        {
            Reference = reference,
            Title = title.Trim(),
            Description = description.Trim(),
            CustomerId = customerId,
            CategoryId = categoryId,
            Priority = priority,
            Status = TicketStatus.New,
            AssignedAgentId = assignedAgentId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        if (slaWindow is { } window)
        {
            ticket.StartSlaWindow(window, nowUtc);
        }

        return ticket;
    }

    /// <summary>
    /// BR-7: refuses an escalation the rules do not allow. Callers check this before working out
    /// the new due date and owner, which depend on the new priority.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is Resolved, Closed or already Critical.</exception>
    public void EnsureCanBeEscalated()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is {Status} and cannot be escalated.");
        }

        if (Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is already Critical and cannot be escalated further.");
        }
    }

    /// <summary>
    /// BR-7: raises the priority one level, restarts the SLA window from <paramref name="nowUtc"/>
    /// and hands the ticket to <paramref name="agentId"/> (which may be the current owner, or
    /// nobody). Returns the history record of the change, to be saved with it.
    /// </summary>
    /// <param name="slaWindow">The window for the new priority, from the SLA policy.</param>
    /// <exception cref="BusinessRuleViolationException">The ticket cannot be escalated.</exception>
    /// <exception cref="ArgumentException">The reason or the actor is missing or too long.</exception>
    public TicketEscalation Escalate(
        TimeSpan slaWindow,
        int? agentId,
        string reason,
        string escalatedBy,
        DateTime nowUtc)
    {
        EnsureCanBeEscalated();

        ArgumentNullException.ThrowIfNull(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalatedBy);

        reason = reason.Trim();
        escalatedBy = escalatedBy.Trim();

        ArgumentOutOfRangeException.ThrowIfLessThan(reason.Length, TicketEscalation.ReasonMinLength, nameof(reason));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reason.Length, TicketEscalation.ReasonMaxLength, nameof(reason));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(escalatedBy.Length, TicketEscalation.EscalatedByMaxLength, nameof(escalatedBy));

        var (fromPriority, fromAgentId, fromDueAtUtc) = (Priority, AssignedAgentId, DueAtUtc);

        Priority = Priority.OneLevelUp();
        AssignedAgentId = agentId;
        StartSlaWindow(slaWindow, nowUtc);
        UpdatedAtUtc = nowUtc;

        return new TicketEscalation(
            Id, fromPriority, Priority, fromAgentId, AssignedAgentId, fromDueAtUtc, DueAtUtc!.Value,
            reason, escalatedBy, nowUtc);
    }

    /// <summary>
    /// Moves the ticket through its lifecycle, keeping the resolution timestamp in step.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed.</exception>
    public void ChangeStatus(TicketStatus status, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is closed and cannot be moved to {status}.");
        }

        ResolvedAtUtc = status switch
        {
            TicketStatus.Resolved => nowUtc,
            TicketStatus.Closed => ResolvedAtUtc ?? nowUtc,
            _ => null
        };

        Status = status;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Hands the ticket to an agent, or takes it back off them when <paramref name="agentId"/>
    /// is null. Whether the agent may take it is checked by the caller, which can see agents.
    /// </summary>
    public void AssignTo(int? agentId, DateTime nowUtc)
    {
        AssignedAgentId = agentId;
        UpdatedAtUtc = nowUtc;
    }

    private void StartSlaWindow(TimeSpan window, DateTime startUtc)
    {
        // Whole seconds, so the stored window and the due date always describe the same span.
        if (window <= TimeSpan.Zero || window.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "An SLA window must be a positive whole number of seconds.");
        }

        SlaWindowSeconds = checked((int)window.TotalSeconds);
        DueAtUtc = startUtc.Add(window);
    }
}
