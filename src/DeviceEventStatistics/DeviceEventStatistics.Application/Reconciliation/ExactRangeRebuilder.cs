using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.Coverage;
using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.Application.Reconciliation;

public sealed class ExactRangeRebuilder(
    IHistoryRangeReader historyReader,
    ProjectionEventOutcomeMapper outcomeMapper,
    IMetricKeyResolver metricKeyResolver,
    LocalStatisticsDateResolver dateResolver,
    StateDurationCalculator stateDurationCalculator,
    ProjectionCoveragePolicy coveragePolicy,
    TimeProvider timeProvider)
{
    public async Task<ReconciliationSourceResult> RebuildAsync(
        ReconciliationSnapshot snapshot,
        ReconciliationExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        var membership = snapshot.Membership
            .Select(value => value.EventId)
            .ToHashSet(StringComparer.Ordinal);
        var events = await ReadEventsAsync(snapshot, options.PageSize, cancellationToken);
        var fetchedIds = events
            .Where(value => value.EventId is not null)
            .Select(value => value.EventId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var decision = coveragePolicy.Evaluate(
            snapshot.Claim,
            snapshot,
            fetchedIds,
            timeProvider.GetUtcNow(),
            options);
        if (!decision.IsAllowed)
        {
            throw new ReconciliationCoverageException(
                decision.ReasonCode ?? ReconciliationReasonCodes.CoverageUnavailable);
        }

        var admitsSourceEvents = snapshot.Claim.Request.ReasonCode is
            ReconciliationReasonCodes.Bootstrap or
            ReconciliationReasonCodes.Backfill or
            ReconciliationReasonCodes.Rebuild;
        var sourceEvents = events
            .Where(value => value.EventId is string eventId &&
                            (admitsSourceEvents || membership.Contains(eventId)))
            .Where(value => !admitsSourceEvents ||
                            value.CompanyId == snapshot.Claim.Request.Key.CompanyId)
            .OrderBy(value => value.TimelineAtUtc)
            .ThenBy(value => value.EventId, StringComparer.Ordinal)
            .ToArray();
        var outcomes = sourceEvents.Select(outcomeMapper.Map).ToArray();
        var targetOutcomes = outcomes
            .Where(value => value.Event.CompanyId == snapshot.Claim.Request.Key.CompanyId &&
                            value.Event.DeviceId == snapshot.Claim.Request.Key.DeviceId)
            .ToArray();
        var metricCodes = targetOutcomes
            .SelectMany(value => value.Metrics)
            .Select(value => value.MetricCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var metricKeys = await metricKeyResolver.ResolveAsync(
            options.MetricSetVersion,
            metricCodes,
            cancellationToken);
        var missingMetricCodes = metricCodes.Where(value => !metricKeys.ContainsKey(value)).ToArray();
        if (missingMetricCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_METRIC_KEY_MISSING,
                    string.Join(',', missingMetricCodes)));
        }

        var metrics = targetOutcomes
            .SelectMany(value => value.Metrics)
            .Select(value => new MetricContribution(
                value.EventId,
                value.CompanyId,
                value.DeviceId,
                value.StatisticsDate,
                metricKeys[value.MetricCode],
                value.SourceKind,
                value.TimelineAtUtc,
                value.SourcePersistedAtUtc,
                value.ParsedWithWarnings,
                value.TimeBasis))
            .ToArray();
        var summaries = targetOutcomes
            .Where(value => value.Disposition == ProjectionEventDisposition.Aggregated)
            .Where(value => value.Event.CompanyId is > 0 && value.Event.DeviceId is > 0)
            .Select(value => new DeviceSummaryContribution(
                value.Event.EventId!,
                value.Event.CompanyId!.Value,
                value.Event.DeviceId!.Value,
                dateResolver.Resolve(value.Event.TimelineAtUtc!.Value).StatisticsDate,
                value.Event.SourceKind!,
                value.Event.Facts.DeviceError is not null,
                string.Equals(value.Event.ParseStatus, "parsed_with_warnings", StringComparison.OrdinalIgnoreCase),
                value.Event.TimelineAtUtc.Value))
            .ToArray();
        var quality = outcomes
            .Where(value => value.Event.CompanyId == snapshot.Claim.Request.Key.CompanyId)
            .SelectMany(value => value.Quality)
            .Select(value => new QualityContribution(
                value.EventId,
                value.QualityIdentity,
                value.StatisticsDate,
                value.CompanyId,
                value.SourceKind,
                value.SourceId,
                value.QualityCode,
                value.SeenAtUtc))
            .ToArray();
        var processed = targetOutcomes
            .Where(value => value.Event.EventId is not null && value.Event.PersistedAtUtc is not null)
            .Select(value => new ProcessedEventInput(
                value.Event.EventId!,
                value.Event.SourceDocumentId,
                value.Event.SourceKind ?? string.Empty,
                value.Event.PersistedAtUtc!.Value,
                value.Event.TimelineAtUtc is DateTimeOffset timeline
                    ? dateResolver.Resolve(timeline).StatisticsDate
                    : null,
                value.Event.TimelineAtUtc,
                options.MappingVersion,
                value.Disposition))
            .ToArray();
        var stateResult = BuildState(snapshot, targetOutcomes, decision);

        return new ReconciliationSourceResult(
            processed,
            metrics,
            summaries,
            stateResult.Daily,
            stateResult.Cursors,
            quality,
            BuildCoverage(snapshot, decision),
            sourceEvents.Length);
    }

    private async Task<IReadOnlyList<HistoryEvent>> ReadEventsAsync(
        ReconciliationSnapshot snapshot,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = new List<HistoryEvent>();
        SourceCursor? cursor = null;
        while (true)
        {
            var page = await historyReader.ReadRangePageAsync(
                snapshot.FromTimelineAtUtc,
                snapshot.ToTimelineAtUtc,
                cursor,
                pageSize,
                null,
                null,
                cancellationToken);
            result.AddRange(page.Events);
            if (page.IsCaughtUp || page.NextCursor == cursor)
            {
                return result
                    .GroupBy(value => value.EventId, StringComparer.Ordinal)
                    .Select(value => value.First())
                    .ToArray();
            }

            cursor = page.NextCursor;
        }
    }

    private StateRebuildResult BuildState(
        ReconciliationSnapshot snapshot,
        IReadOnlyCollection<ProjectionEventOutcome> outcomes,
        CoverageDecision coverage)
    {
        var observations = outcomes
            .Where(value => value.Disposition == ProjectionEventDisposition.Aggregated)
            .Select(value => StateObservationFactory.Create(value.Event, dateResolver))
            .Where(value => value is not null)
            .Select(value => value!)
            .Where(value => value.Key.StateType == snapshot.Claim.Request.Key.StateType)
            .GroupBy(value => value.Key)
            .ToDictionary(value => value.Key, value => (IReadOnlyList<StateObservation>)value.ToArray());
        var streamKeys = observations.Keys
            .Concat(snapshot.OpeningCursors.Keys)
            .Distinct()
            .ToArray();
        var daily = new List<StateDailyContribution>();
        var cursors = new List<StateCursorInput>();
        var asOfAtUtc = snapshot.ToTimelineAtUtc <= timeProvider.GetUtcNow()
            ? snapshot.ToTimelineAtUtc
            : timeProvider.GetUtcNow();
        foreach (var streamKey in streamKeys)
        {
            var dates = Enumerable.Range(
                    0,
                    snapshot.Claim.Request.ToStatisticsDate.DayNumber -
                    snapshot.Claim.Request.FromStatisticsDate.DayNumber + 1)
                .Select(offset => snapshot.Claim.Request.FromStatisticsDate.AddDays(offset))
                .ToArray();
            var buckets = dates.ToDictionary(
                value => value,
                value =>
                {
                    var bucket = dateResolver.Resolve(value);
                    return new StateBucket(value, bucket.BucketStartAtUtc, bucket.BucketEndAtUtc, bucket.TimeZoneId);
                });
            var result = stateDurationCalculator.Calculate(
                snapshot.OpeningCursors.GetValueOrDefault(streamKey),
                observations.GetValueOrDefault(streamKey, []),
                buckets,
                asOfAtUtc);
            daily.AddRange(result.DailyChanges.Select(value => ToDailyContribution(value, coverage)));
            if (result.Cursor is not null)
            {
                cursors.Add(ToCursorInput(result.Cursor));
            }
        }

        return new StateRebuildResult(daily, cursors);
    }

    private IReadOnlyList<ProjectionCoverageInput> BuildCoverage(
        ReconciliationSnapshot snapshot,
        CoverageDecision decision)
    {
        var result = new List<ProjectionCoverageInput>();
        for (var date = snapshot.Claim.Request.FromStatisticsDate;
             date <= snapshot.Claim.Request.ToStatisticsDate;
             date = date.AddDays(1))
        {
            var bucket = dateResolver.Resolve(date);
            result.Add(new ProjectionCoverageInput(
                snapshot.Claim.Request.Key.CompanyId,
                snapshot.Claim.Request.Key.DeviceId,
                date,
                ProjectionCoverageKinds.Activity,
                decision.Status,
                bucket.BucketStartAtUtc,
                bucket.BucketEndAtUtc,
                decision.ReasonCode));
            result.Add(new ProjectionCoverageInput(
                snapshot.Claim.Request.Key.CompanyId,
                snapshot.Claim.Request.Key.DeviceId,
                date,
                ProjectionCoverageKinds.ForState(snapshot.Claim.Request.Key.StateType),
                decision.Status,
                bucket.BucketStartAtUtc,
                bucket.BucketEndAtUtc,
                decision.ReasonCode));
        }

        return result;
    }

    private static StateDailyContribution ToDailyContribution(
        StateDailyChange value,
        CoverageDecision coverage) =>
        new(
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
            false,
            value.IsFinalized,
            coverage.Status);

    private static StateCursorInput ToCursorInput(StateCursorSnapshot value) =>
        new(
            value.Key,
            value.CurrentState,
            value.StateSinceAtUtc,
            value.AccountedThroughAtUtc,
            value.LastTimelineAtUtc,
            value.LastEventId,
            value.OpeningEvidenceKind);

    private sealed record StateRebuildResult(
        IReadOnlyList<StateDailyContribution> Daily,
        IReadOnlyList<StateCursorInput> Cursors);
}

public sealed class ReconciliationCoverageException(string reasonCode) : Exception(reasonCode);

public sealed class ReconciliationStaleException : Exception
{
    public ReconciliationStaleException()
        : base(StatisticsContractConstants.Messages.MSG_RECONCILIATION_REVISION_STALE)
    {
    }
}

internal static class StateObservationFactory
{
    public static StateObservation? Create(
        HistoryEvent historyEvent,
        LocalStatisticsDateResolver dateResolver)
    {
        if (historyEvent.EventId is null ||
            historyEvent.CompanyId is not > 0 ||
            historyEvent.DeviceId is not > 0 ||
            historyEvent.TimelineAtUtc is not DateTimeOffset timelineAtUtc)
        {
            return null;
        }

        var stateType = historyEvent.Category switch
        {
            StateTypes.DeviceConnection => StateTypes.DeviceConnection,
            StateTypes.ScannerConnection => StateTypes.ScannerConnection,
            _ => null
        };
        if (stateType is null)
        {
            return null;
        }

        var state = historyEvent.Facts.Connection?.Status?.ToLowerInvariant();
        state ??= historyEvent.SourceEventName switch
        {
            "receiveDeviceScanConnect" => ConnectionStates.Connected,
            "receiveDeviceScanDisconnect" => ConnectionStates.Disconnected,
            _ => null
        };
        return state is null
            ? null
            : new StateObservation(
                historyEvent.EventId,
                new StateStreamKey(historyEvent.CompanyId.Value, historyEvent.DeviceId.Value, stateType),
                state,
                timelineAtUtc,
                StateEvidenceKinds.ObservedEvent);
    }
}
