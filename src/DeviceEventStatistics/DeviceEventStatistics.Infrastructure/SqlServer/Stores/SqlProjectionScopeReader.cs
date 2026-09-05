using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.Projection;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlProjectionScopeReader(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options) : IProjectionScopeReader
{
    public async Task<IReadOnlyList<ProjectionDeviceKey>> ReadDeviceKeysAsync(
        ProjectionIdentity identity,
        IReadOnlyCollection<long> companyIds,
        IReadOnlyCollection<long> deviceIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        var companyFilter = AddIds(command, "companyId", companyIds);
        var deviceFilter = AddIds(command, "deviceId", deviceIds);
        command.CommandText = $"""
            SELECT DISTINCT [CompanyId], [DeviceId]
            FROM
            (
                SELECT [CompanyId], [DeviceId]
                FROM {Table("DeviceStateCursor")}
                WHERE [ProjectionVersion] = @projectionVersion
                  AND [CompanyId] IS NOT NULL AND [DeviceId] IS NOT NULL

                UNION

                SELECT [CompanyId], [DeviceId]
                FROM {Table("DeviceStateDaily")}
                WHERE [ProjectionVersion] = @projectionVersion
                  AND [CompanyId] IS NOT NULL AND [DeviceId] IS NOT NULL

                UNION

                SELECT [CompanyId], [DeviceId]
                FROM {Table("DeviceDimension")}
                WHERE [CompanyId] IS NOT NULL AND [DeviceId] IS NOT NULL
            ) AS [scope]
            WHERE (@hasCompanyFilter = 0 OR [CompanyId] IN ({companyFilter}))
              AND (@hasDeviceFilter = 0 OR [DeviceId] IN ({deviceFilter}))
            ORDER BY [CompanyId], [DeviceId];
            """;
        command.Parameters.Add(new SqlParameter("@projectionVersion", identity.ProjectionVersion));
        command.Parameters.Add(new SqlParameter("@hasCompanyFilter", companyIds.Count == 0 ? 0 : 1));
        command.Parameters.Add(new SqlParameter("@hasDeviceFilter", deviceIds.Count == 0 ? 0 : 1));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ProjectionDeviceKey>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectionDeviceKey(reader.GetInt64(0), reader.GetInt64(1)));
        }

        return result;
    }

    private static string AddIds(SqlCommand command, string parameterPrefix, IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return "NULL";
        }

        var placeholders = new List<string>(ids.Count);
        var index = 0;
        foreach (var id in ids)
        {
            var parameterName = $"@{parameterPrefix}{index++}";
            command.Parameters.Add(new SqlParameter(parameterName, id));
            placeholders.Add(parameterName);
        }

        return string.Join(',', placeholders);
    }

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
