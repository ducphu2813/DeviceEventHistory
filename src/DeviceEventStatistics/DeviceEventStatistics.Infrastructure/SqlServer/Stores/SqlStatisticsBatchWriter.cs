using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Infrastructure.SqlServer.Execution;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlStatisticsBatchWriter(
    SqlStatisticsDbContext dbContext,
    SqlProjectionBatchOperations operations,
    SqlProjectionCheckpointStore checkpointStore,
    SqlRetryPolicy retryPolicy,
    SqlProjectionWriterOptions writerOptions,
    StateDurationCalculator stateDurationCalculator,
    LocalStatisticsDateResolver dateResolver,
    TimeProvider timeProvider)
    : IStatisticsBatchWriter
{
    public Task<CommittedBatchResult> PersistAsync(
        ProjectionBatch batch,
        CancellationToken cancellationToken = default) =>
        retryPolicy.ExecuteAsync(
            token => PersistAttemptAsync(batch, token),
            writerOptions.MaxAttempts,
            writerOptions.MinimumRetryDelay,
            writerOptions.MaximumRetryDelay,
            cancellationToken);

    private async Task<CommittedBatchResult> PersistAttemptAsync(
        ProjectionBatch input,
        CancellationToken cancellationToken)
    {
        var batch = Normalize(input);
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);

        var newEventIds = await operations.InsertNewProcessedEventsAsync(
            session,
            batch.Identity,
            batch.ProcessedEvents,
            batch.Lease,
            cancellationToken);

        var stateResult = await CalculateStateAsync(
            session,
            batch,
            newEventIds,
            cancellationToken);

        var affectedRows = 0;
        affectedRows += await operations.UpsertMetricDailyAsync(
            session,
            batch.Identity,
            batch.MetricContributions,
            batch.ProcessedEvents,
            newEventIds,
            batch.Lease,
            cancellationToken);
        affectedRows += await operations.UpsertDeviceSummariesAsync(
            session,
            batch.Identity,
            batch.DeviceSummaries,
            batch.ProcessedEvents,
            newEventIds,
            batch.Lease,
            cancellationToken);
        affectedRows += await operations.UpsertQualityDailyAsync(
            session,
            batch.Identity,
            batch.QualityContributions,
            batch.ProcessedEvents,
            newEventIds,
            batch.Lease,
            cancellationToken);
        var insertedFailureCount = await operations.InsertFailuresAsync(
            session,
            batch.Identity,
            batch.Failures,
            batch.Lease,
            cancellationToken);
        affectedRows += insertedFailureCount;
        affectedRows += await operations.UpsertStateDailyAsync(
            session,
            batch.Identity,
            stateResult.DailyChanges,
            batch.Lease,
            cancellationToken);
        affectedRows += await operations.UpsertStateCursorsAsync(
            session,
            batch.Identity,
            stateResult.Cursors,
            batch.Lease,
            cancellationToken);
        affectedRows += await operations.MarkStateDaysDirtyAsync(
            session,
            batch.Identity,
            stateResult.DirtyRanges,
            batch.Lease,
            cancellationToken);
        affectedRows += await operations.UpsertReconciliationRequestsAsync(
            session,
            batch.Identity,
            stateResult.ReconciliationRequests,
            batch.Lease,
            cancellationToken);

        var failureDataChanged = insertedFailureCount > 0;
        var stateDataChanged = stateResult.DailyChanges.Count > 0 ||
                               stateResult.Cursors.Count > 0 ||
                               stateResult.DirtyRanges.Count > 0 ||
                               stateResult.ReconciliationRequests.Count > 0;
        var dataRevision = batch.ExpectedCheckpoint.DataRevision +
                           (newEventIds.Count > 0 || failureDataChanged || stateDataChanged ? 1 : 0);
        var nextCheckpoint = batch.NextCheckpoint with { DataRevision = dataRevision };
        var checkpointAdvanced = await operations.AdvanceCheckpointAsync(
            session,
            batch.ExpectedCheckpoint,
            nextCheckpoint,
            batch.Lease,
            cancellationToken);
        var committedDataRevision = dataRevision;
        if (!checkpointAdvanced &&
            !await checkpointStore.IsEquivalentAsync(
                session,
                nextCheckpoint,
                batch.Lease,
                cancellationToken: cancellationToken))
        {
            if (!await checkpointStore.IsEquivalentAsync(
                    session,
                    nextCheckpoint,
                    batch.Lease,
                    allowOneRevisionAhead: true,
                    cancellationToken: cancellationToken))
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_CHECKPOINT_CONFLICT);
            }

            committedDataRevision++;
        }

        await session.CommitAsync(cancellationToken);
        return new CommittedBatchResult(
            newEventIds.Count,
            batch.ProcessedEvents.Count - newEventIds.Count,
            CountNew(batch.ProcessedEvents, newEventIds, ProjectionEventDisposition.Aggregated),
            CountNew(batch.ProcessedEvents, newEventIds, ProjectionEventDisposition.Ignored),
            CountNew(batch.ProcessedEvents, newEventIds, ProjectionEventDisposition.QualityOnly),
            CountNew(batch.ProcessedEvents, newEventIds, ProjectionEventDisposition.FailedTerminal),
            affectedRows,
            nextCheckpoint with { DataRevision = committedDataRevision },
            committedDataRevision);
    }

    private static ProjectionBatch Normalize(ProjectionBatch batch)
    {
        if (batch.ExpectedCheckpoint.Identity != batch.Identity ||
            batch.NextCheckpoint.Identity != batch.Identity ||
            batch.Lease.Identity != batch.Identity)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_BATCH_INVALID);
        }

        var processedEvents = batch.ProcessedEvents
            .GroupBy(value => value.EventId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                if (group.Any(value => value != first))
                {
                    throw new InvalidOperationException(
                        StatisticsContractConstants.Messages.MSG_PROJECTION_BATCH_INVALID);
                }

                return first;
            })
            .ToArray();
        var processedIds = processedEvents
            .Select(value => value.EventId)
            .ToHashSet(StringComparer.Ordinal);
        if (batch.MetricContributions.Any(value => !processedIds.Contains(value.EventId)) ||
            batch.DeviceSummaries.Any(value => !processedIds.Contains(value.EventId)) ||
            batch.QualityContributions.Any(value => !processedIds.Contains(value.EventId)) ||
            batch.StateObservations.Any(value => !processedIds.Contains(value.EventId)))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_BATCH_INVALID);
        }

        return batch with
        {
            ProcessedEvents = processedEvents,
            MetricContributions = batch.MetricContributions
                .DistinctBy(value => new { value.EventId, value.MetricKey, value.SourceKind, value.StatisticsDate })
                .ToArray(),
            DeviceSummaries = batch.DeviceSummaries
                .DistinctBy(value => new { value.EventId, value.StatisticsDate, value.SourceKind })
                .ToArray(),
            QualityContributions = batch.QualityContributions
                .DistinctBy(value => new { value.EventId, value.QualityCode })
                .ToArray(),
            Failures = batch.Failures
                .DistinctBy(value => value.FailureId)
                .ToArray()
        };
    }

    private async Task<StatePersistenceResult> CalculateStateAsync(
        SqlProjectionSession session,
        ProjectionBatch batch,
        IReadOnlySet<string> newEventIds,
        CancellationToken cancellationToken)
    {
        var observations = batch.StateObservations
            .Where(value => newEventIds.Contains(value.EventId))
            .ToArray();
        if (observations.Length == 0)
        {
            return StatePersistenceResult.Empty;
        }

        var cursors = await operations.LoadStateCursorsAsync(
            session,
            batch.Identity,
            observations,
            batch.Lease,
            cancellationToken);
        var asOfAtUtc = batch.NextCheckpoint.LastProcessedAtUtc ?? timeProvider.GetUtcNow();
        var dailyChanges = new List<StateDailyContribution>();
        var cursorChanges = new List<StateCursorInput>();
        var dirtyRanges = new List<StateDirtyRange>();

        foreach (var stream in observations.GroupBy(value =>
                     new StateStreamKey(value.CompanyId, value.DeviceId, value.StateType)))
        {
            var domainObservations = stream
                .Select(value => new StateObservation(
                    value.EventId,
                    stream.Key,
                    value.ObservedState,
                    value.TimelineAtUtc,
                    value.OpeningEvidenceKind ?? StateEvidenceKinds.ObservedEvent))
                .ToArray();
            var cursor = cursors.GetValueOrDefault(stream.Key);
            var bucketDates = domainObservations
                .Select(value => dateResolver.Resolve(value.TimelineAtUtc).StatisticsDate)
                .Concat(cursor is null
                    ? []
                    : [dateResolver.Resolve(cursor.AccountedThroughAtUtc).StatisticsDate])
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            var bucketMap = bucketDates.ToDictionary(
                date => date,
                date =>
                {
                    var bucket = dateResolver.Resolve(date);
                    return new StateBucket(
                        date,
                        bucket.BucketStartAtUtc,
                        bucket.BucketEndAtUtc,
                        bucket.TimeZoneId);
                });
            var result = stateDurationCalculator.Calculate(cursor, domainObservations, bucketMap, asOfAtUtc);

            dailyChanges.AddRange(result.DailyChanges.Select(value => new StateDailyContribution(
                value.Key,
                value.Bucket.StatisticsDate,
                value.Bucket.BucketStartAtUtc,
                value.Bucket.BucketEndAtUtc,
                value.CalculatedThroughAtUtc,
                value.Bucket.TimeZoneId,
                value.OpeningState,
                value.ClosingState,
                value.OnlineSeconds,
                value.OfflineSeconds,
                value.UnknownSeconds,
                value.ConnectedEventCount,
                value.DisconnectedEventCount,
                value.ReconnectCount,
                value.OpeningEvidenceKind,
                value.OpeningEvidenceEventId,
                value.IsDirty,
                value.IsFinalized,
                value.CoverageStatus)));
            if (result.Cursor is not null)
            {
                cursorChanges.Add(new StateCursorInput(
                    result.Cursor.Key,
                    result.Cursor.CurrentState,
                    result.Cursor.StateSinceAtUtc,
                    result.Cursor.AccountedThroughAtUtc,
                    result.Cursor.LastTimelineAtUtc,
                    result.Cursor.LastEventId,
                    result.Cursor.OpeningEvidenceKind));
            }

            dirtyRanges.AddRange(result.DirtyRanges);
        }

        var requests = dirtyRanges
            .GroupBy(value => new { value.Key, value.ReasonCode })
            .Select(group => new ReconciliationRequestInput(
                group.Key.Key,
                group.Min(value => value.FromStatisticsDate),
                group.Max(value => value.ToStatisticsDate),
                group.Key.ReasonCode,
                asOfAtUtc,
                group.First().EvidenceEventId))
            .ToArray();
        return new StatePersistenceResult(dailyChanges, cursorChanges, dirtyRanges, requests);
    }

    private sealed record StatePersistenceResult(
        IReadOnlyList<StateDailyContribution> DailyChanges,
        IReadOnlyList<StateCursorInput> Cursors,
        IReadOnlyList<StateDirtyRange> DirtyRanges,
        IReadOnlyList<ReconciliationRequestInput> ReconciliationRequests)
    {
        public static StatePersistenceResult Empty { get; } = new([], [], [], []);
    }

    private static int CountNew(
        IEnumerable<ProcessedEventInput> events,
        IReadOnlySet<string> newEventIds,
        ProjectionEventDisposition disposition) =>
        events.Count(value =>
            newEventIds.Contains(value.EventId) && value.Outcome == disposition);
}
