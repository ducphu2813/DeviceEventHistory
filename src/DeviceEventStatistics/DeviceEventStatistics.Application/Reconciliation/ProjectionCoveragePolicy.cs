using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.Coverage;
using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.Application.Reconciliation;

public sealed class ProjectionCoveragePolicy
{
    public CoverageDecision Evaluate(
        ReconciliationClaim claim,
        ReconciliationSnapshot snapshot,
        IReadOnlyCollection<string> fetchedEventIds,
        DateTimeOffset now,
        ReconciliationExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(fetchedEventIds);
        ArgumentNullException.ThrowIfNull(options);

        if (claim.Request.FromStatisticsDate > claim.Request.ToStatisticsDate ||
            claim.Request.Key.CompanyId <= 0 ||
            claim.Request.Key.DeviceId <= 0 ||
            !StateTypes.Supported.Contains(claim.Request.Key.StateType))
        {
            return CoverageDecision.Unrecoverable(
                ReconciliationReasonCodes.InvalidRequest);
        }

        var retentionBoundary = now - options.HistoryRetention + options.MinimumHistoryHeadroom;
        if (snapshot.FromTimelineAtUtc < retentionBoundary)
        {
            return CoverageDecision.Unrecoverable(ReconciliationReasonCodes.SourceRetentionGap);
        }

        var membership = snapshot.Membership.Select(value => value.EventId).ToHashSet(StringComparer.Ordinal);
        var fetched = fetchedEventIds.ToHashSet(StringComparer.Ordinal);
        var missing = membership.Where(value => !fetched.Contains(value)).ToArray();
        if (missing.Length > 0)
        {
            return CoverageDecision.Unrecoverable(
                ReconciliationReasonCodes.SourceIdentityMissing);
        }

        if (snapshot.ToTimelineAtUtc > now)
        {
            return CoverageDecision.Partial(ReconciliationReasonCodes.CurrentRangeOpen);
        }

        if (snapshot.OpeningCursors.Count == 0)
        {
            return CoverageDecision.Partial(ReconciliationReasonCodes.OpeningStateEvidenceMissing);
        }

        return CoverageDecision.Complete();
    }
}

public sealed record CoverageDecision(
    string Status,
    string? ReasonCode)
{
    public bool IsAllowed => Status is ProjectionCoverageStatuses.Complete or ProjectionCoverageStatuses.Partial;

    public static CoverageDecision Complete() =>
        new(ProjectionCoverageStatuses.Complete, null);

    public static CoverageDecision Partial(string reasonCode) =>
        new(ProjectionCoverageStatuses.Partial, reasonCode);

    public static CoverageDecision Unrecoverable(string reasonCode) =>
        new(ProjectionCoverageStatuses.Unrecoverable, reasonCode);
}
