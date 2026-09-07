using DeviceEventStatistics.Infrastructure.MongoDb;
using DeviceEventStatistics.Infrastructure.MongoDb.Indexes;
using DeviceEventStatistics.Infrastructure.SqlServer;
using DeviceEventStatistics.Infrastructure.SqlServer.Schema;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.HostedServices;

public sealed class StartupInitializationHostedService(
    IOptions<WorkerOptions> workerOptions,
    IServiceProvider serviceProvider,
    StartupReadinessBarrier readinessBarrier,
    StartupReadinessState readinessState,
    IOptions<ProjectionOptions> projectionOptions,
    IOptions<MetadataOptions> metadataOptions,
    IProjectionDefinitionResolver definitionResolver,
    ProjectionDefinitionRuntimeState runtimeDefinition,
    IMetricKeyResolver metricKeyResolver,
    ILogger<StartupInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!workerOptions.Value.Enabled)
        {
            readinessState.MarkDisabled();
            readinessBarrier.Open();
            logger.LogInformation(StatisticsContractConstants.Messages.MSG_LOG_WORKER_DISABLED);
            return;
        }

        try
        {
            var mongoContext = serviceProvider.GetRequiredService<MongoHistoryDbContext>();
            var sqlContext = serviceProvider.GetRequiredService<SqlStatisticsDbContext>();
            var schemaVerifier = serviceProvider.GetRequiredService<SqlSchemaVerifier>();
            var mongoIndexVerifier = serviceProvider.GetRequiredService<MongoHistoryIndexVerifier>();
            var mapperRegistry = serviceProvider.GetRequiredService<DeviceMetricMapperRegistry>();

            await mongoContext.PingAsync(cancellationToken);
            await mongoContext.VerifyReadContractAsync(cancellationToken);
            await mongoIndexVerifier.VerifyAsync(cancellationToken);
            await sqlContext.PingAsync(cancellationToken);
            await sqlContext.VerifyTargetAsync(cancellationToken);
            await schemaVerifier.VerifyAsync(cancellationToken);

            var settings = projectionOptions.Value;
            var metricRegistry = await metricKeyResolver.ResolveRegistryAsync(
                new MetricRegistryIdentity(
                    settings.MetricSetVersion,
                    settings.MappingVersion,
                    EventOwnershipPolicy.Version),
                mapperRegistry.RequiredMetricCodes,
                cancellationToken);
            logger.LogInformation(
                StatisticsContractConstants.Messages.MSG_LOG_METRIC_REGISTRY_VERIFIED,
                metricRegistry.Identity.MetricSetVersion,
                metricRegistry.Identity.MappingVersion,
                metricRegistry.Identity.OwnershipVersion,
                metricRegistry.Count);

            var definition = await definitionResolver.ResolveAsync(
                new ProjectionDefinitionResolutionRequest(
                    new ProjectionIdentity(
                        settings.Name,
                        settings.ProjectionVersion,
                        StatisticsContractConstants.DefaultPartitionKey),
                    settings.MappingVersion,
                    EventOwnershipPolicy.Version,
                    settings.MetricSetVersion,
                    settings.CoverageStartAtUtc,
                    metadataOptions.Value.TimeZoneId,
                    settings.ResumeFromStoredDefinition,
                    settings.Mode is ProjectionMode.Bootstrap or ProjectionMode.Rebuild),
                cancellationToken);
            runtimeDefinition.Set(definition);

            readinessState.MarkReady();
            readinessBarrier.Open();
            logger.LogInformation(StatisticsContractConstants.Messages.MSG_LOG_STARTUP_READY);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readinessState.MarkFailed(StatisticsContractConstants.StartupErrors.Cancelled);
            readinessBarrier.Fail(new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_STARTUP_CANCELLED));
            throw;
        }
        catch (Exception exception)
        {
            readinessState.MarkFailed(GetFailureCode(exception));
            readinessBarrier.Fail(exception);
            logger.LogCritical(
                exception,
                StatisticsContractConstants.Messages.MSG_LOG_STARTUP_FAILED,
                readinessState.FailureCode);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string GetFailureCode(Exception exception) => exception switch
    {
        TimeoutException => StatisticsContractConstants.StartupErrors.Timeout,
        _ when exception.Message.StartsWith(
            StatisticsContractConstants.MessageCodePrefix,
            StringComparison.Ordinal) =>
            exception.Message.Split(':', 2)[0],
        _ => StatisticsContractConstants.StartupErrors.DependencyFailed
    };
}
