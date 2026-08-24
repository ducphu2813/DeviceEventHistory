using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Worker.Configuration;

public sealed record RedactedConfigurationSummary(
    bool Enabled,
    string WorkerId,
    int SourceCount,
    IReadOnlyCollection<string> SourceIds,
    bool MongoConnectionStringConfigured,
    string DatabaseName,
    string HistoryCollection,
    string FailureCollection,
    string CheckpointCollection);

public sealed class ConfigurationRedactor
{
    public RedactedConfigurationSummary CreateSummary(
        WorkerOptions worker,
        RfidRawLogOptions rawLog,
        MongoDbOptions mongo)
    {
        var sourceIds = rawLog.Sources
            .Select(source => source.SourceId.Trim())
            .Where(sourceId => sourceId.Length > 0)
            .ToArray();

        return new RedactedConfigurationSummary(
            worker.Enabled,
            worker.WorkerId.Trim(),
            sourceIds.Length,
            sourceIds,
            !string.IsNullOrWhiteSpace(mongo.ConnectionString),
            mongo.DatabaseName,
            mongo.HistoryCollection,
            mongo.FailureCollection,
            mongo.CheckpointCollection);
    }
}
