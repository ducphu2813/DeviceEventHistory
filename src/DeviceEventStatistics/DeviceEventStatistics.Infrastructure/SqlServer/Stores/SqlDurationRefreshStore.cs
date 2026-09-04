using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.State;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Stores;

public sealed class SqlDurationRefreshStore(
    SqlStatisticsDbContext dbContext,
    SqlProjectionBatchOperations operations,
    SqlProjectionCheckpointStore checkpointStore,
    StateDurationCalculator calculator,
    LocalStatisticsDateResolver dateResolver)
    : IDurationRefreshStore
{
    public async Task<int> RefreshAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        DateTimeOffset asOfAtUtc,
        int maxStreams,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStore.GetOrCreateAsync(identity, cancellationToken);
        await using var session = await dbContext.OpenSessionAsync(cancellationToken);
        var cursors = await operations.LoadAllStateCursorsAsync(
            session,
            identity,
            lease,
            maxStreams,
            cancellationToken);
        var dailyChanges = new List<StateDailyContribution>();
        var cursorChanges = new List<StateCursorInput>();

        foreach (var cursor in cursors)
        {
            var buckets = CreateBuckets(cursor, asOfAtUtc);
            if (buckets.Count == 0)
            {
                continue;
            }

            var result = calculator.Calculate(cursor, [], buckets, asOfAtUtc);
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
            if (result.Cursor is not null && result.Cursor.AccountedThroughAtUtc > cursor.AccountedThroughAtUtc)
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
        }

        if (dailyChanges.Count == 0 || cursorChanges.Count == 0)
        {
            await session.RollbackAsync(cancellationToken);
            return 0;
        }

        var affectedRows = await operations.UpsertStateDailyAsync(
            session,
            identity,
            dailyChanges,
            lease,
            cancellationToken);
        affectedRows += await operations.UpsertStateCursorsAsync(
            session,
            identity,
            cursorChanges,
            lease,
            cancellationToken);

        var nextCheckpoint = checkpoint with
        {
            DataRevision = checkpoint.DataRevision + 1
        };
        if (!await operations.AdvanceCheckpointAsync(
                session,
                checkpoint,
                nextCheckpoint,
                lease,
                cancellationToken))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_PROJECTION_CHECKPOINT_CONFLICT);
        }

        await session.CommitAsync(cancellationToken);
        return affectedRows;
    }

    private IReadOnlyDictionary<DateOnly, StateBucket> CreateBuckets(
        StateCursorSnapshot cursor,
        DateTimeOffset asOfAtUtc)
    {
        var firstDate = dateResolver.Resolve(cursor.AccountedThroughAtUtc).StatisticsDate;
        var lastDate = dateResolver.Resolve(asOfAtUtc).StatisticsDate;
        if (lastDate < firstDate)
        {
            return new Dictionary<DateOnly, StateBucket>();
        }

        var result = new Dictionary<DateOnly, StateBucket>();
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            var bucket = dateResolver.Resolve(date);
            if (bucket.BucketEndAtUtc > cursor.AccountedThroughAtUtc &&
                bucket.BucketStartAtUtc <= asOfAtUtc)
            {
                result[date] = new StateBucket(
                    date,
                    bucket.BucketStartAtUtc,
                    bucket.BucketEndAtUtc,
                    bucket.TimeZoneId);
            }
        }

        return result;
    }
}
