using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

/// <summary>
/// BR-7's input rules, checked before the ticket is loaded. The limits are the domain's own
/// constants, so the API, the aggregate and the database column cannot drift apart.
/// </summary>
public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(x => x.Reason)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A reason for the escalation is required.")
            .Must(reason => reason.Trim().Length is >= TicketEscalation.ReasonMinLength and <= TicketEscalation.ReasonMaxLength)
            .WithMessage($"The reason must be between {TicketEscalation.ReasonMinLength} and {TicketEscalation.ReasonMaxLength} characters.");

        RuleFor(x => x.EscalatedBy)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Who is escalating the ticket (escalatedBy) is required.")
            .Must(actor => actor.Trim().Length <= TicketEscalation.EscalatedByMaxLength)
            .WithMessage($"escalatedBy must be at most {TicketEscalation.EscalatedByMaxLength} characters.");
    }
}
