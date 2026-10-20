using Microsoft.Extensions.Diagnostics.HealthChecks;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Presentation.Health;

/// <summary>
/// DK-7: is the database reachable? Opens a real connection through the application's own
/// DbContext, so it fails exactly when the API's queries would.
/// </summary>
public sealed class DatabaseHealthCheck(SupportDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // SqlClient ignores the token until its own 15 s connect timeout, and part of opening a
            // connection (resolving the server name) runs synchronously before a task is returned.
            // Task.Run + WaitAsync stop waiting at the check's timeout either way; the cancellation then
            // counts as unhealthy. Measured with SQL Server stopped: 20 s without this, 16 s with
            // WaitAsync alone.
            return await Task.Run(() => db.Database.CanConnectAsync(cancellationToken), cancellationToken)
                .WaitAsync(cancellationToken)
                ? HealthCheckResult.Healthy("The database is reachable.")
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Logged by the health check service; never written to the response.
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}
