using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Presentation.Authentication;

/// <summary>
/// Issues an HMAC-SHA256 signed JWT naming the agent: <c>sub</c> (agent id), <c>email</c> and
/// <c>name</c>, valid for <see cref="JwtOptions.AccessTokenMinutes"/> from "now" on the clock.
/// </summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, IClock clock) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var settings = options.Value;
        var now = clock.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Email, agent.Email),
                new Claim(JwtRegisteredClaimNames.Name, agent.FullName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ]),
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256)
        });

        return new AccessToken(token, expires);
    }
}
