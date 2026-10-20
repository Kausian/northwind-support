namespace SupportDesk.Application.Abstractions;

/// <summary>
/// The signed-in agent making the current request, as their verified access token identifies
/// them. Never taken from the request body.
/// </summary>
public interface ICurrentUser
{
    /// <exception cref="Exceptions.UnauthorizedException">The request is not authenticated.</exception>
    int AgentId { get; }

    /// <exception cref="Exceptions.UnauthorizedException">The request is not authenticated.</exception>
    string Email { get; }
}
