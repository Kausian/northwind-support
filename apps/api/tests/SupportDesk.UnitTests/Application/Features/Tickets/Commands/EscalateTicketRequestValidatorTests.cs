using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

/// <summary>
/// The 400 rules for POST /api/tickets/{id}/escalate. The actor is not part of the request any
/// more - it comes from the token (see the API tests).
/// </summary>
public class EscalateTicketRequestValidatorTests
{
    private readonly EscalateTicketRequestValidator _validator = new();

    [Theory]
    [InlineData("abcde")]
    [InlineData("  abcde  ")]
    [InlineData("The customer's whole team is blocked")]
    public void AcceptsAReasonOfFiveCharactersOrMore(string reason) =>
        Assert.True(_validator.Validate(new EscalateTicketRequest(reason)).IsValid);

    [Fact]
    public void AcceptsAReasonOfExactlyFiveHundredCharacters() =>
        Assert.True(_validator.Validate(new EscalateTicketRequest(new string('x', 500))).IsValid);

    [Theory]
    [InlineData(null, "A reason for the escalation is required.")]
    [InlineData("", "A reason for the escalation is required.")]
    [InlineData("    ", "A reason for the escalation is required.")]
    [InlineData("abcd", "The reason must be between 5 and 500 characters.")]
    [InlineData("  abcd   ", "The reason must be between 5 and 500 characters.")]
    public void RejectsAMissingOrShortReason(string? reason, string message)
    {
        var result = _validator.Validate(new EscalateTicketRequest(reason!));

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(EscalateTicketRequest.Reason), error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }

    [Fact]
    public void RejectsAReasonOverFiveHundredCharacters()
    {
        var result = _validator.Validate(new EscalateTicketRequest(new string('x', 501)));

        Assert.Equal("The reason must be between 5 and 500 characters.", Assert.Single(result.Errors).ErrorMessage);
    }
}
