using System.Diagnostics;
using System.Diagnostics.Metrics;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.Observability;

public sealed class IngestionMetrics : IIngestionTelemetry
{
    private static readonly Meter Meter = new(
        AppConst.Observability.MeterName,
        AppConst.Observability.MeterVersion);

    private readonly Counter<long> filesDiscovered = Meter.CreateCounter<long>(
        AppConst.Observability.MetricFilesDiscovered);
    private readonly Counter<long> sourceAccessFailures = Meter.CreateCounter<long>(
        AppConst.Observability.MetricSourceAccessFailures);
    private readonly Counter<long> bytesRead = Meter.CreateCounter<long>(
        AppConst.Observability.MetricBytesRead);
    private readonly Counter<long> recordsFramed = Meter.CreateCounter<long>(
        AppConst.Observability.MetricRecordsFramed);
    private readonly Counter<long> partialRecords = Meter.CreateCounter<long>(
        AppConst.Observability.MetricPartialRecords);
    private readonly Counter<long> recordsParsed = Meter.CreateCounter<long>(
        AppConst.Observability.MetricRecordsParsed);
    private readonly Counter<long> recordsParseWarnings = Meter.CreateCounter<long>(
        AppConst.Observability.MetricRecordsParseWarnings);
    private readonly Counter<long> recordsParseFailures = Meter.CreateCounter<long>(
        AppConst.Observability.MetricRecordsParseFailures);
    private readonly Counter<long> historyWrites = Meter.CreateCounter<long>(
        AppConst.Observability.MetricHistoryWrites);
    private readonly Counter<long> failureWrites = Meter.CreateCounter<long>(
        AppConst.Observability.MetricFailureWrites);
    private readonly Counter<long> duplicateIdentities = Meter.CreateCounter<long>(
        AppConst.Observability.MetricDuplicateIdentities);
    private readonly Counter<long> checkpointAdvances = Meter.CreateCounter<long>(
        AppConst.Observability.MetricCheckpointAdvances);
    private readonly Counter<long> checkpointFailures = Meter.CreateCounter<long>(
        AppConst.Observability.MetricCheckpointFailures);
    private readonly Counter<long> mongoRetries = Meter.CreateCounter<long>(
        AppConst.Observability.MetricMongoRetries);
    private readonly Counter<long> mongoFailures = Meter.CreateCounter<long>(
        AppConst.Observability.MetricMongoFailures);
    private readonly Counter<long> oversizedRecords = Meter.CreateCounter<long>(
        AppConst.Observability.MetricOversizedRecords);
    private readonly Counter<long> truncatedFiles = Meter.CreateCounter<long>(
        AppConst.Observability.MetricTruncatedFiles);
    private readonly Counter<long> appHubCallbacksReceived = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubCallbacksReceived);
    private readonly Counter<long> appHubCallbacksAdmitted = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubCallbacksAdmitted);
    private readonly Counter<long> appHubCallbacksDropped = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubCallbacksDropped);
    private readonly Counter<long> appHubConnectionAttempts = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubConnectionAttempts);
    private readonly Counter<long> appHubConnectionStates = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubConnectionStates);
    private readonly Counter<long> appHubReconnects = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubReconnects);
    private readonly Counter<long> appHubJoins = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubJoins);
    private readonly Counter<long> appHubChannelSaturations = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubChannelSaturations);
    private readonly Counter<long> appHubMappingResults = Meter.CreateCounter<long>(
        AppConst.Observability.MetricAppHubMappingResults);
    private readonly Histogram<double> persistenceLatency = Meter.CreateHistogram<double>(
        AppConst.Observability.MetricPersistenceLatency,
        "ms");

    private readonly IngestionHealthState healthState;
    private readonly AppHubHealthState appHubHealthState;

    public IngestionMetrics(
        IngestionHealthState healthState,
        AppHubHealthState? appHubHealthState = null)
    {
        ArgumentNullException.ThrowIfNull(healthState);
        this.healthState = healthState;
        this.appHubHealthState = appHubHealthState ?? new AppHubHealthState(
            TimeProvider.System,
            failureUnhealthyThreshold: 3);
        Meter.CreateObservableGauge<long>(
            AppConst.Observability.MetricFilesActive,
            () => healthState.Snapshot.ActiveFileCount);
        Meter.CreateObservableGauge<long>(
            AppConst.Observability.MetricIngestionLagBytes,
            ObserveLagBytes);
        Meter.CreateObservableGauge<long>(
            AppConst.Observability.MetricAppHubChannelDepth,
            ObserveAppHubChannelDepth);
        Meter.CreateObservableGauge<double>(
            AppConst.Observability.MetricAppHubLastCallbackAge,
            ObserveAppHubLastCallbackAge,
            "s");
        Meter.CreateObservableGauge<double>(
            AppConst.Observability.MetricAppHubLastSuccessfulJoinAge,
            ObserveAppHubLastSuccessfulJoinAge,
            "s");
    }

    public void RecordFilesDiscovered(string sourceId, string mode, int count)
    {
        filesDiscovered.Add(count, Tags(sourceId, mode));
        healthState.MarkSourceAvailable(sourceId);
    }

    public void RecordSourceAccessFailure(string sourceId, string mode)
    {
        sourceAccessFailures.Add(1, Tags(sourceId, mode));
        healthState.MarkSourceUnavailable(sourceId);
    }

    public void RecordFileProcessingStarted(string sourceId, long fileId) =>
        healthState.MarkFileProcessingStarted(sourceId, fileId);

    public void RecordFileProcessingCompleted(string sourceId, long fileId, string result) =>
        healthState.MarkFileProcessingCompleted(sourceId, fileId, result);

    public void RecordBytesRead(string sourceId, long fileId, long bytes) =>
        bytesRead.Add(bytes, Tags(sourceId, fileId));

    public void RecordRecordsFramed(string sourceId, long fileId, int count) =>
        recordsFramed.Add(count, Tags(sourceId, fileId));

    public void RecordPartialRecord(string sourceId, long fileId, int pendingBytes)
    {
        partialRecords.Add(1, Tags(sourceId, fileId));
    }

    public void RecordParseResult(string sourceId, long fileId, RawRecordParseStatus status)
    {
        var tags = Tags(sourceId, fileId, status.ToString());
        switch (status)
        {
            case RawRecordParseStatus.Parsed:
                recordsParsed.Add(1, tags);
                break;
            case RawRecordParseStatus.ParsedWithWarnings:
                recordsParsed.Add(1, tags);
                recordsParseWarnings.Add(1, tags);
                break;
            case RawRecordParseStatus.Failed:
                recordsParseFailures.Add(1, tags);
                break;
        }
    }

    public void RecordHistoryWrite(bool wasAlreadyPersisted, TimeSpan duration)
    {
        historyWrites.Add(1, Tags(AppConst.Observability.OperationHistoryWrite));
        RecordPersistenceLatency(AppConst.Observability.OperationHistoryWrite, duration);
        RecordDuplicate(wasAlreadyPersisted, AppConst.Observability.OperationHistoryWrite);
    }

    public void RecordFailureWrite(bool wasAlreadyPersisted, TimeSpan duration)
    {
        failureWrites.Add(1, Tags(AppConst.Observability.OperationFailureWrite));
        RecordPersistenceLatency(AppConst.Observability.OperationFailureWrite, duration);
        RecordDuplicate(wasAlreadyPersisted, AppConst.Observability.OperationFailureWrite);
    }

    public void RecordCheckpointAdvance(string sourceId, long fileId, long position, bool succeeded)
    {
        var tags = Tags(sourceId, fileId, succeeded ? "advanced" : "failed");
        if (succeeded)
        {
            checkpointAdvances.Add(1, tags);
            healthState.RecordCheckpointAdvance(sourceId, fileId, position, true);
        }
        else
        {
            checkpointFailures.Add(1, tags);
        }
    }

    public void RecordMongoRetry(string operation) =>
        mongoRetries.Add(1, Tags(operation));

    public void RecordMongoFailure(string operation)
    {
        mongoFailures.Add(1, Tags(operation));
        healthState.MarkMongoUnavailable();
    }

    public void RecordPersistenceLatency(string operation, TimeSpan duration) =>
        persistenceLatency.Record(duration.TotalMilliseconds, Tags(operation));

    public void RecordOversizedRecord(string sourceId, long fileId) =>
        oversizedRecords.Add(1, Tags(sourceId, fileId));

    public void RecordFileTruncated(string sourceId, long fileId)
    {
        truncatedFiles.Add(1, Tags(sourceId, fileId));
        healthState.MarkFileTruncated(sourceId, fileId);
    }

    public void RecordProgress(
        string sourceId,
        long fileId,
        long checkpointPosition,
        long? fileLength,
        int pendingBytes,
        DateTimeOffset? checkpointUpdatedAtUtc) =>
        healthState.RecordProgress(
            sourceId,
            fileId,
            checkpointPosition,
            fileLength,
            pendingBytes,
            checkpointUpdatedAtUtc);

    public void RecordAppHubCallbackReceived(string sourceId, string eventName)
    {
        appHubCallbacksReceived.Add(1, AppHubTags(sourceId, eventName));
        appHubHealthState.RecordCallbackReceived(sourceId);
    }

    public void RecordAppHubCallbackAdmitted(string sourceId, string eventName) =>
        appHubCallbacksAdmitted.Add(1, AppHubTags(sourceId, eventName));

    public void RecordAppHubCallbackDropped(string sourceId, string eventName, string reason)
    {
        appHubCallbacksDropped.Add(1, AppHubTags(sourceId, eventName, reason));
        healthState.MarkSourceUnavailable(sourceId);
    }

    public void RecordAppHubConnectionAttempt(string sourceId) =>
        appHubConnectionAttempts.Add(1, SourceTags(sourceId));

    public void RecordAppHubConnectionState(string sourceId, string state) =>
        appHubConnectionStates.Add(1, AppHubStateTags(sourceId, state));

    public void RecordAppHubReconnect(string sourceId) =>
        appHubReconnects.Add(1, SourceTags(sourceId));

    public void RecordAppHubJoin(string sourceId, bool succeeded) =>
        appHubJoins.Add(1, AppHubStateTags(sourceId, succeeded ? "succeeded" : "failed"));

    public void RecordAppHubChannelDepth(string sourceId, int depth) =>
        appHubHealthState.SetChannelDepth(sourceId, depth);

    public void RecordAppHubChannelSaturation(string sourceId) =>
        appHubChannelSaturations.Add(1, SourceTags(sourceId));

    public void RecordAppHubMappingResult(string sourceId, string status) =>
        appHubMappingResults.Add(1, AppHubStateTags(sourceId, status));

    private IEnumerable<Measurement<long>> ObserveLagBytes()
    {
        foreach (var file in healthState.Snapshot.Files)
        {
            var lag = Math.Max(0, (file.FileLength ?? file.CheckpointPosition) - file.CheckpointPosition);
            yield return new Measurement<long>(lag, Tags(file.SourceId, file.FileId));
        }
    }

    private IEnumerable<Measurement<long>> ObserveAppHubChannelDepth()
    {
        foreach (var source in appHubHealthState.Snapshot.Sources)
        {
            yield return new Measurement<long>(
                source.ChannelDepth,
                SourceTags(source.SourceId));
        }
    }

    private IEnumerable<Measurement<double>> ObserveAppHubLastCallbackAge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var source in appHubHealthState.Snapshot.Sources)
        {
            var age = source.LastCallbackAtUtc is { } lastCallback
                ? Math.Max(0, (now - lastCallback).TotalSeconds)
                : 0;
            yield return new Measurement<double>(age, SourceTags(source.SourceId));
        }
    }

    private IEnumerable<Measurement<double>> ObserveAppHubLastSuccessfulJoinAge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var source in appHubHealthState.Snapshot.Sources)
        {
            var age = source.LastSuccessfulJoinAtUtc is { } lastJoin
                ? Math.Max(0, (now - lastJoin).TotalSeconds)
                : 0;
            yield return new Measurement<double>(age, SourceTags(source.SourceId));
        }
    }

    private void RecordDuplicate(bool wasAlreadyPersisted, string operation)
    {
        if (wasAlreadyPersisted)
        {
            duplicateIdentities.Add(1, Tags(operation));
        }
    }

    private static TagList Tags(string operation) => new()
    {
        { AppConst.Observability.TagOperation, operation }
    };

    private static TagList Tags(string sourceId, string mode) => new()
    {
        { AppConst.Observability.TagSourceId, sourceId },
        { AppConst.Observability.TagMode, mode }
    };

    private static TagList Tags(string sourceId, long fileId, string? status = null)
    {
        var tags = new TagList
        {
            { AppConst.Observability.TagSourceId, sourceId },
            { AppConst.Observability.TagFileId, fileId }
        };
        if (status is not null)
        {
            tags.Add(AppConst.Observability.TagStatus, status);
        }

        return tags;
    }

    private static TagList AppHubTags(string sourceId, string eventName, string? reason = null)
    {
        var tags = new TagList
        {
            { AppConst.Observability.TagSourceId, sourceId },
            { AppConst.Observability.TagEventName, eventName }
        };
        if (reason is not null)
        {
            tags.Add(AppConst.Observability.TagReason, reason);
        }

        return tags;
    }

    private static TagList SourceTags(string sourceId) => new()
    {
        { AppConst.Observability.TagSourceId, sourceId }
    };

    private static TagList AppHubStateTags(string sourceId, string state) => new()
    {
        { AppConst.Observability.TagSourceId, sourceId },
        { AppConst.Observability.TagState, state }
    };
}
