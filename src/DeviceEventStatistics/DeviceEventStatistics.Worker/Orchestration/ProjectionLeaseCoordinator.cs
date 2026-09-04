using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class ProjectionLeaseCoordinator(
    IProjectionLeaseStore leaseStore,
    IOptions<WorkerOptions> workerOptions,
    IOptions<ProjectionOptions> projectionOptions)
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
        var result = await leaseStore.AcquireAsync(
            identity,
            workerOptions.Value.WorkerId,
            options.LeaseDuration,
            cancellationToken);
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
