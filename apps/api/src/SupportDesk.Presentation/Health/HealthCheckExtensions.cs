using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SupportDesk.Presentation.Health;

public static class HealthCheckExtensions
{
    public const string HealthPath = "/health";

    /// <summary>A probe should answer quickly; an unreachable database must not hang it.</summary>
    private static readonly TimeSpan DatabaseCheckTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, timeout: DatabaseCheckTimeout);

        return services;
    }

    /// <summary>
    /// GET /health: 200 when every check passes, 503 otherwise. Open without a token - it is the
    /// one endpoint besides sign-in exempt from the fallback policy, declared here, next to it.
    /// The body names each check and its status only: no exception text, server names or
    /// connection strings.
    /// </summary>
    public static IEndpointConventionBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHealthChecks(HealthPath, new HealthCheckOptions { ResponseWriter = WriteAsync })
            .AllowAnonymous();

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
    }
}
