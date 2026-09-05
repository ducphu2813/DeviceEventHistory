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
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private ProjectionLeaseToken? currentLease;
    private CancellationTokenSource? leaseLostSource;
    private int leaseReferenceCount;

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
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            lock (gate)
            {
                if (currentLease is not null)
                {
                    leaseReferenceCount++;
                    return new LeaseAcquireResult(true, currentLease);
                }
            }

            return await AcquireUnderlyingLeaseAsync(cancellationToken);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<LeaseAcquireResult> AcquireUnderlyingLeaseAsync(CancellationToken cancellationToken)
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
            leaseReferenceCount = 1;
        }

        telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseAcquired);

        return result;
    }

    public async Task<bool> RenewAsync(CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
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
                else
                {
                    return false;
                }
            }

            telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseRenewed);

            return true;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ProjectionLeaseToken? leaseToRelease = null;
            lock (gate)
            {
                if (currentLease is null || leaseReferenceCount == 0)
                {
                    return;
                }

                leaseReferenceCount--;
                if (leaseReferenceCount == 0)
                {
                    leaseToRelease = currentLease;
                    currentLease = null;
                    leaseLostSource?.Dispose();
                    leaseLostSource = null;
                }
            }

            if (leaseToRelease is null)
            {
                return;
            }

            if (await leaseStore.ReleaseAsync(leaseToRelease, cancellationToken))
            {
                telemetry.RecordLeaseTransition(StatisticsContractConstants.Telemetry.LeaseReleased);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public void SignalLeaseLost()
    {
        lock (gate)
        {
            leaseLostSource?.Cancel();
            currentLease = null;
            leaseReferenceCount = 0;
        }
    }
}
