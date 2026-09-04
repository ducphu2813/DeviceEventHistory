using DeviceEventStatistics.Application.History;

namespace DeviceEventStatistics.Application.Projection;

public sealed record ProjectionSweep(
    DateTimeOffset FromAtUtc,
    DateTimeOffset ToAtUtc,
    SourceCursor? PageCursor,
    DateTimeOffset? HighWatermarkAtUtc,
    string? HighWatermarkEventId)
{
    public static ProjectionSweep Start(
        ProjectionCheckpoint checkpoint,
        DateTimeOffset nowAtUtc,
        DateTimeOffset coverageStartAtUtc,
        TimeSpan overlapWindow,
        TimeSpan readSafetyDelay) =>
        checkpoint.SweepFromAtUtc is DateTimeOffset sweepFrom &&
        checkpoint.SweepToAtUtc is DateTimeOffset sweepTo
            ? new ProjectionSweep(
                sweepFrom,
                sweepTo,
                checkpoint.SweepLastPersistedAtUtc is DateTimeOffset persistedAtUtc &&
                checkpoint.SweepLastEventId is not null
                    ? new SourceCursor(persistedAtUtc, checkpoint.SweepLastEventId)
                    : null,
                checkpoint.LastPersistedAtUtc,
                checkpoint.LastEventId)
            : CreateNew(checkpoint, nowAtUtc, coverageStartAtUtc, overlapWindow, readSafetyDelay);

    public ProjectionCheckpoint ApplyPage(
        ProjectionCheckpoint checkpoint,
        SourceCursor? lastPageCursor,
        int pageSize,
        bool pageIsComplete,
        DateTimeOffset? processedAtUtc = null)
    {
        if (!pageIsComplete)
        {
            return checkpoint with
            {
                SweepFromAtUtc = FromAtUtc,
                SweepToAtUtc = ToAtUtc,
                SweepLastPersistedAtUtc = lastPageCursor?.PersistedAtUtc,
                SweepLastEventId = lastPageCursor?.EventId,
                LastBatchSize = pageSize
            };
        }

        var highWatermark = GetMaximumCursor(checkpoint, lastPageCursor);
        return checkpoint with
        {
            LastPersistedAtUtc = highWatermark?.PersistedAtUtc,
            LastEventId = highWatermark?.EventId,
            LastProcessedAtUtc = processedAtUtc ?? DateTimeOffset.UtcNow,
            LastBatchSize = pageSize,
            SweepFromAtUtc = null,
            SweepToAtUtc = null,
            SweepLastPersistedAtUtc = null,
            SweepLastEventId = null
        };
    }

    private static ProjectionSweep CreateNew(
        ProjectionCheckpoint checkpoint,
        DateTimeOffset nowAtUtc,
        DateTimeOffset coverageStartAtUtc,
        TimeSpan overlapWindow,
        TimeSpan readSafetyDelay)
    {
        var from = checkpoint.LastPersistedAtUtc is DateTimeOffset highWatermark
            ? highWatermark - overlapWindow
            : coverageStartAtUtc;
        var to = nowAtUtc - readSafetyDelay;
        return new ProjectionSweep(
            from > to ? to : from,
            to,
            null,
            checkpoint.LastPersistedAtUtc,
            checkpoint.LastEventId);
    }

    private static SourceCursor? GetMaximumCursor(
        ProjectionCheckpoint checkpoint,
        SourceCursor? candidate)
    {
        var current = checkpoint.LastPersistedAtUtc is DateTimeOffset currentTime &&
                      checkpoint.LastEventId is not null
            ? new SourceCursor(currentTime, checkpoint.LastEventId)
            : null;
        if (current is null) return candidate;
        if (candidate is null || !candidate.IsAfter(current)) return current;
        return candidate;
    }
}
