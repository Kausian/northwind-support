using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

/// <summary>The 400 rules for POST /api/tickets/{id}/escalate.</summary>
public class EscalateTicketRequestValidatorTests
{
    private readonly EscalateTicketRequestValidator _validator = new();

    [Theory]
    [InlineData("abcde")]
    [InlineData("  abcde  ")]
    [InlineData("The customer's whole team is blocked")]
    public void AcceptsAReasonOfFiveCharactersOrMore(string reason) =>
        Assert.True(_validator.Validate(new EscalateTicketRequest(reason, "sara")).IsValid);

    [Fact]
    public void AcceptsAReasonOfExactlyFiveHundredCharacters() =>
        Assert.True(_validator.Validate(new EscalateTicketRequest(new string('x', 500), "sara")).IsValid);

    [Theory]
    [InlineData(null, "A reason for the escalation is required.")]
    [InlineData("", "A reason for the escalation is required.")]
    [InlineData("    ", "A reason for the escalation is required.")]
    [InlineData("abcd", "The reason must be between 5 and 500 characters.")]
    [InlineData("  abcd   ", "The reason must be between 5 and 500 characters.")]
    public void RejectsAMissingOrShortReason(string? reason, string message)
    {
        var result = _validator.Validate(new EscalateTicketRequest(reason!, "sara"));

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(EscalateTicketRequest.Reason), error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }

    [Fact]
    public void RejectsAReasonOverFiveHundredCharacters()
    {
        var result = _validator.Validate(new EscalateTicketRequest(new string('x', 501), "sara"));

        Assert.Equal("The reason must be between 5 and 500 characters.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsAMissingActor(string? actor)
    {
        var result = _validator.Validate(new EscalateTicketRequest("Customer is blocked", actor!));

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(EscalateTicketRequest.EscalatedBy), error.PropertyName);
    }

    [Fact]
    public void RejectsAnActorOverOneHundredCharacters() =>
        Assert.False(_validator.Validate(new EscalateTicketRequest("Customer is blocked", new string('a', 101))).IsValid);
}
