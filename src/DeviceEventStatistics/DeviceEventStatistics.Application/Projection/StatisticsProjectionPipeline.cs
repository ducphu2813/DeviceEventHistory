using DeviceEventStatistics.Application.Persistence;

namespace DeviceEventStatistics.Application.Projection;

public sealed record ProjectionPageResult(
    CommittedBatchResult Commit,
    int ReadEventCount,
    bool IsCaughtUp);

public sealed class StatisticsProjectionPipeline(
    IncrementalProjectionHandler handler,
    IStatisticsBatchWriter batchWriter)
{
    public async Task<ProjectionPageResult> ExecutePageAsync(
        IncrementalProjectionOptions options,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        var prepared = await handler.PreparePageAsync(options, lease, cancellationToken);
        var committed = await batchWriter.PersistAsync(prepared.Batch, cancellationToken);
        return new ProjectionPageResult(committed, prepared.ReadEventCount, prepared.IsCaughtUp);
    }
}
