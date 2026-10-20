using System.Globalization;
using Microsoft.IdentityModel.JsonWebTokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Presentation.Authentication;

/// <summary>
/// Reads the signed-in agent from the request's validated token claims. Only the JWT bearer
/// handler puts claims there, after checking the signature - the request body cannot.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public int AgentId =>
        int.TryParse(Claim(JwtRegisteredClaimNames.Sub), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw NotSignedIn();

    public string Email => Claim(JwtRegisteredClaimNames.Email) ?? throw NotSignedIn();

    private string? Claim(string type)
    {
        var user = httpContextAccessor.HttpContext?.User;

        return user?.Identity?.IsAuthenticated == true ? user.FindFirst(type)?.Value : null;
    }

    private static UnauthorizedException NotSignedIn() => new("Sign in to continue.");
}
