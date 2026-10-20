using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth.Login;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Security;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Auth;

/// <summary>AU-4: one generic failure for every reason, and a token only for an active agent.</summary>
public class LoginCommandHandlerTests
{
    private const string Email = "sara@northwind.test";
    private const string Password = "Correct-Horse-1";

    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IAccessTokenIssuer> _tokens = new();
    private readonly IdentityPasswordHasher _hasher = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _tokens.Setup(t => t.Issue(It.IsAny<Agent>()))
            .Returns(new AccessToken("signed.jwt.token", FixedClock.DefaultNow.AddHours(1)));

        _handler = new LoginCommandHandler(_agents.Object, _hasher, _tokens.Object, NullLogger<LoginCommandHandler>.Instance);
    }

    private Agent ExistingAgent(bool active = true, bool withCredentials = true)
    {
        var agent = new Agent("Sara Lindqvist", Email, 6, FixedClock.DefaultNow).WithId(4);
        if (withCredentials)
        {
            agent.SetPasswordHash(_hasher.Hash(Password));
        }

        if (!active)
        {
            agent.Deactivate();
        }

        _agents.Setup(a => a.GetByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        return agent;
    }

    [Fact]
    public async Task HandleAsync_WithTheRightPassword_IssuesATokenForTheAgent()
    {
        var agent = ExistingAgent();

        var response = await _handler.HandleAsync(new LoginRequest($"  {Email} ", Password), CancellationToken.None);

        Assert.Equal("signed.jwt.token", response.AccessToken);
        Assert.Equal(FixedClock.DefaultNow.AddHours(1), response.ExpiresAtUtc);
        Assert.Equal(new AgentProfileDto(4, "Sara Lindqvist", Email), response.Agent);
        _tokens.Verify(t => t.Issue(agent), Times.Once());
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_WrongPassword_InactiveAndNoCredential_AllFailIdentically()
    {
        var messages = new List<string>();

        // Unknown email.
        messages.Add((await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(new LoginRequest("nobody@northwind.test", Password), CancellationToken.None))).Message);

        // Wrong password.
        ExistingAgent();
        messages.Add((await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(new LoginRequest(Email, "wrong-password"), CancellationToken.None))).Message);

        // Right password, inactive agent.
        ExistingAgent(active: false);
        messages.Add((await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(new LoginRequest(Email, Password), CancellationToken.None))).Message);

        // An agent who was never given a credential.
        ExistingAgent(withCredentials: false);
        messages.Add((await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(new LoginRequest(Email, Password), CancellationToken.None))).Message);

        Assert.All(messages, message => Assert.Equal(LoginCommandHandler.InvalidCredentialsMessage, message));
        _tokens.Verify(t => t.Issue(It.IsAny<Agent>()), Times.Never());
    }
}
