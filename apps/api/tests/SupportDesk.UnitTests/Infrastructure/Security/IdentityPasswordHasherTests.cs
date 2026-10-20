using SupportDesk.Infrastructure.Security;

namespace SupportDesk.UnitTests.Infrastructure.Security;

/// <summary>AU-2: hashed, salted, verifiable - and never the password itself.</summary>
public class IdentityPasswordHasherTests
{
    private const string Password = "Correct-Horse-Battery-1";

    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotContainThePassword()
    {
        var hash = _hasher.Hash(Password);

        Assert.DoesNotContain(Password, hash, StringComparison.Ordinal);
        Assert.NotEqual(Password, hash);
    }

    [Fact]
    public void Hash_IsSalted_SoTheSamePasswordHashesDifferentlyEachTime() =>
        Assert.NotEqual(_hasher.Hash(Password), _hasher.Hash(Password));

    [Fact]
    public void Hash_UsesIdentitysVersionedPbkdf2Format()
    {
        var bytes = Convert.FromBase64String(_hasher.Hash(Password));

        Assert.Equal(0x01, bytes[0]); // Identity v3: PBKDF2 with the parameters stored in the hash.
        Assert.Equal(61, bytes.Length); // marker + PRF + iterations + salt length + 16-byte salt + 32-byte subkey
    }

    [Fact]
    public void Verify_AcceptsTheRightPassword() =>
        Assert.True(_hasher.Verify(_hasher.Hash(Password), Password));

    [Theory]
    [InlineData("correct-horse-battery-1")]
    [InlineData("Correct-Horse-Battery-")]
    [InlineData("")]
    public void Verify_RejectsAnyOtherPassword(string attempt) =>
        Assert.False(_hasher.Verify(_hasher.Hash(Password), attempt));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_WithoutAHash_RejectsEvenAnEmptyPassword(string? hash)
    {
        Assert.False(_hasher.Verify(hash, Password));
        Assert.False(_hasher.Verify(hash, string.Empty));
    }
}
