using System.Collections.Concurrent;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Domain.Common;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlMetricKeyResolver(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IMetricKeyResolver
{
    private readonly ConcurrentDictionary<MetricRegistryIdentity, Task<ResolvedMetricRegistry>> registryCache = [];

    public async Task<IReadOnlyDictionary<string, int>> ResolveAsync(
        int metricSetVersion,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken = default)
    {
        var registry = await ResolveRegistryAsync(
            new MetricRegistryIdentity(metricSetVersion, "v1", "v1"),
            metricCodes,
            cancellationToken);
        return registry.MetricKeys;
    }

    public async Task<ResolvedMetricRegistry> ResolveRegistryAsync(
        MetricRegistryIdentity identity,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken = default)
    {
        var requestedCodes = metricCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedCodes.Length == 0)
        {
            return new(
                identity,
                new Dictionary<string, int>(StringComparer.Ordinal));
        }

        var loadTask = registryCache.GetOrAdd(
            identity,
            _ => LoadAndValidateAsync(identity, requestedCodes, cancellationToken));
        try
        {
            var registry = await loadTask;
            var missingCodes = requestedCodes
                .Where(code => !registry.MetricKeys.ContainsKey(code))
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();
            if (missingCodes.Length > 0)
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.Format(
                        StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_REQUIRED_MISSING,
                        string.Join(',', missingCodes)));
            }

            return registry;
        }
        catch
        {
            registryCache.TryRemove(new KeyValuePair<MetricRegistryIdentity, Task<ResolvedMetricRegistry>>(
                identity,
                loadTask));
            throw;
        }
    }

    private async Task<ResolvedMetricRegistry> LoadAndValidateAsync(
        MetricRegistryIdentity identity,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken)
    {
        var entries = await LoadEntriesAsync(identity.MetricSetVersion, metricCodes, cancellationToken);
        return MetricRegistryValidator.Validate(identity, entries, metricCodes);
    }

    private async Task<IReadOnlyList<MetricRegistryEntry>> LoadEntriesAsync(
        int metricSetVersion,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var parameterNames = metricCodes
            .Select((_, index) => $"@metricCode{index}")
            .ToArray();
        command.CommandText = $"""
            SELECT [MetricCode], [MetricKey], [IsEnabled], [MappingVersion], [OwnershipVersion]
            FROM {StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, "MetricDefinition")}
            WHERE [MetricSetVersion] = @metricSetVersion
              AND [MetricCode] IN ({string.Join(',', parameterNames)});
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@metricSetVersion", metricSetVersion));
        foreach (var (metricCode, index) in metricCodes.Select((value, index) => (value, index)))
        {
            command.Parameters.Add(new SqlParameter(parameterNames[index], metricCode));
        }

        var entries = new List<MetricRegistryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new MetricRegistryEntry(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return entries;
    }
}
