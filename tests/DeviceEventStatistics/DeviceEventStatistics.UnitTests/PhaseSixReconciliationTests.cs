using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.UnitTests;

public sealed class PhaseSixReconciliationTests
{
    [Fact]
    public void Forward_propagation_splits_long_ranges_into_bounded_continuations()
    {
        var propagation = new ForwardStatePropagation();

        var result = propagation.Split(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 10),
            3);

        Assert.Equal(
        [
            new PropagationRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3)),
            new PropagationRange(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 6)),
            new PropagationRange(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 9)),
            new PropagationRange(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10))
        ], result);
    }

    [Fact]
    public void Coverage_policy_rejects_membership_lost_from_retained_source()
    {
        var request = CreateRequest(new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 28));
        var claim = new ReconciliationClaim(
            request,
            "worker-1",
            1,
            DateTimeOffset.UtcNow.AddMinutes(1));
        var snapshot = new ReconciliationSnapshot(
            Guid.NewGuid(),
            claim,
            Utc(2026, 8, 28, 0),
            Utc(2026, 9, 1, 0),
            4,
            [new ReconciliationMembership(new string('a', 64), "source-1")],
            new Dictionary<StateStreamKey, StateCursorSnapshot>());
        var policy = new ProjectionCoveragePolicy();

        var decision = policy.Evaluate(
            claim,
            snapshot,
            [],
            Utc(2026, 9, 1, 0),
            new ReconciliationExecutionOptions(
                "v1", 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), 3, 100, 31, 10,
                TimeSpan.FromDays(7), TimeSpan.FromDays(2), new DateOnly(2026, 9, 1)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(ReconciliationReasonCodes.SourceIdentityMissing, decision.ReasonCode);
    }

    [Fact]
    public void Coverage_policy_marks_missing_opening_state_as_partial()
    {
        var request = CreateRequest(new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 29));
        var claim = new ReconciliationClaim(
            request,
            "worker-1",
            1,
            DateTimeOffset.UtcNow.AddMinutes(1));
        var snapshot = new ReconciliationSnapshot(
            Guid.NewGuid(),
            claim,
            Utc(2026, 8, 28, 0),
            Utc(2026, 8, 30, 0),
            4,
            [],
            new Dictionary<StateStreamKey, StateCursorSnapshot>());

        var decision = new ProjectionCoveragePolicy().Evaluate(
            claim,
            snapshot,
            [],
            Utc(2026, 9, 1, 0),
            new ReconciliationExecutionOptions(
                "v1", 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), 3, 100, 31, 10,
                TimeSpan.FromDays(7), TimeSpan.FromDays(2), new DateOnly(2026, 8, 29)));

        Assert.True(decision.IsAllowed);
        Assert.Equal("partial", decision.Status);
        Assert.Equal(ReconciliationReasonCodes.OpeningStateEvidenceMissing, decision.ReasonCode);
    }

    private static ReconciliationRequest CreateRequest(DateOnly from, DateOnly to) =>
        new(
            1,
            ProjectionIdentity.Default(),
            new StateStreamKey(2, 101, StateTypes.DeviceConnection),
            from,
            to,
            "late_state",
            ReconciliationRequestStatuses.Processing,
            1,
            null,
            "worker-1",
            1,
            DateTimeOffset.UtcNow.AddMinutes(1),
            1,
            DateTimeOffset.UtcNow,
            null);

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
