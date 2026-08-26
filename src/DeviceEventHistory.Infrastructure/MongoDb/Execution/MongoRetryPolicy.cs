using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Application.Observability;
using MongoDB.Driver;

namespace DeviceEventHistory.Infrastructure.MongoDb.Execution;

public sealed class MongoRetryPolicy(
    int retryCount,
    IIngestionTelemetry? telemetry = null)
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        string operationName = AppConst.Observability.OperationMongo)
    {
        await ExecuteAsync(
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken,
            operationName);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        string operationName = AppConst.Observability.OperationMongo)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                if (attempt >= retryCount)
                {
                    telemetry?.RecordMongoFailure(operationName);
                    throw;
                }

                telemetry?.RecordMongoRetry(operationName);
                var delayMilliseconds = Math.Min(
                    AppConst.Defaults.PersistenceRetryMaxDelayMilliseconds,
                    AppConst.Defaults.PersistenceRetryDelayMilliseconds * (1 << Math.Min(attempt, 4)));

                await Task.Delay(delayMilliseconds, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        MongoConnectionException => true,
        MongoExecutionTimeoutException => true,
        MongoWriteConcernException => true,
        MongoCommandException commandException =>
            commandException.HasErrorLabel("RetryableWriteError") ||
            commandException.HasErrorLabel("TransientTransactionError"),
        _ => false
    };
}
