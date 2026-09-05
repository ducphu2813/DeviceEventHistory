using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Worker.Orchestration;

namespace DeviceEventStatistics.UnitTests;

public sealed class PhaseEightObservabilityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Idle_source_with_caught_up_checkpoint_is_healthy()
    {
        var snapshot = Snapshot(
            sourceLatest: Now.AddHours(-30),
            checkpoint: Now.AddHours(-30));

        var evaluation = new StatisticsHealthEvaluator().Evaluate(
            new StatisticsHealthInput(true, true, false, Now, snapshot),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24));

        Assert.Equal(StatisticsHealthStatus.Healthy, evaluation.Status);
        Assert.Equal(TimeSpan.Zero, evaluation.IncrementalLag);
    }

    [Fact]
    public void Twelve_hour_lag_is_degraded_and_twenty_four_hour_lag_is_unhealthy()
    {
        var evaluator = new StatisticsHealthEvaluator();
        var snapshot = Snapshot(
            sourceLatest: Now,
            checkpoint: Now.AddHours(-12));

        var warning = evaluator.Evaluate(
            new StatisticsHealthInput(true, true, false, Now, snapshot),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24));
        var breach = evaluator.Evaluate(
            new StatisticsHealthInput(
                true,
                true,
                false,
                Now,
                Snapshot(sourceLatest: Now, checkpoint: Now.AddHours(-24))),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24));

        Assert.Equal(StatisticsHealthStatus.Degraded, warning.Status);
        Assert.Equal(StatisticsHealthStatus.Unhealthy, breach.Status);
    }

    [Fact]
    public void Unrecoverable_coverage_is_unhealthy_even_when_checkpoint_is_caught_up()
    {
        var evaluation = new StatisticsHealthEvaluator().Evaluate(
            new StatisticsHealthInput(
                true,
                true,
                false,
                Now,
                Snapshot(
                    sourceLatest: Now,
                    checkpoint: Now,
                    hasUnrecoverableCoverage: true)),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24));

        Assert.Equal(StatisticsHealthStatus.Unhealthy, evaluation.Status);
        Assert.Equal(StatisticsContractConstants.HealthReasons.UnrecoverableCoverage, evaluation.Reason);
    }

    [Fact]
    public void Shutdown_drain_is_degraded_and_new_operations_are_rejected()
    {
        var coordinator = new GracefulShutdownCoordinator();
        Assert.True(coordinator.TryBeginOperation(out var operation));

        coordinator.BeginDrain();

        Assert.True(coordinator.IsDraining);
        Assert.False(coordinator.TryBeginOperation(out _));
        operation!.Dispose();
        Assert.Equal(0, coordinator.ActiveOperations);
    }

    [Fact]
    public void Retention_headroom_uses_required_source_time_not_request_creation_time()
    {
        var snapshot = Snapshot(
            sourceLatest: Now,
            checkpoint: Now,
            oldestPendingRequest: Now.AddDays(-30),
            oldestRequiredFrom: Now.AddDays(-4),
            retentionBoundary: Now.AddDays(-7));

        var evaluation = new StatisticsHealthEvaluator().Evaluate(
            new StatisticsHealthInput(true, true, false, Now, snapshot),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(2));

        Assert.Equal(StatisticsHealthStatus.Unhealthy, evaluation.Status);
        Assert.Equal(StatisticsContractConstants.HealthReasons.PendingRequestAge, evaluation.Reason);
        Assert.Equal(TimeSpan.FromDays(3), evaluation.RetentionHeadroom);
    }

    [Fact]
    public void Required_source_before_retention_boundary_is_a_source_retention_risk()
    {
        var snapshot = Snapshot(
            sourceLatest: Now,
            checkpoint: Now,
            oldestRequiredFrom: Now.AddDays(-8),
            retentionBoundary: Now.AddDays(-7)) with
        {
            SourceOldestPersistedAtUtc = Now.AddDays(-9)
        };

        var evaluation = new StatisticsHealthEvaluator().Evaluate(
            new StatisticsHealthInput(true, true, false, Now, snapshot),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(2));

        Assert.Equal(StatisticsHealthStatus.Unhealthy, evaluation.Status);
        Assert.Equal(StatisticsContractConstants.HealthReasons.SourceRetentionRisk, evaluation.Reason);
    }

    [Fact]
    public void Manual_mode_does_not_require_a_lease()
    {
        var evaluation = new StatisticsHealthEvaluator().Evaluate(
            new StatisticsHealthInput(
                true,
                true,
                false,
                Now,
                Snapshot(sourceLatest: Now, checkpoint: Now) with { LeaseHeld = false },
                RequiresLease: false),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24));

        Assert.Equal(StatisticsHealthStatus.Healthy, evaluation.Status);
    }

    private static ProjectionOperationalSnapshot Snapshot(
        DateTimeOffset? sourceLatest,
        DateTimeOffset? checkpoint,
        bool hasUnrecoverableCoverage = false,
        DateTimeOffset? oldestPendingRequest = null,
        DateTimeOffset? oldestRequiredFrom = null,
        DateTimeOffset? retentionBoundary = null) =>
        new(
            sourceLatest,
            sourceLatest?.AddDays(-7),
            checkpoint,
            true,
            0,
            oldestPendingRequest,
            null,
            Now,
            hasUnrecoverableCoverage,
            oldestRequiredFrom,
            retentionBoundary);
}
