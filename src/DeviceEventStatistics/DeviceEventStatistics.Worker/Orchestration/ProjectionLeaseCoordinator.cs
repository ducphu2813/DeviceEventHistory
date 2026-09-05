using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class ProjectionLeaseCoordinator(
    IProjectionLeaseStore leaseStore,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions,
    IStatisticsTelemetry telemetry,
    ILogger<ProjectionLeaseCoordinator> logger)
{
    private readonly Lock gate = new();
    private ProjectionLeaseToken? currentLease;
    private CancellationTokenSource? leaseLostSource;

    public ProjectionLeaseToken? CurrentLease
    {
        get
        {
            lock (gate)
            {
                return currentLease;
            }
        }
    }

    public CancellationToken LeaseLostToken
    {
        get
        {
            lock (gate)
            {
                return leaseLostSource?.Token ?? CancellationToken.None;
            }
        }
    }

    public async Task<LeaseAcquireResult> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var options = projectionOptions.Value;
        var identity = new ProjectionIdentity(
            options.Name,
            options.ProjectionVersion,
            StatisticsContractConstants.DefaultPartitionKey);
        LeaseAcquireResult result;
        try
        {
            result = await leaseStore.AcquireAsync(
                identity,
                workerOptions.Value.WorkerId,
                options.LeaseDuration,
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (
            string.Equals(
                exception.Message,
                StatisticsContractConstants.Messages.MSG_SQL_LEASE_APPLOCK_UNAVAILABLE,
                StringComparison.Ordinal))
        {
            return new LeaseAcquireResult(false, null);
        }
        if (!result.Acquired || result.Lease is null)
        {
            return result;
        }

        lock (gate)
        {
            leaseLostSource?.Dispose();
            leaseLostSource = new CancellationTokenSource();
            currentLease = result.Lease;
        }

        telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseAcquired);

        return result;
    }

    public async Task<bool> RenewAsync(CancellationToken cancellationToken)
    {
        var lease = CurrentLease;
        if (lease is null)
        {
            return false;
        }

        var renewed = await leaseStore.RenewAsync(
            lease,
            projectionOptions.Value.LeaseDuration,
            cancellationToken);
        if (renewed is null)
        {
            telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseLost);
            logger.LogWarning(
                StatisticsContractConstants.Messages.MSG_LOG_LEASE_LOST,
                lease.Epoch);
            SignalLeaseLost();
            return false;
        }

        lock (gate)
        {
            if (currentLease?.Epoch == lease.Epoch && currentLease.Owner == lease.Owner)
            {
                currentLease = renewed;
            }
        }

        telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseRenewed);

        return true;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        var lease = CurrentLease;
        if (lease is not null)
        {
            await leaseStore.ReleaseAsync(lease, cancellationToken);
        }

        lock (gate)
        {
            currentLease = null;
            leaseLostSource?.Dispose();
            leaseLostSource = null;
        }

        if (lease is not null)
        {
            telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseReleased);
        }
    }

    public void SignalLeaseLost()
    {
        lock (gate)
        {
            leaseLostSource?.Cancel();
            currentLease = null;
        }
    }
}
