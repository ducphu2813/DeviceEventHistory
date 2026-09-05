using DeviceEventStatistics.Application.History;
using DeviceEventStatistics.Application.Mapping;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Application.Reconciliation;
using DeviceEventStatistics.Application.Time;
using DeviceEventStatistics.Domain.Common;
using DeviceEventStatistics.Domain.State;

namespace DeviceEventStatistics.Application.Projection;

public sealed record PreparedProjectionPage(
    ProjectionBatch Batch,
    bool IsCaughtUp,
    int ReadEventCount);

public sealed class IncrementalProjectionHandler(
    IHistoryEventReader historyReader,
    IProjectionCheckpointStore checkpointStore,
    ProjectionEventOutcomeMapper outcomeMapper,
    IMetricKeyResolver metricKeyResolver,
    LocalStatisticsDateResolver dateResolver,
    TimeProvider timeProvider)
{
    public async Task<PreparedProjectionPage> PreparePageAsync(
        IncrementalProjectionOptions options,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        if (lease.Identity != options.Identity)
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_SQL_LEASE_IDENTITY_MISMATCH);
        }

        var checkpoint = await checkpointStore.GetOrCreateAsync(options.Identity, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var sweep = ProjectionSweep.Start(
            checkpoint,
            now,
            options.CoverageStartAtUtc,
            options.OverlapWindow,
            options.ReadSafetyDelay);

        var readResult = await historyReader.ReadPageAsync(
            sweep.FromAtUtc,
            sweep.ToAtUtc,
            sweep.PageCursor,
            options.BatchSize,
            options.CompanyIds,
            options.DeviceIds,
            cancellationToken);

        var outcomes = readResult.Events.Select(outcomeMapper.Map).ToArray();
        ValidateSourceOrder(readResult.Events, sweep.PageCursor);
        ValidateUniqueEventOutcomes(outcomes);

        var metricCodes = outcomes
            .SelectMany(outcome => outcome.Metrics)
            .Select(metric => metric.MetricCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var metricKeys = await metricKeyResolver.ResolveAsync(
            options.MetricSetVersion,
            metricCodes,
            cancellationToken);
        var missingMetricCodes = metricCodes
            .Where(metricCode => !metricKeys.ContainsKey(metricCode))
            .ToArray();
        if (missingMetricCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_METRIC_KEY_MISSING,
                    string.Join(',', missingMetricCodes)));
        }

        var processedEvents = outcomes
            .Where(outcome => outcome.Event.EventId is string eventId &&
                              IsLowercaseSha256(eventId) &&
                              outcome.Event.PersistedAtUtc is not null)
            .Select(outcome => new ProcessedEventInput(
                outcome.Event.EventId!,
                outcome.Event.SourceDocumentId,
                outcome.Event.SourceKind ?? string.Empty,
                outcome.Event.PersistedAtUtc!.Value,
                outcome.Event.TimelineAtUtc is DateTimeOffset timeline
                    ? dateResolver.Resolve(timeline).StatisticsDate
                    : null,
                outcome.Event.TimelineAtUtc,
                options.MappingVersion,
                outcome.Disposition))
            .ToArray();

        var metricContributions = outcomes
            .SelectMany(outcome => outcome.Metrics)
            .Select(metric => new MetricContribution(
                metric.EventId,
                metric.CompanyId,
                metric.DeviceId,
                metric.StatisticsDate,
                metricKeys[metric.MetricCode],
                metric.SourceKind,
                metric.TimelineAtUtc,
                metric.SourcePersistedAtUtc,
                metric.ParsedWithWarnings,
                metric.TimeBasis))
            .ToArray();
        var qualityContributions = outcomes
            .SelectMany(outcome => outcome.Quality)
            .Select(quality => new QualityContribution(
                quality.EventId,
                quality.QualityIdentity,
                quality.StatisticsDate,
                quality.CompanyId,
                quality.SourceKind,
                quality.SourceId,
                quality.QualityCode,
                quality.SeenAtUtc))
            .ToArray();
        var summaries = outcomes
            .Where(outcome => outcome.Disposition is ProjectionEventDisposition.Aggregated)
            .Select(outcome => new DeviceSummaryContribution(
                outcome.Event.EventId!,
                outcome.Event.CompanyId!.Value,
                outcome.Event.DeviceId!.Value,
                outcome.Metrics.First().StatisticsDate,
                outcome.Event.SourceKind!,
                outcome.Event.Facts.DeviceError is not null,
                string.Equals(outcome.Event.ParseStatus, "parsed_with_warnings", StringComparison.OrdinalIgnoreCase),
                outcome.Event.TimelineAtUtc!.Value))
            .ToArray();
        var failures = outcomes
            .Where(outcome => outcome.Failure is not null)
            .Select(outcome => outcome.Failure!)
            .ToArray();
        var stateObservations = outcomes
            .Where(outcome => outcome.Disposition == ProjectionEventDisposition.Aggregated)
            .Select(outcome => StateObservationFactory.Create(outcome.Event, dateResolver))
            .Where(value => value is not null)
            .Select(value => new StateObservationInput(
                value!.EventId,
                value.Key.CompanyId,
                value.Key.DeviceId,
                dateResolver.Resolve(value.TimelineAtUtc).StatisticsDate,
                value.Key.StateType,
                value.ObservedState,
                value.TimelineAtUtc,
                value.OpeningEvidenceKind))
            .ToArray();

        var contributionCount = metricContributions.Length + qualityContributions.Length + summaries.Length;
        if (contributionCount > options.MaxContributionsPerBatch)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_CONTRIBUTION_LIMIT,
                    contributionCount,
                    options.MaxContributionsPerBatch));
        }

        var nextCheckpoint = sweep.ApplyPage(
            checkpoint,
            readResult.NextCursor,
            readResult.Events.Count,
            readResult.IsCaughtUp,
            now);
        var batch = new ProjectionBatch(
            options.Identity,
            lease,
            checkpoint,
            nextCheckpoint,
            processedEvents,
            metricContributions,
            summaries,
            stateObservations,
            qualityContributions,
            failures);
        return new PreparedProjectionPage(batch, readResult.IsCaughtUp, readResult.Events.Count);
    }

    private static void ValidateSourceOrder(
        IReadOnlyList<HistoryEvent> events,
        SourceCursor? pageCursor)
    {
        var previous = pageCursor;
        foreach (var historyEvent in events)
        {
            var current = SourceCursor.From(historyEvent);
            if (current is null)
            {
                continue;
            }

            if (previous is not null && !current.IsAfter(previous))
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_BATCH_INVALID);
            }

            previous = current;
        }
    }

    private static void ValidateUniqueEventOutcomes(
        IReadOnlyCollection<ProjectionEventOutcome> outcomes)
    {
        var seen = new Dictionary<string, ProjectionEventDisposition>(StringComparer.Ordinal);
        foreach (var outcome in outcomes)
        {
            if (outcome.Event.EventId is not string eventId)
            {
                continue;
            }

            if (seen.TryGetValue(eventId, out var existing) && existing != outcome.Disposition)
            {
                throw new InvalidOperationException(
                    StatisticsContractConstants.Messages.MSG_PROJECTION_BATCH_INVALID);
            }

            seen[eventId] = outcome.Disposition;
        }
    }

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

}
