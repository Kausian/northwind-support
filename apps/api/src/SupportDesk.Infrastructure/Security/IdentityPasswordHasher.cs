using Microsoft.AspNetCore.Identity;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Infrastructure.Security;

/// <summary>
/// AU-2 with ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>: PBKDF2 (HMAC-SHA512,
/// 100,000 iterations in the v3 format) with a random 128-bit salt per password, stored with
/// its algorithm and iteration count so the parameters can be raised later. Nothing here is
/// home-made; this class only adapts the vetted hasher to the application's interface.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    // The Identity hasher takes a user object for custom subclasses; the default ignores it.
    private static readonly object NoUser = new();

    private readonly PasswordHasher<object> _hasher = new();

    // A real hash of a random value, so a missing account is checked at full cost and never matches.
    private readonly Lazy<string> _unmatchableHash;

    public IdentityPasswordHasher()
    {
        _unmatchableHash = new Lazy<string>(() => Hash(Guid.NewGuid().ToString("N")));
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(NoUser, password);
    }

    public bool Verify(string? passwordHash, string password)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            _hasher.VerifyHashedPassword(NoUser, _unmatchableHash.Value, password ?? string.Empty);
            return false;
        }

        return _hasher.VerifyHashedPassword(NoUser, passwordHash, password ?? string.Empty)
            is not PasswordVerificationResult.Failed;
    }
}
