using System.Diagnostics;
using System.Diagnostics.Metrics;
using DeviceEventStatistics.Application.Observability;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Infrastructure.Observability;

public sealed class StatisticsMetrics : IStatisticsTelemetry, IDisposable
{
    public const string MeterName = StatisticsContractConstants.Telemetry.MeterName;

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> sourceEventsRead;
    private readonly Counter<long> newEvents;
    private readonly Counter<long> duplicateEvents;
    private readonly Counter<long> ignoredEvents;
    private readonly Counter<long> qualityOnlyEvents;
    private readonly Counter<long> failedEvents;
    private readonly Counter<long> batchesCommitted;
    private readonly Counter<long> batchesFailed;
    private readonly Counter<long> affectedRows;
    private readonly Counter<long> leaseTransitions;
    private readonly Counter<long> reconciliationCompleted;
    private readonly Counter<long> reconciliationRetried;
    private readonly Counter<long> reconciliationFailed;
    private readonly Counter<long> stagingRowsDeleted;
    private readonly Counter<long> projectionRunsDeleted;
    private readonly Histogram<double> batchDuration;
    private readonly Histogram<double> reconciliationDuration;
    private readonly Histogram<long> pendingRequestCount;
    private readonly Histogram<long> coverageGapCount;
    private readonly Histogram<double> auditCursorAge;
    private readonly Histogram<double> retentionHeadroom;
    private readonly Counter<long> healthEvaluations;

    public StatisticsMetrics()
    {
        sourceEventsRead = meter.CreateCounter<long>("statistics.source.events.read");
        newEvents = meter.CreateCounter<long>("statistics.events.new");
        duplicateEvents = meter.CreateCounter<long>("statistics.events.duplicate");
        ignoredEvents = meter.CreateCounter<long>("statistics.events.ignored");
        qualityOnlyEvents = meter.CreateCounter<long>("statistics.events.quality_only");
        failedEvents = meter.CreateCounter<long>("statistics.events.failed");
        batchesCommitted = meter.CreateCounter<long>("statistics.batches.committed");
        batchesFailed = meter.CreateCounter<long>("statistics.batches.failed");
        affectedRows = meter.CreateCounter<long>("statistics.rows.affected");
        leaseTransitions = meter.CreateCounter<long>("statistics.lease.transitions");
        reconciliationCompleted = meter.CreateCounter<long>("statistics.reconciliation.completed");
        reconciliationRetried = meter.CreateCounter<long>("statistics.reconciliation.retried");
        reconciliationFailed = meter.CreateCounter<long>("statistics.reconciliation.failed");
        stagingRowsDeleted = meter.CreateCounter<long>("statistics.cleanup.staging_rows_deleted");
        projectionRunsDeleted = meter.CreateCounter<long>("statistics.cleanup.projection_runs_deleted");
        batchDuration = meter.CreateHistogram<double>("statistics.batch.duration", "ms");
        reconciliationDuration = meter.CreateHistogram<double>("statistics.reconciliation.duration", "ms");
        pendingRequestCount = meter.CreateHistogram<long>("statistics.reconciliation.pending_requests", "requests");
        coverageGapCount = meter.CreateHistogram<long>("statistics.coverage.gaps", "gaps");
        auditCursorAge = meter.CreateHistogram<double>("statistics.audit.cursor_age", "ms");
        retentionHeadroom = meter.CreateHistogram<double>("statistics.retention.headroom", "ms");
        healthEvaluations = meter.CreateCounter<long>("statistics.health.evaluations");
    }

    public void RecordBatchCommitted(string mode, ProjectionPageResult result, TimeSpan duration)
    {
        var tags = new TagList { { "mode", mode } };
        sourceEventsRead.Add(result.ReadEventCount, tags);
        newEvents.Add(result.Commit.NewEventCount, tags);
        duplicateEvents.Add(result.Commit.DuplicateEventCount, tags);
        ignoredEvents.Add(result.Commit.IgnoredEventCount, tags);
        qualityOnlyEvents.Add(result.Commit.QualityOnlyEventCount, tags);
        failedEvents.Add(result.Commit.FailedTerminalEventCount, tags);
        batchesCommitted.Add(1, tags);
        affectedRows.Add(result.Commit.AffectedRowCount, tags);
        batchDuration.Record(duration.TotalMilliseconds, tags);
    }

    public void RecordBatchFailed(string mode)
    {
        batchesFailed.Add(1, new TagList { { "mode", mode } });
    }

    public void RecordLeaseTransition(string transition)
    {
        leaseTransitions.Add(1, new TagList { { "transition", transition } });
    }

    public void RecordReconciliation(ReconciliationRunResult result, TimeSpan duration)
    {
        reconciliationCompleted.Add(result.CompletedCount);
        reconciliationRetried.Add(result.RetriedCount);
        reconciliationFailed.Add(result.FailedCount);
        reconciliationDuration.Record(duration.TotalMilliseconds);
    }

    public void RecordOperationalCleanup(int deletedStagingRows, int deletedProjectionRuns)
    {
        stagingRowsDeleted.Add(deletedStagingRows);
        projectionRunsDeleted.Add(deletedProjectionRuns);
    }

    public void RecordHealthSnapshot(
        ProjectionOperationalSnapshot snapshot,
        StatisticsHealthEvaluation evaluation)
    {
        pendingRequestCount.Record(snapshot.PendingRequestCount);
        coverageGapCount.Record(snapshot.CoverageGapCount);
        if (evaluation.AuditCursorAge is TimeSpan auditAge)
        {
            auditCursorAge.Record(auditAge.TotalMilliseconds);
        }

        if (evaluation.RetentionHeadroom is TimeSpan headroom)
        {
            retentionHeadroom.Record(headroom.TotalMilliseconds);
        }

        healthEvaluations.Add(
            1,
            new TagList
            {
                { "status", evaluation.Status.ToString().ToLowerInvariant() },
                { "reason", evaluation.Reason }
            });
    }

    public void Dispose() => meter.Dispose();
}
