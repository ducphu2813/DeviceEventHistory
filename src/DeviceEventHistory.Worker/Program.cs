using DeviceEventHistory.Domain.Common;

using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using DeviceEventHistory.Worker.Orchestration;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDeviceEventHistoryConfiguration(builder.Configuration);
builder.Services.AddHostedService<RawLogIngestionHostedService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(AppConst.Logging.StartupCategory);
var redactor = host.Services.GetRequiredService<ConfigurationRedactor>();
var summary = redactor.CreateSummary(
    host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value,
    host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RfidRawLogOptions>>().Value,
    host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoDbOptions>>().Value);

logger.LogInformation(
    AppConst.Logging.ConfigurationValidatedMessage + " Enabled={Enabled}, WorkerId={WorkerId}, SourceCount={SourceCount}, SourceIds={SourceIds}, MongoConnectionStringConfigured={MongoConnectionStringConfigured}, Database={Database}, HistoryCollection={HistoryCollection}, FailureCollection={FailureCollection}, CheckpointCollection={CheckpointCollection}",
    summary.Enabled,
    summary.WorkerId,
    summary.SourceCount,
    string.Join(',', summary.SourceIds),
    summary.MongoConnectionStringConfigured,
    summary.DatabaseName,
    summary.HistoryCollection,
    summary.FailureCollection,
    summary.CheckpointCollection);

host.Run();
