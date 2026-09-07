using DeviceEventStatistics.Infrastructure.SqlServer.Execution;

namespace DeviceEventStatistics.UnitTests;

public sealed class SqlRetryPolicyTests
{
    [Fact]
    public void Classifies_timeout_and_connection_failures_as_transient()
    {
        Assert.True(SqlRetryPolicy.IsTransient(new TimeoutException()));
        Assert.True(SqlRetryPolicy.IsTransient(new InvalidOperationException("connection reset")));
        Assert.False(SqlRetryPolicy.IsTransient(new InvalidOperationException("constraint violation")));
    }

    [Fact]
    public async Task Retries_transient_operation_with_cancellation_support()
    {
        var policy = new SqlRetryPolicy(random: new Random(17));
        var attempts = 0;

        var result = await policy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3) throw new TimeoutException();
                return Task.FromResult("committed");
            },
            maxAttempts: 3,
            minimumDelay: TimeSpan.FromMilliseconds(1),
            maximumDelay: TimeSpan.FromMilliseconds(5));

        Assert.Equal("committed", result);
        Assert.Equal(3, attempts);
    }
}
