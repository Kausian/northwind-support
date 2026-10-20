using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Login;

/// <summary>
/// Signs an agent in with email and password (AU-4) and issues their access token.
/// </summary>
public sealed class LoginCommandHandler(
    IAgentRepository agents,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer tokenIssuer,
    ILogger<LoginCommandHandler> logger)
{
    /// <summary>
    /// The one message for every failed sign-in, so a caller cannot tell an unknown email from a
    /// wrong password or an inactive account.
    /// </summary>
    public const string InvalidCredentialsMessage = "Email or password is incorrect.";

    /// <exception cref="UnauthorizedException">Unknown email, wrong password or inactive agent.</exception>
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var agent = await agents.GetByEmailAsync(request.Email.Trim(), ct);

        // Verified even when there is no such agent, so every failure costs the same time.
        var passwordMatches = passwordHasher.Verify(agent?.PasswordHash, request.Password);

        if (agent is null || !passwordMatches || !agent.IsActive)
        {
            var reason = agent is null ? "unknown email" : !passwordMatches ? "wrong password" : "inactive agent";
            logger.LogInformation("Sign-in refused: {Reason}.", reason);

            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var token = tokenIssuer.Issue(agent);

        logger.LogInformation("Agent {AgentId} signed in.", agent.Id);

        return new LoginResponse(token.Token, token.ExpiresAtUtc, new AgentProfileDto(agent.Id, agent.FullName, agent.Email));
    }
}
