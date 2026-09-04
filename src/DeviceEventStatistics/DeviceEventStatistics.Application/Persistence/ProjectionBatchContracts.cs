namespace DeviceEventStatistics.Application.Persistence;

public enum ProjectionEventDisposition
{
    Aggregated,
    Ignored,
    QualityOnly,
    FailedTerminal
}

public enum EventTimeBasis
{
    Occurred,
    Received
}

public sealed record ProcessedEventInput(
    string EventId,
    string SourceDocumentId,
    string SourceKind,
    DateTimeOffset SourcePersistedAtUtc,
    DateOnly? StatisticsDate,
    DateTimeOffset? TimelineAtUtc,
    string MappingVersion,
    ProjectionEventDisposition Outcome);

public sealed record MetricContribution(
    string EventId,
    long CompanyId,
    long DeviceId,
    DateOnly StatisticsDate,
    int MetricKey,
    string SourceKind,
    DateTimeOffset TimelineAtUtc,
    DateTimeOffset SourcePersistedAtUtc,
    bool ParsedWithWarnings,
    EventTimeBasis TimeBasis);

public sealed record DeviceSummaryContribution(
    string EventId,
    long CompanyId,
    long DeviceId,
    DateOnly StatisticsDate,
    string SourceKind,
    bool IsError,
    bool IsWarning,
    DateTimeOffset TimelineAtUtc);

public sealed record StateObservationInput(
    string EventId,
    long CompanyId,
    long DeviceId,
    DateOnly StatisticsDate,
    string StateType,
    string ObservedState,
    DateTimeOffset TimelineAtUtc,
    string? OpeningEvidenceKind = null);

public sealed record QualityContribution(
    string EventId,
    string QualityIdentity,
    DateOnly StatisticsDate,
    long CompanyId,
    string SourceKind,
    string SourceId,
    string QualityCode,
    DateTimeOffset SeenAtUtc);

public sealed record ProjectionFailureInput(
    string FailureId,
    string SourceEventIdentity,
    string ErrorCode,
    string ErrorStage,
    string ErrorMessage,
    bool Retryable,
    int RetryCount,
    DateTimeOffset FirstFailedAtUtc,
    DateTimeOffset LastFailedAtUtc,
    string? EventId = null,
    long? CompanyId = null,
    long? DeviceId = null,
    string? SourceKind = null,
    string? Category = null,
    string? SourceEventName = null,
    DateTimeOffset? SourcePersistedAtUtc = null);

public sealed record ProjectionBatch(
    Projection.ProjectionIdentity Identity,
    Projection.ProjectionLeaseToken Lease,
    Projection.ProjectionCheckpoint ExpectedCheckpoint,
    Projection.ProjectionCheckpoint NextCheckpoint,
    IReadOnlyList<ProcessedEventInput> ProcessedEvents,
    IReadOnlyList<MetricContribution> MetricContributions,
    IReadOnlyList<DeviceSummaryContribution> DeviceSummaries,
    IReadOnlyList<StateObservationInput> StateObservations,
    IReadOnlyList<QualityContribution> QualityContributions,
    IReadOnlyList<ProjectionFailureInput> Failures)
{
    public static ProjectionBatch Empty(
        Projection.ProjectionLeaseToken lease,
        Projection.ProjectionCheckpoint checkpoint) =>
        new(
            checkpoint.Identity,
            lease,
            checkpoint,
            checkpoint,
            [],
            [],
            [],
            [],
            [],
            []);
}

public sealed record CommittedBatchResult(
    int NewEventCount,
    int DuplicateEventCount,
    int AggregatedEventCount,
    int IgnoredEventCount,
    int QualityOnlyEventCount,
    int FailedTerminalEventCount,
    int AffectedRowCount,
    Projection.ProjectionCheckpoint Checkpoint,
    long DataRevision);

public interface IStatisticsBatchWriter
{
    Task<CommittedBatchResult> PersistAsync(
        ProjectionBatch batch,
        CancellationToken cancellationToken = default);
}
