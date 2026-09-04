using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlMetricKeyResolver(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IMetricKeyResolver
{
    public async Task<IReadOnlyDictionary<string, int>> ResolveAsync(
        int metricSetVersion,
        IReadOnlyCollection<string> metricCodes,
        CancellationToken cancellationToken = default)
    {
        if (metricCodes.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var parameterNames = metricCodes
            .Select((_, index) => $"@metricCode{index}")
            .ToArray();
        command.CommandText = $"""
            SELECT [MetricCode], [MetricKey]
            FROM [{options.SchemaName}].[MetricDefinition]
            WHERE [MetricSetVersion] = @metricSetVersion
              AND [MetricCode] IN ({string.Join(',', parameterNames)});
            """;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@metricSetVersion", metricSetVersion));
        foreach (var (metricCode, index) in metricCodes.Select((value, index) => (value, index)))
        {
            command.Parameters.Add(new SqlParameter(parameterNames[index], metricCode));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }

        return result;
    }
}
