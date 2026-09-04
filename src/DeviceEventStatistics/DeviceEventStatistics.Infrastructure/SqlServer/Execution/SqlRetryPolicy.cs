using Microsoft.Data.SqlClient;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Execution;

public sealed class SqlRetryPolicy
{
    private readonly TimeProvider timeProvider;
    private readonly Random random;

    public SqlRetryPolicy(TimeProvider? timeProvider = null, Random? random = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.random = random ?? Random.Shared;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxAttempts,
        TimeSpan minimumDelay,
        TimeSpan maximumDelay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (minimumDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumDelay));
        if (maximumDelay < minimumDelay) throw new ArgumentOutOfRangeException(nameof(maximumDelay));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception exception) when (attempt < maxAttempts && IsTransient(exception))
            {
                var exponentialMilliseconds = Math.Min(
                    maximumDelay.TotalMilliseconds,
                    minimumDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                var jitterMilliseconds = random.NextDouble() * Math.Min(250, exponentialMilliseconds / 4);
                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    maximumDelay.TotalMilliseconds,
                    exponentialMilliseconds + jitterMilliseconds));
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    public static bool IsTransient(Exception exception) => exception switch
    {
        TimeoutException => true,
        SqlException sqlException => sqlException.Errors.Cast<SqlError>().Any(IsTransient),
        InvalidOperationException invalidOperationException when
            invalidOperationException.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => true,
        _ => false
    };

    private static bool IsTransient(SqlError error) => error.Number is
        -2 or 53 or 64 or 233 or 1205 or 1222 or 4060 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920;
}
