using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.History;

public sealed class HistoryContractAuditHandler(
    IHistoryContractAuditReader auditReader,
    IProjectionCheckpointStore checkpointStore,
    IncrementalProjectionHandler projectionHandler,
    TimeProvider timeProvider)
{
    public async Task<PreparedAuditPage> PreparePageAsync(
        IncrementalProjectionOptions options,
        ProjectionLeaseToken lease,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStore.GetOrCreateAsync(
            options.Identity,
            cancellationToken);
        var audit = await auditReader.ReadAuditPageAsync(
            checkpoint.AuditLastSourceDocumentId,
            options.BatchSize,
            cancellationToken);

        if (!audit.IsComplete && string.IsNullOrWhiteSpace(audit.NextSourceDocumentId))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_AUDIT_CURSOR_MISSING);
        }

        if (!audit.IsComplete &&
            string.Equals(
                audit.NextSourceDocumentId,
                checkpoint.AuditLastSourceDocumentId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.MSG_AUDIT_CURSOR_NOT_ADVANCED);
        }

        var now = timeProvider.GetUtcNow();
        var startsNewCycle = checkpoint.AuditLastSourceDocumentId is null;
        var nextCheckpoint = checkpoint with
        {
            AuditLastSourceDocumentId = audit.IsComplete
                ? null
                : audit.NextSourceDocumentId,
            AuditStartedAtUtc = startsNewCycle
                ? now
                : checkpoint.AuditStartedAtUtc ?? now,
            AuditCompletedAtUtc = audit.IsComplete
                ? now
                : startsNewCycle
                    ? null
                    : checkpoint.AuditCompletedAtUtc,
            AuditCycle = audit.IsComplete
                ? checked(checkpoint.AuditCycle + 1)
                : checkpoint.AuditCycle
        };

        var projectionPage = await projectionHandler.PrepareEventsAsync(
            options,
            lease,
            checkpoint,
            nextCheckpoint,
            audit.Events,
            audit.IsComplete,
            cancellationToken);
        return new PreparedAuditPage(projectionPage, audit.IsComplete);
    }
}
