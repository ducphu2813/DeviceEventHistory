using DeviceEventStatistics.Infrastructure.MongoDb;
using DeviceEventStatistics.Infrastructure.MongoDb.Indexes;
using DeviceEventStatistics.Infrastructure.SqlServer;
using DeviceEventStatistics.Infrastructure.SqlServer.Schema;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.HostedServices;

public sealed class StartupInitializationHostedService(
    IOptions<WorkerOptions> workerOptions,
    IServiceProvider serviceProvider,
    StartupReadinessBarrier readinessBarrier,
    StartupReadinessState readinessState,
    ILogger<StartupInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            readinessState.MarkDisabled();
            readinessBarrier.Open();
            logger.LogInformation("Statistics worker is disabled; no projection loop will be started.");
            return;
        }

        try
        {
            var mongoContext = serviceProvider.GetRequiredService<MongoHistoryDbContext>();
            var sqlContext = serviceProvider.GetRequiredService<SqlStatisticsDbContext>();
            var schemaVerifier = serviceProvider.GetRequiredService<SqlSchemaVerifier>();
            var mongoIndexVerifier = serviceProvider.GetRequiredService<MongoHistoryIndexVerifier>();
            _ = serviceProvider.GetRequiredService<DeviceMetricMapperRegistry>();

            await mongoContext.PingAsync(cancellationToken);
            await mongoContext.VerifyReadContractAsync(cancellationToken);
            await mongoIndexVerifier.VerifyAsync(cancellationToken);
            await sqlContext.PingAsync(cancellationToken);
            await sqlContext.VerifyTargetAsync(cancellationToken);
            await schemaVerifier.VerifyAsync(cancellationToken);

            readinessState.MarkReady();
            readinessBarrier.Open();
            logger.LogInformation("Statistics startup preflight completed successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readinessState.MarkFailed("STAT-STARTUP-CANCELLED");
            readinessBarrier.Fail(new InvalidOperationException("STAT-STARTUP-CANCELLED: Startup preflight was cancelled."));
            throw;
        }
        catch (Exception exception)
        {
            readinessState.MarkFailed(GetFailureCode(exception));
            readinessBarrier.Fail(exception);
            logger.LogCritical(exception, "Statistics startup preflight failed. FailureCode={FailureCode}", readinessState.FailureCode);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string GetFailureCode(Exception exception) => exception switch
    {
        TimeoutException => "STAT-STARTUP-TIMEOUT",
        _ when exception.Message.StartsWith("STAT-", StringComparison.Ordinal) =>
            exception.Message.Split(':', 2)[0],
        _ => "STAT-STARTUP-DEPENDENCY-FAILED"
    };
}
