namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Turns passwords into salted, deliberately slow hashes and checks passwords against them.
/// Implemented with a vetted algorithm in Infrastructure; nothing else handles hashing.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// True when <paramref name="password"/> matches <paramref name="passwordHash"/>. With no hash
    /// it still does the full amount of work and returns false, so an unknown account takes as
    /// long to reject as a wrong password.
    /// </summary>
    bool Verify(string? passwordHash, string password);
}
