using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.GetCurrentAgent;

/// <summary>
/// The signed-in agent (AU-5), read fresh from the database rather than from the token, so an
/// agent deactivated since signing in is refused here.
/// </summary>
public sealed class GetCurrentAgentQueryHandler(ICurrentUser currentUser, IAgentRepository agents)
{
    /// <exception cref="UnauthorizedException">The agent no longer exists or is inactive.</exception>
    public async Task<AgentProfileDto> HandleAsync(CancellationToken ct)
    {
        var agent = await agents.GetByIdAsync(currentUser.AgentId, ct);

        if (agent is null || !agent.IsActive)
        {
            throw new UnauthorizedException("Your account can no longer sign in.");
        }

        return new AgentProfileDto(agent.Id, agent.FullName, agent.Email);
    }
}
