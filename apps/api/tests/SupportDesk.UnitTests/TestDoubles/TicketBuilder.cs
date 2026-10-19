using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// Builds tickets for tests, so each test only has to say what actually matters to it. The
/// ticket is driven into the requested state through its own behaviour, so a test can never
/// start from a state the domain would not allow.
/// </summary>
public sealed class TicketBuilder
{
    private string _reference = "TCK-0001";
    private TicketPriority _priority = TicketPriority.Medium;
    private TicketStatus _status = TicketStatus.New;
    private int? _assignedAgentId;
    private DateTime? _resolvedAtUtc;
    private TimeSpan? _slaWindow;

    public TicketBuilder WithReference(string reference)
    {
        _reference = reference;
        return this;
    }

    public TicketBuilder WithPriority(TicketPriority priority)
    {
        _priority = priority;
        return this;
    }

    public TicketBuilder WithStatus(TicketStatus status)
    {
        _status = status;
        return this;
    }

    public TicketBuilder AssignedTo(int? agentId)
    {
        _assignedAgentId = agentId;
        return this;
    }

    /// <summary>Gives the ticket a due date this long after it was raised.</summary>
    public TicketBuilder WithSlaWindow(TimeSpan window)
    {
        _slaWindow = window;
        return this;
    }

    /// <summary>When the ticket reached its status; for Resolved and Closed, the resolution time.</summary>
    public TicketBuilder ResolvedAt(DateTime? resolvedAtUtc)
    {
        _resolvedAtUtc = resolvedAtUtc;
        return this;
    }

    public Ticket Build()
    {
        var ticket = Ticket.Raise(
            _reference,
            "Something is not working",
            "A description long enough to be realistic.",
            customerId: 1,
            categoryId: 1,
            _priority,
            FixedClock.DefaultNow,
            _slaWindow);

        if (_assignedAgentId is not null)
        {
            ticket.AssignTo(_assignedAgentId, FixedClock.DefaultNow);
        }

        if (_status != TicketStatus.New)
        {
            ticket.ChangeStatus(_status, _resolvedAtUtc ?? FixedClock.DefaultNow);
        }

        return ticket;
    }
}
