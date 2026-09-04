using DeviceEventStatistics.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer;

public sealed class SqlStatisticsDbContext(SqlStatisticsDatabaseOptions options)
{
    public SqlConnection CreateConnection() => new(options.ConnectionString)
    {
        StatisticsEnabled = false
    };

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.CommandTimeout = options.CommandTimeoutSeconds;
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task VerifyTargetAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME(), SCHEMA_ID(@schemaName);";
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@schemaName", options.SchemaName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("STAT-SQL-TARGET-UNVERIFIED: SQL target verification returned no result.");
        }

        var databaseName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        int? schemaId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
        if (!string.Equals(databaseName, options.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"STAT-SQL-DATABASE-MISMATCH: Connected to '{databaseName}', expected '{options.DatabaseName}'.");
        }

        if (schemaId is null)
        {
            throw new InvalidOperationException(
                $"STAT-SQL-SCHEMA-MISSING: Schema '{options.SchemaName}' was not found in database '{options.DatabaseName}'.");
        }
    }

    public async Task<SqlProjectionSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            return new SqlProjectionSession(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
