using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Reconciliation;

public sealed class ReconciliationCoordinator(
    IReconciliationRequestStore requestStore,
    IProjectionRebuildStore rebuildStore,
    ExactRangeRebuilder rangeRebuilder,
    ForwardStatePropagation propagation,
    IProjectionRecoveryStore recoveryStore,
    TimeProvider timeProvider)
{
    public async Task<ReconciliationRunResult> RunOnceAsync(
        ProjectionIdentity identity,
        ProjectionLeaseToken lease,
        ReconciliationExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        var completed = 0;
        var retried = 0;
        var failed = 0;
        for (var index = 0; index < options.MaximumRequestsPerRun; index++)
        {
            var claim = await requestStore.ClaimNextAsync(
                identity,
                lease,
                options.ClaimDuration,
                options.MaxAttempts,
                cancellationToken);
            if (claim is null)
            {
                break;
            }

            ReconciliationSnapshot? snapshot = null;
            try
            {
                claim = await requestStore.ExtendToCurrentEdgeAsync(
                    claim,
                    lease,
                    options.CurrentEdgeDate,
                    cancellationToken);
                _ = propagation.Split(
                    claim.Request.FromStatisticsDate,
                    claim.Request.ToStatisticsDate,
                    options.CurrentEdgeDate,
                    options.MaximumRangeDays);
                claim = await requestStore.LimitRangeAsync(
                    claim,
                    lease,
                    options.MaximumRangeDays,
                    cancellationToken);

                snapshot = await rebuildStore.CaptureAsync(claim, lease, cancellationToken);
                var run = CreateRun(snapshot);
                await recoveryStore.StartRunAsync(run, lease, cancellationToken);
                var result = await rangeRebuilder.RebuildAsync(snapshot, options, cancellationToken);
                if (!await requestStore.RenewAsync(claim, lease, options.ClaimDuration, cancellationToken))
                {
                    throw new ReconciliationStaleException();
                }

                await rebuildStore.StageAsync(snapshot, result, cancellationToken);
                if (!await requestStore.RenewAsync(claim, lease, options.ClaimDuration, cancellationToken))
                {
                    throw new ReconciliationStaleException();
                }

                await rebuildStore.PublishAsync(snapshot, result, lease, cancellationToken);
                await recoveryStore.CompleteRunAsync(
                    run,
                    lease,
                    ProjectionRunStatuses.Succeeded,
                    result.ReadEventCount,
                    result.MetricContributions.Count + result.DeviceSummaries.Count +
                    result.StateDailyContributions.Count,
                    cancellationToken: cancellationToken);
                completed++;
            }
            catch (ReconciliationCoverageException exception)
            {
                await TryCleanupAsync(snapshot, cancellationToken);
                await TryCompleteRunAsync(snapshot, lease, ProjectionRunStatuses.Failed, exception.Message, cancellationToken);
                await TryFailAsync(claim, lease, exception.Message, true, options, cancellationToken);
                failed++;
            }
            catch (ReconciliationStaleException exception)
            {
                await TryCleanupAsync(snapshot, cancellationToken);
                await TryCompleteRunAsync(snapshot, lease, ProjectionRunStatuses.Failed, exception.Message, cancellationToken);
                await TryFailAsync(claim, lease, exception.Message, false, options, cancellationToken);
                retried++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await TryCleanupAsync(snapshot, cancellationToken);
                await TryCompleteRunAsync(snapshot, lease, ProjectionRunStatuses.Failed, exception.Message, cancellationToken);
                await TryFailAsync(
                    claim,
                    lease,
                    exception.Message,
                    claim.Request.AttemptCount >= options.MaxAttempts,
                    options,
                    cancellationToken);
                retried++;
            }
        }

        return new ReconciliationRunResult(completed, retried, failed, timeProvider.GetUtcNow());
    }

    private async Task TryCompleteRunAsync(
        ReconciliationSnapshot? snapshot,
        ProjectionLeaseToken lease,
        string status,
        string errorSummary,
        CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            return;
        }

        try
        {
            await recoveryStore.CompleteRunAsync(
                CreateRun(snapshot),
                lease,
                status,
                0,
                0,
                errorSummary,
                cancellationToken);
        }
        catch
        {
            // The original request failure remains the source of truth for retry/recovery.
        }
    }

    private static ProjectionRecoveryRun CreateRun(ReconciliationSnapshot snapshot)
    {
        var request = snapshot.Claim.Request;
        var runType = request.ReasonCode switch
        {
            ReconciliationReasonCodes.Bootstrap => "bootstrap",
            ReconciliationReasonCodes.Backfill => "backfill",
            ReconciliationReasonCodes.Rebuild => "rebuild",
            _ => "reconciliation"
        };
        return new ProjectionRecoveryRun(
            request.Identity,
            snapshot.RunId,
            runType,
            request.FromStatisticsDate,
            request.ToStatisticsDate,
            request.Key.CompanyId,
            request.Key.DeviceId,
            request.RequestedAtUtc);
    }

    private async Task TryFailAsync(
        ReconciliationClaim claim,
        ProjectionLeaseToken lease,
        string reason,
        bool permanent,
        ReconciliationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        await requestStore.FailAsync(
            claim,
            lease,
            reason,
            permanent,
            options.RetryDelay,
            cancellationToken);
    }

    private async Task TryCleanupAsync(
        ReconciliationSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot is not null)
        {
            await rebuildStore.CleanupAsync(snapshot.RunId, cancellationToken);
        }
    }
}

public sealed record ReconciliationRunResult(
    int CompletedCount,
    int RetriedCount,
    int FailedCount,
    DateTimeOffset CompletedAtUtc);
