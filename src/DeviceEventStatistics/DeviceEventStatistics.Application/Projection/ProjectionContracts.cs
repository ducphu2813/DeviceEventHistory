using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Projection;

public sealed record ProjectionIdentity(
    string ProjectionName,
    int ProjectionVersion,
    string PartitionKey)
{
    public static ProjectionIdentity Default(int version = 1) =>
        new(StatisticsContractConstants.ProjectionName, version, StatisticsContractConstants.DefaultPartitionKey);
}

public sealed record ProjectionLeaseToken(
    ProjectionIdentity Identity,
    string Owner,
    long Epoch,
    DateTimeOffset ExpiresAtUtc);

public sealed record ProjectionCheckpoint(
    ProjectionIdentity Identity,
    DateTimeOffset? LastPersistedAtUtc = null,
    string? LastEventId = null,
    DateTimeOffset? LastProcessedAtUtc = null,
    int LastBatchSize = 0,
    DateTimeOffset? SweepFromAtUtc = null,
    DateTimeOffset? SweepToAtUtc = null,
    DateTimeOffset? SweepLastPersistedAtUtc = null,
    string? SweepLastEventId = null,
    long DataRevision = 0,
    byte[]? RowVersion = null);

public sealed record IncrementalProjectionOptions(
    ProjectionIdentity Identity,
    string MappingVersion,
    int MetricSetVersion,
    DateTimeOffset CoverageStartAtUtc,
    int BatchSize,
    int MaxContributionsPerBatch,
    TimeSpan OverlapWindow,
    TimeSpan ReadSafetyDelay,
    IReadOnlyCollection<long> CompanyIds,
    IReadOnlyCollection<long> DeviceIds);

public sealed record LeaseAcquireResult(
    bool Acquired,
    ProjectionLeaseToken? Lease,
    DateTimeOffset? CurrentExpiryUtc = null);

public interface IProjectionLeaseStore
{
    Task<LeaseAcquireResult> AcquireAsync(
        ProjectionIdentity identity,
        string owner,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<ProjectionLeaseToken?> RenewAsync(
        ProjectionLeaseToken lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default);
}

public interface IProjectionCheckpointStore
{
    Task<ProjectionCheckpoint> GetOrCreateAsync(
        ProjectionIdentity identity,
        CancellationToken cancellationToken = default);

    Task<bool> AdvanceAsync(
        ProjectionCheckpoint expected,
        ProjectionCheckpoint next,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default);
}
