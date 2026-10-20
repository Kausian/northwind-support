using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Features.Auth.GetCurrentAgent;
using SupportDesk.Application.Features.Auth.Login;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// Signs an agent in and returns their access token. The only endpoint open without one.
    /// 401 with the same message for an unknown email, a wrong password or an inactive agent.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public Task<LoginResponse> Login(
        LoginRequest request,
        [FromServices] LoginCommandHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(request, ct);

    /// <summary>The signed-in agent; 401 without a valid token.</summary>
    [HttpGet("me")]
    public Task<AgentProfileDto> Me([FromServices] GetCurrentAgentQueryHandler handler, CancellationToken ct) =>
        handler.HandleAsync(ct);
}
