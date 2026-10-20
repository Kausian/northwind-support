using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.ApiTests;

/// <summary>
/// The real API (Program.cs, unchanged) with its SQL Server context swapped for a private
/// in-memory SQLite database, and test-only settings - including a signing key that exists only
/// here. One instance is shared by a test class.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "api-tests-only-signing-key-0123456789abcdef";
    public const string Issuer = "northwind-support-api";
    public const string Audience = "northwind-support-web";
    public const string Password = "Correct-Horse-1";

    public const string SaraEmail = "sara@northwind.test";
    public const string PriyaEmail = "priya@northwind.test";
    public const string YukiEmail = "yuki@northwind.test";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private bool _seeded;

    public int SaraId { get; private set; }

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        // Not Development: no automatic migration or demo seed, no Swagger.
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Default", "Server=unused-by-tests");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Sla:BaseWindowHours:Low", "72");
        builder.UseSetting("Sla:BaseWindowHours:Medium", "24");
        builder.UseSetting("Sla:BaseWindowHours:High", "8");
        builder.UseSetting("Sla:BaseWindowHours:Critical", "4");
        builder.UseSetting("Sla:PremiumCustomerMultiplier", "0.5");
        builder.UseSetting("Sla:MinimumWindowHours", "1");
        builder.UseSetting("Sla:AtRiskThresholdPercent", "25");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<SupportDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SupportDbContext>>();
            services.AddDbContext<SupportDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>A client with the schema and test data in place.</summary>
    public HttpClient CreateSeededClient()
    {
        var client = CreateClient();
        EnsureSeeded();
        return client;
    }

    /// <summary>Signs in through the real endpoint and returns a client that sends the token.</summary>
    public async Task<HttpClient> SignedInClientAsync(string email = SaraEmail)
    {
        var client = CreateSeededClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    /// <summary>A new open Medium ticket, so each escalation test starts from its own.</summary>
    public int CreateTicket()
    {
        EnsureSeeded();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var reference = $"TCK-{db.Set<Ticket>().Count() + 1:D4}";
        var ticket = Ticket.Raise(reference, "Printer jams", "Every page jams.", CustomerId, CategoryId,
            TicketPriority.Medium, clock.UtcNow, TimeSpan.FromHours(24), SaraId);
        db.Add(ticket);
        db.SaveChanges();
        return ticket.Id;
    }

    /// <summary>Deactivates an agent directly in the database, as an administrator would.</summary>
    public void Deactivate(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        db.Set<Agent>().Single(a => a.Email == email).Deactivate();
        db.SaveChanges();
    }

    public string? EscalatedByOfLatestEscalation(int ticketId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        return db.Set<TicketEscalation>().Where(e => e.TicketId == ticketId).OrderByDescending(e => e.Id).Select(e => e.EscalatedBy).FirstOrDefault();
    }

    private void EnsureSeeded()
    {
        if (_seeded)
        {
            return;
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        db.Database.EnsureCreated();

        var category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        var customer = new Customer("Fabrikam Inc", "help@fabrikam.test", null, CustomerTier.Standard, now);
        var sara = new Agent("Sara Lindqvist", SaraEmail, 10, now);
        var priya = new Agent("Priya Nair", PriyaEmail, 10, now);
        var yuki = new Agent("Yuki Tanaka", YukiEmail, 8, now);
        yuki.Deactivate();

        foreach (var agent in new[] { sara, priya, yuki })
        {
            agent.SetPasswordHash(hasher.Hash(Password));
        }

        db.AddRange(category, customer, sara, priya, yuki);
        db.SaveChanges();

        (SaraId, CustomerId, CategoryId) = (sara.Id, customer.Id, category.Id);
        _seeded = true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}
