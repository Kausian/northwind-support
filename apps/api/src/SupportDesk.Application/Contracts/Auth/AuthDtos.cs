namespace SupportDesk.Application.Contracts.Auth;

/// <summary>Body of POST /api/auth/login.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>The signed-in agent: enough to greet them. Never includes credentials.</summary>
public sealed record AgentProfileDto(int Id, string FullName, string Email);

/// <summary>Response of POST /api/auth/login.</summary>
/// <param name="AccessToken">A signed JWT, sent back as <c>Authorization: Bearer ...</c>.</param>
/// <param name="ExpiresAtUtc">When the token stops being accepted; the client signs out then.</param>
/// <param name="Agent">Who signed in.</param>
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, AgentProfileDto Agent);
