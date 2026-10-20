using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SupportDesk.Presentation.Authentication;

/// <summary>
/// The <c>Jwt</c> configuration section. Issuer, audience and lifetimes are ordinary settings in
/// appsettings.json; the signing key is a secret and comes only from user-secrets or the
/// <c>Jwt__SigningKey</c> environment variable (AU-7) - never from a committed file.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 needs a key of at least 256 bits.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    /// <summary>How long a token is accepted after sign-in.</summary>
    public int AccessTokenMinutes { get; init; }

    /// <summary>Tolerance for clock differences when checking expiry; small, so expiry means expiry.</summary>
    public int ClockSkewSeconds { get; init; }

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            errors.Add($"{SectionName}:{nameof(Issuer)} is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            errors.Add($"{SectionName}:{nameof(Audience)} is required.");
        }

        if (Encoding.UTF8.GetByteCount(SigningKey) < MinimumSigningKeyBytes)
        {
            errors.Add(
                $"{SectionName}:{nameof(SigningKey)} must be at least {MinimumSigningKeyBytes} bytes. Set it with " +
                "'dotnet user-secrets set Jwt:SigningKey <key>' or the Jwt__SigningKey environment variable (see README).");
        }

        if (AccessTokenMinutes <= 0)
        {
            errors.Add($"{SectionName}:{nameof(AccessTokenMinutes)} must be positive.");
        }

        if (ClockSkewSeconds < 0)
        {
            errors.Add($"{SectionName}:{nameof(ClockSkewSeconds)} cannot be negative.");
        }

        return errors;
    }

    /// <summary>Reports <see cref="Validate"/> through the options pipeline, so a bad key stops startup.</summary>
    public sealed class Validator : IValidateOptions<JwtOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var errors = options.Validate();
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
