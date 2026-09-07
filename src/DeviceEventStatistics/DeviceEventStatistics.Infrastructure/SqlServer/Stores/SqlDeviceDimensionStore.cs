using DeviceEventStatistics.Application.Metadata;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlDeviceDimensionStore(
    SqlStatisticsDbContext dbContext,
    SqlStatisticsDatabaseOptions options,
    TimeProvider timeProvider) : IDeviceDimensionStore
{
    public async Task UpsertAsync(
        IReadOnlyCollection<DeviceMetadata> metadata,
        CancellationToken cancellationToken = default)
    {
        if (metadata.Count == 0)
        {
            return;
        }

        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        await UpsertAsync(session, metadata, cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        SqlProjectionSession session,
        IReadOnlyCollection<DeviceMetadata> metadata,
        CancellationToken cancellationToken = default)
    {
        foreach (var device in metadata
                     .GroupBy(value => (value.CompanyId, value.DeviceId))
                     .Select(group => group.Last()))
        {
            await UpsertDeviceAsync(session, device, cancellationToken);
        }
    }

    private async Task UpsertDeviceAsync(
        SqlProjectionSession session,
        DeviceMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock
                @Resource = @lockResource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;
            IF @lockResult < 0
                THROW 51000, @lockErrorMessage, 1;

            UPDATE target
            SET [DeviceCode] = COALESCE(@deviceCode, target.[DeviceCode]),
                [DeviceName] = COALESCE(@deviceName, target.[DeviceName]),
                [DeviceType] = COALESCE(@deviceType, target.[DeviceType]),
                [GateId] = COALESCE(@gateId, target.[GateId]),
                [GateCode] = COALESCE(@gateCode, target.[GateCode]),
                [GateName] = COALESCE(@gateName, target.[GateName]),
                [TimeZoneId] = COALESCE(@timeZoneId, target.[TimeZoneId]),
                [MetadataSource] = COALESCE(@metadataSource, target.[MetadataSource]),
                [MetadataUpdatedAtUtc] = @updatedAtUtc,
                [UpdatedAtUtc] = @updatedAtUtc
            FROM {Table("DeviceDimension")} target WITH (UPDLOCK, HOLDLOCK)
            WHERE target.[CompanyId] = @companyId
              AND target.[DeviceId] = @deviceId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {Table("DeviceDimension")}
                (
                    [CompanyId], [DeviceId], [DeviceCode], [DeviceName], [DeviceType], [GateId],
                    [GateCode], [GateName], [TimeZoneId], [TimeZoneEffectiveFromUtc], [IsActive],
                    [MetadataSource], [MetadataUpdatedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]
                )
                SELECT @companyId, @deviceId, @deviceCode, @deviceName, @deviceType, @gateId,
                       @gateCode, @gateName, @timeZoneId, @updatedAtUtc, NULL,
                       @metadataSource, @updatedAtUtc, @updatedAtUtc, @updatedAtUtc
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM {Table("DeviceDimension")} existing WITH (UPDLOCK, HOLDLOCK)
                    WHERE existing.[CompanyId] = @companyId
                      AND existing.[DeviceId] = @deviceId
                );
            END;
            """;
        command.Parameters.Add(new SqlParameter(
            "@lockResource",
            $"DeviceEventStatistics:DeviceDimension:{metadata.CompanyId}:{metadata.DeviceId}"));
        command.Parameters.Add(new SqlParameter("@lockTimeout", options.CommandTimeoutSeconds * 1000));
        command.Parameters.Add(new SqlParameter(
            "@lockErrorMessage",
            StatisticsContractConstants.Messages.MSG_DEVICE_DIMENSION_LOCK_UNAVAILABLE));
        command.Parameters.Add(new SqlParameter("@companyId", metadata.CompanyId));
        command.Parameters.Add(new SqlParameter("@deviceId", metadata.DeviceId));
        command.Parameters.Add(new SqlParameter("@deviceCode", (object?)metadata.DeviceCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@deviceName", (object?)metadata.DeviceName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@deviceType", (object?)metadata.DeviceType ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@gateId", (object?)metadata.GateId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@gateCode", (object?)metadata.GateCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@gateName", (object?)metadata.GateName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@timeZoneId", (object?)metadata.TimeZoneId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@metadataSource", (object?)metadata.Source ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@updatedAtUtc", timeProvider.GetUtcNow().UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string Table(string name) =>
        StatisticsSqlObjectNames.QualifiedTable(options.SchemaName, name);
}
