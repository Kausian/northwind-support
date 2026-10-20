using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Security;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure;

/// <summary>AU-3: credentials hashed at seed time, and seeding that is safe to repeat.</summary>
public sealed class DevelopmentCredentialSeedingTests : IDisposable
{
    private const string DevPassword = "Local-Dev-Only-1!";

    private readonly SqliteTestDatabase _database = new();
    private readonly IdentityPasswordHasher _hasher = new();

    public void Dispose() => _database.Dispose();

    private async Task SeedCredentialsAsync()
    {
        await using var db = _database.CreateContext();
        await new SupportDbSeeder(db, new FixedClock(), _hasher, NullLogger<SupportDbSeeder>.Instance)
            .SeedDevelopmentCredentialsAsync(DevPassword);
    }

    private async Task<List<Agent>> AgentsAsync()
    {
        await using var db = _database.CreateContext();
        return await db.Set<Agent>().AsNoTracking().OrderBy(a => a.Email).ToListAsync();
    }

    [Fact]
    public async Task GivesEveryAgentAHashOfTheDevelopmentPassword_IncludingInactiveOnes()
    {
        await using (var db = _database.CreateContext())
        {
            var inactive = new Agent("Yuki Tanaka", "yuki@northwind.test", 8, FixedClock.DefaultNow);
            inactive.Deactivate();
            db.AddRange(new Agent("Sara Lindqvist", "sara@northwind.test", 6, FixedClock.DefaultNow), inactive);
            await db.SaveChangesAsync();
        }

        await SeedCredentialsAsync();

        var agents = await AgentsAsync();
        Assert.All(agents, agent =>
        {
            Assert.NotNull(agent.PasswordHash);
            Assert.NotEqual(DevPassword, agent.PasswordHash);
            Assert.True(_hasher.Verify(agent.PasswordHash, DevPassword));
        });

        // Salted per agent: the same password never produces the same stored value.
        Assert.NotEqual(agents[0].PasswordHash, agents[1].PasswordHash);
    }

    [Fact]
    public async Task RunningAgain_KeepsExistingHashes()
    {
        await using (var db = _database.CreateContext())
        {
            db.Add(new Agent("Sara Lindqvist", "sara@northwind.test", 6, FixedClock.DefaultNow));
            await db.SaveChangesAsync();
        }

        await SeedCredentialsAsync();
        var first = (await AgentsAsync()).Single().PasswordHash;

        await SeedCredentialsAsync();
        var second = (await AgentsAsync()).Single().PasswordHash;

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task NeverOverwritesAPasswordThatWasAlreadySet()
    {
        await using (var db = _database.CreateContext())
        {
            var agent = new Agent("Sara Lindqvist", "sara@northwind.test", 6, FixedClock.DefaultNow);
            agent.SetPasswordHash(_hasher.Hash("Her-Own-Password-9"));
            db.Add(agent);
            await db.SaveChangesAsync();
        }

        await SeedCredentialsAsync();

        var hash = (await AgentsAsync()).Single().PasswordHash;
        Assert.True(_hasher.Verify(hash, "Her-Own-Password-9"));
        Assert.False(_hasher.Verify(hash, DevPassword));
    }
}
