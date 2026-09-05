using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DeviceEventStatistics.Worker.HealthChecks;

public static class HealthEndpointResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = ToStatus(report.Status),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => ToStatus(entry.Value.Status),
                StringComparer.Ordinal)
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(response), context.RequestAborted);
    }

    private static string ToStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy"
    };
}
