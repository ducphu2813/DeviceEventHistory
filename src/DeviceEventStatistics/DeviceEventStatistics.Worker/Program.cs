using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Worker.Configuration;
using DeviceEventStatistics.Worker.HostedServices;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDeviceEventStatisticsConfiguration(builder.Configuration);
builder.Services.AddDeviceEventStatisticsInfrastructure();
builder.Services.AddDeviceEventStatisticsObservability();
builder.Services.AddHostedService<StartupInitializationHostedService>();
builder.Services.AddHostedService<DisabledWorkerHostedService>();

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
    "Statistics configuration validated. Enabled={Enabled}, WorkerId={WorkerId}, Mode={Mode}, ProjectionName={ProjectionName}, ProjectionVersion={ProjectionVersion}, MongoConnectionStringConfigured={MongoConnectionStringConfigured}, MongoDatabase={MongoDatabase}, HistoryCollection={HistoryCollection}, SqlConnectionStringConfigured={SqlConnectionStringConfigured}, SqlDatabase={SqlDatabase}, SqlSchema={SqlSchema}, CompanyScopeCount={CompanyScopeCount}, DeviceScopeCount={DeviceScopeCount}",
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

host.Run();
