using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.ApiTests;

/// <summary>Task 3 through the real HTTP pipeline: sign-in, tokens, central protection, actor.</summary>
public sealed class AuthenticationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // ---- AU-4: login ----------------------------------------------------------------------

    [Fact]
    public async Task Login_WithTheRightCredentials_ReturnsASignedTokenItsExpiryAndTheAgent()
    {
        var client = factory.CreateSeededClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.SaraEmail, ApiFactory.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var login = JsonSerializer.Deserialize<LoginResponse>(body, Json.Options)!;

        Assert.Equal(3, login.AccessToken.Split('.').Length);
        Assert.InRange(login.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
        Assert.Equal(ApiFactory.SaraEmail, login.Agent.Email);
        Assert.Equal("Sara Lindqvist", login.Agent.FullName);

        // The credential never leaves the server.
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_UnknownEmail_WrongPassword_AndInactiveAgent_GetTheIdentical401()
    {
        var client = factory.CreateSeededClient();

        var attempts = new[]
        {
            new LoginRequest("nobody@northwind.test", ApiFactory.Password),
            new LoginRequest(ApiFactory.SaraEmail, "Wrong-Password-1"),
            new LoginRequest(ApiFactory.YukiEmail, ApiFactory.Password)
        };

        var problems = new List<(HttpStatusCode Status, string? Title, string? Detail)>();
        foreach (var attempt in attempts)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", attempt);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            problems.Add((response.StatusCode, problem?.Title, problem?.Detail));
        }

        Assert.All(problems, p => Assert.Equal((HttpStatusCode.Unauthorized, "Unauthorized", "Email or password is incorrect."), p));
    }

    [Fact]
    public async Task Login_WithMissingFields_Is400()
    {
        var client = factory.CreateSeededClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- AU-5: me -------------------------------------------------------------------------

    [Fact]
    public async Task Me_WithAValidToken_ReturnsTheSignedInAgent()
    {
        var client = await factory.SignedInClientAsync(ApiFactory.PriyaEmail);

        var me = await client.GetFromJsonAsync<AgentProfileDto>("/api/auth/me", Json.Options);

        Assert.Equal(new AgentProfileDto(me!.Id, "Priya Nair", ApiFactory.PriyaEmail), me);
    }

    [Fact]
    public async Task Me_WithoutAToken_Is401WithAProblemDocument()
    {
        var client = factory.CreateSeededClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Sign in to use the API.", problem?.Detail);
    }

    [Fact]
    public async Task Me_ForAnAgentDeactivatedAfterSigningIn_Is401()
    {
        var client = await factory.SignedInClientAsync(ApiFactory.PriyaEmail);
        factory.Deactivate(ApiFactory.PriyaEmail);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- AU-6: every existing endpoint is closed without a token --------------------------

    [Theory]
    [InlineData("GET", "/api/tickets")]
    [InlineData("GET", "/api/tickets/1")]
    [InlineData("GET", "/api/tickets/1/escalations")]
    [InlineData("POST", "/api/tickets")]
    [InlineData("POST", "/api/tickets/1/escalate")]
    [InlineData("PATCH", "/api/tickets/1/status")]
    [InlineData("PATCH", "/api/tickets/1/assignment")]
    [InlineData("GET", "/api/customers")]
    [InlineData("GET", "/api/customers/1")]
    [InlineData("GET", "/api/agents")]
    [InlineData("GET", "/api/categories")]
    public async Task EveryEndpoint_WithoutAToken_Is401(string method, string path)
    {
        var client = factory.CreateSeededClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { })
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithAValidToken_Is200()
    {
        var client = await factory.SignedInClientAsync();

        var response = await client.GetAsync("/api/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Token validation ------------------------------------------------------------------

    [Fact]
    public async Task ExpiredToken_Is401_SayingTheSessionExpired()
    {
        // Expired a minute ago: beyond the 30 s clock skew.
        var response = await GetTicketsWith(Tokens.Create(expiresUtc: DateTime.UtcNow.AddMinutes(-1)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Your session has expired. Sign in again.", (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Is401() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await GetTicketsWith(Tokens.Create(key: "someone-elses-signing-key-0123456789abcdef"))).StatusCode);

    [Fact]
    public async Task TokenWithATamperedPayload_Is401()
    {
        var parts = Tokens.Create(subject: "1").Split('.');
        var forged = Tokens.Create(subject: "2", key: "attacker-key-0000000000000000000000000000").Split('.');

        // A genuine signature on someone else's claims.
        var response = await GetTicketsWith($"{parts[0]}.{forged[1]}.{parts[2]}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnsignedToken_Is401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetTicketsWith(Tokens.Unsigned())).StatusCode);

    [Fact]
    public async Task TokenFromAnotherIssuer_Is401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetTicketsWith(Tokens.Create(issuer: "someone-else"))).StatusCode);

    [Fact]
    public async Task TokenForAnotherAudience_Is401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetTicketsWith(Tokens.Create(audience: "another-app"))).StatusCode);

    [Fact]
    public async Task HandMadeValidToken_IsAccepted_SoTheRejectionsAboveAreAboutTheirOneDefect() =>
        Assert.Equal(HttpStatusCode.OK, (await GetTicketsWith(Tokens.Create())).StatusCode);

    // ---- AU-8: the escalation actor comes from the token ----------------------------------

    [Fact]
    public async Task Escalate_RecordsTheSignedInAgent_AndIgnoresAnActorInTheBody()
    {
        var ticketId = factory.CreateTicket();
        var client = await factory.SignedInClientAsync(ApiFactory.SaraEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/escalate",
            new { reason = "Customer is blocked", escalatedBy = ApiFactory.PriyaEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EscalationResultDto>(Json.Options);

        Assert.Equal(ApiFactory.SaraEmail, result!.Escalation.EscalatedBy);
        Assert.Equal(ApiFactory.SaraEmail, factory.EscalatedByOfLatestEscalation(ticketId));
    }

    [Fact]
    public async Task Escalate_StillAppliesItsOwnValidation()
    {
        var ticketId = factory.CreateTicket();
        var client = await factory.SignedInClientAsync();

        var response = await client.PostAsJsonAsync($"/api/tickets/{ticketId}/escalate", new { reason = "abc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetTicketsWith(string token)
    {
        var client = factory.CreateSeededClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/api/tickets");
    }
}
