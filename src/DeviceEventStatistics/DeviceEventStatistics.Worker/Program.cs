using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using DeviceEventStatistics.Worker.HostedServices;
using DeviceEventStatistics.Worker.Orchestration;
using DeviceEventStatistics.Worker.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var healthSection = builder.Configuration.GetSection(ObservabilityOptions.SectionName);
if (healthSection.GetValue(nameof(ObservabilityOptions.HealthEndpointEnabled), true))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{healthSection.GetValue(nameof(ObservabilityOptions.HealthPort), 8080)}");
}
builder.Services.AddDeviceEventStatisticsConfiguration(builder.Configuration);
builder.Services.AddDeviceEventStatisticsInfrastructure();
builder.Services.AddDeviceEventStatisticsObservability();
builder.Services.AddHostedService<StartupInitializationHostedService>();
builder.Services.AddHostedService<DisabledWorkerHostedService>();
builder.Services.AddHostedService<IncrementalProjectionHostedService>();
builder.Services.AddHostedService<HistoryContractAuditHostedService>();
builder.Services.AddHostedService<LeaseHeartbeatHostedService>();
builder.Services.AddHostedService<DurationRefreshHostedService>();
builder.Services.AddHostedService<ReconciliationHostedService>();
builder.Services.AddHostedService<ManualProjectionHostedService>();
builder.Services.AddHostedService<RetentionCleanupHostedService>();
builder.Services.AddHostedService<OperationalHealthHostedService>();
builder.Services.AddHostedService<GracefulShutdownHostedService>();

var host = builder.Build();

var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("DeviceEventStatistics.Startup");
var redactor = host.Services.GetRequiredService<ConfigurationRedactor>();
var summary = redactor.CreateSummary(
    host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value,
    host.Services.GetRequiredService<IOptions<ProjectionOptions>>().Value,
    host.Services.GetRequiredService<IOptions<DatabaseSettingsOptions>>().Value);

logger.LogInformation(
    StatisticsContractConstants.Messages.MSG_LOG_CONFIGURATION_VALIDATED,
    summary.Enabled,
    summary.WorkerId,
    summary.ProjectionMode,
    summary.ProjectionName,
    summary.ProjectionVersion,
    summary.MongoConnectionStringConfigured,
    summary.MongoDatabaseName,
    summary.MongoHistoryCollection,
    summary.SqlConnectionStringConfigured,
    summary.SqlDatabaseName,
    summary.SqlSchemaName,
    summary.CompanyIds.Count,
    summary.DeviceIds.Count);

var observabilityOptions = host.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
if (observabilityOptions.HealthEndpointEnabled)
{
    host.MapHealthChecks(
        "/health/live",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live", StringComparer.Ordinal),
            ResponseWriter = HealthEndpointResponseWriter.WriteAsync
        });
    host.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.Ordinal),
            ResponseWriter = HealthEndpointResponseWriter.WriteAsync
        });
}

await host.RunAsync();
