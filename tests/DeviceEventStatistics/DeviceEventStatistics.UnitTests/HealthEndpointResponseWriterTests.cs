using System.Text.Json;
using DeviceEventStatistics.Worker.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DeviceEventStatistics.UnitTests;

public sealed class HealthEndpointResponseWriterTests
{
    [Fact]
    public async Task Response_is_redacted_to_status_and_check_names()
    {
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["statistics_operational_health"] = new HealthReportEntry(
                    HealthStatus.Unhealthy,
                    "secret connection details",
                    TimeSpan.Zero,
                    new InvalidOperationException("Server=secret;Password=secret"),
                    new Dictionary<string, object> { ["ConnectionString"] = "secret" })
            },
            TimeSpan.Zero);

        await HealthEndpointResponseWriter.WriteAsync(context, report);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var json = document.RootElement.GetRawText();

        Assert.Equal("unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "unhealthy",
            document.RootElement.GetProperty("checks").GetProperty("statistics_operational_health").GetString());
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", json, StringComparison.OrdinalIgnoreCase);
    }
}
