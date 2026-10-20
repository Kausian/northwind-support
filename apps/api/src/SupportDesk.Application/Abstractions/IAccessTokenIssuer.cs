using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

/// <summary>Issues the signed access token an agent presents on every request after signing in.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(Agent agent);
}

/// <param name="Token">The encoded token, sent as <c>Authorization: Bearer ...</c>.</param>
/// <param name="ExpiresAtUtc">When the API stops accepting it.</param>
public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);
