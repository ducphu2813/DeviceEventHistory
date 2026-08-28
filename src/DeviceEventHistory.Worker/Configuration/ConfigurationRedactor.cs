using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;

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
    string CheckpointCollection,
    bool AppHubEnabled,
    IReadOnlyCollection<RedactedAppHubSourceSummary> AppHubSources);

public sealed record RedactedAppHubSourceSummary(
    string SourceId,
    string EndpointHost,
    int EnabledEventCount,
    bool CredentialConfigured);

public sealed class ConfigurationRedactor
{
    public RedactedConfigurationSummary CreateSummary(
        WorkerOptions worker,
        RfidRawLogOptions rawLog,
        MongoDbOptions mongo,
        AppHubOptions? appHub = null)
    {
        var sourceIds = (rawLog.Sources ?? [])
            .Select(source => source.SourceId.Trim())
            .Where(sourceId => sourceId.Length > 0)
            .ToArray();

        var appHubSources = appHub?.Sources
            ?? [];
        var redactedAppHubSources = appHubSources
            .Select(source => new RedactedAppHubSourceSummary(
                source.SourceId.Trim(),
                GetEndpointHost(source.Endpoint),
                source.EnabledEvents?.Count ?? 0,
                HasCredentialConfigured(source)))
            .ToArray()
            ;

        return new RedactedConfigurationSummary(
            worker.Enabled,
            worker.WorkerId.Trim(),
            sourceIds.Length,
            sourceIds,
            !string.IsNullOrWhiteSpace(mongo.ConnectionString),
            mongo.DatabaseName,
            mongo.HistoryCollection,
            mongo.FailureCollection,
            mongo.CheckpointCollection,
            appHub?.Enabled ?? false,
            redactedAppHubSources);
    }

    private static string GetEndpointHost(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;

    private static bool HasCredentialConfigured(AppHubSourceOptions source) =>
        (!string.IsNullOrWhiteSpace(source.AccessTokenEnvironmentVariable) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(source.AccessTokenEnvironmentVariable))) ||
        (!string.IsNullOrWhiteSpace(source.TokenJwtEnvironmentVariable) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(source.TokenJwtEnvironmentVariable)));
}
