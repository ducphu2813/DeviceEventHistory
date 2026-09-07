using Microsoft.Data.SqlClient;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Infrastructure.SqlServer;

public sealed class SqlProjectionSession : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly SqlTransaction transaction;
    private int completed;

    internal SqlProjectionSession(SqlConnection connection, SqlTransaction transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    public SqlConnection Connection => connection;

    public SqlTransaction Transaction => transaction;

    public bool IsCompleted => Volatile.Read(ref completed) == 1;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await transaction.CommitAsync(cancellationToken);
        Interlocked.Exchange(ref completed, 1);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (IsCompleted)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        Interlocked.Exchange(ref completed, 1);
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsCompleted)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The connection may already have been closed by the provider.
            }
        }

        await transaction.DisposeAsync();
        await connection.DisposeAsync();
        Interlocked.Exchange(ref completed, 1);
    }

    private void EnsureActive()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_SQL_SESSION_COMPLETED);
        }
    }
}
