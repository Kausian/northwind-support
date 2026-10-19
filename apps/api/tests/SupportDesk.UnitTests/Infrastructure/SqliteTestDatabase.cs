using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.UnitTests.Infrastructure;

/// <summary>
/// A private in-memory SQLite database with the real EF Core model, for tests that need queries
/// to be translated and run by a database rather than by LINQ to Objects. Lives as long as the
/// connection, so each test gets its own.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    /// <summary>A fresh context on the shared connection, so reads never see tracked entities.</summary>
    public SupportDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
