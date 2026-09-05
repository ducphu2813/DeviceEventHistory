using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Worker.Configuration;
using DeviceEventStatistics.Worker.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.UnitTests;

public sealed class ProjectionLeaseCoordinatorTests
{
    [Fact]
    public async Task Shared_lease_is_released_only_after_all_holders_release()
    {
        var store = new TestLeaseStore();
        var coordinator = CreateCoordinator(store);

        var first = await coordinator.TryAcquireAsync(CancellationToken.None);
        var second = await coordinator.TryAcquireAsync(CancellationToken.None);

        Assert.True(first.Acquired);
        Assert.True(second.Acquired);
        Assert.Same(first.Lease, second.Lease);
        Assert.Equal(1, store.AcquireCount);

        await coordinator.ReleaseAsync(CancellationToken.None);

        Assert.Equal(0, store.ReleaseCount);
        Assert.NotNull(coordinator.CurrentLease);

        await coordinator.ReleaseAsync(CancellationToken.None);

        Assert.Equal(1, store.ReleaseCount);
        Assert.Null(coordinator.CurrentLease);
    }

    [Fact]
    public async Task Lease_loss_cancels_current_work_and_clears_shared_state()
    {
        var store = new TestLeaseStore();
        var coordinator = CreateCoordinator(store);
        await coordinator.TryAcquireAsync(CancellationToken.None);
        var leaseLostToken = coordinator.LeaseLostToken;

        coordinator.SignalLeaseLost();

        Assert.True(leaseLostToken.IsCancellationRequested);
        Assert.Null(coordinator.CurrentLease);
        await coordinator.ReleaseAsync(CancellationToken.None);
        Assert.Equal(0, store.ReleaseCount);
    }

    private static ProjectionLeaseCoordinator CreateCoordinator(TestLeaseStore store) =>
        new(
            store,
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "test-worker" }),
            Options.Create(new ProjectionOptions
            {
                Name = "test-projection",
                ProjectionVersion = 1,
                LeaseDuration = TimeSpan.FromMinutes(2)
            }),
            new NullStatisticsTelemetry(),
            NullLogger<ProjectionLeaseCoordinator>.Instance);

    private sealed class TestLeaseStore : IProjectionLeaseStore
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task<LeaseAcquireResult> AcquireAsync(
            ProjectionIdentity requestedIdentity,
            string owner,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return Task.FromResult(new LeaseAcquireResult(
                true,
                new ProjectionLeaseToken(
                    requestedIdentity,
                    owner,
                    1,
                    DateTimeOffset.UtcNow.Add(duration))));
        }

        public Task<ProjectionLeaseToken?> RenewAsync(
            ProjectionLeaseToken lease,
            TimeSpan duration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ProjectionLeaseToken?>(lease with { ExpiresAtUtc = DateTimeOffset.UtcNow.Add(duration) });

        public Task<bool> ReleaseAsync(
            ProjectionLeaseToken lease,
            CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            return Task.FromResult(true);
        }
    }
}
