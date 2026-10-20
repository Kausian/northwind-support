using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SupportDesk.ApiTests;

/// <summary>JSON the way the API writes it: camelCase, enums as names.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// Hand-made tokens for the cases the login endpoint will never produce: expired, signed with
/// another key, from another issuer or for another audience.
/// </summary>
public static class Tokens
{
    public static string Create(
        string key = ApiFactory.SigningKey,
        string issuer = ApiFactory.Issuer,
        string audience = ApiFactory.Audience,
        DateTime? expiresUtc = null,
        string subject = "1",
        string email = ApiFactory.SaraEmail)
    {
        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(30);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expires.AddHours(-1),
            NotBefore = expires.AddHours(-1),
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Name, "Sara Lindqvist")
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        });
    }

    /// <summary>A token with no signature at all ("alg": "none").</summary>
    public static string Unsigned()
    {
        static string Encode(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));

        var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        return Encode("""{"alg":"none","typ":"JWT"}""") + "." +
            Encode($$"""{"sub":"1","email":"{{ApiFactory.SaraEmail}}","iss":"{{ApiFactory.Issuer}}","aud":"{{ApiFactory.Audience}}","exp":{{expires}}}""") + ".";
    }
}
