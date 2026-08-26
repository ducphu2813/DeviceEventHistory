using DeviceEventHistory.Application.Persistence;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Orchestration;

public sealed class FileRegistry(
    IIngestionCheckpointStore checkpointStore,
    IRawLogTailReader tailReader,
    Func<IRawLogRecordFramer> framerFactory,
    IOptions<RfidRawLogOptions> rawLogOptions,
    TimeProvider timeProvider,
    ILogger<FileRegistry>? registryLogger = null)
{
    private readonly ILogger<FileRegistry> logger =
        registryLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileRegistry>.Instance;
    private readonly Dictionary<string, FileIngestionState> states = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public async Task<FileIngestionState> GetOrCreateAsync(
        RawLogFileDescriptor descriptor,
        bool startupExistingFile,
        CancellationToken cancellationToken)
    {
        var key = new IngestionCheckpointKey
        {
            SourceId = descriptor.SourceId,
            FolderDate = descriptor.FolderDate,
            FileId = descriptor.FileId,
            RelativePath = descriptor.RelativePath
        };

        lock (sync)
        {
            if (states.TryGetValue(key.DocumentId, out var existing))
            {
                existing.UpdateDescriptor(descriptor);
                return existing;
            }
        }

        var checkpoint = await checkpointStore.LoadAsync(key, cancellationToken);
        var hasPersistedCheckpoint = checkpoint is not null;
        var initialPosition = checkpoint?.Position ??
            await ResolveInitialPositionAsync(descriptor, startupExistingFile, cancellationToken);

        checkpoint ??= new IngestionCheckpoint
        {
            Key = key,
            Position = initialPosition,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Version = 0
        };

        var state = new FileIngestionState(
            descriptor,
            checkpoint,
            initialPosition,
            framerFactory(),
            startupExistingFile);

        logger.LogDebug(
            AppConst.Logging.FileStateCreatedMessage,
            descriptor.SourceId,
            descriptor.FileId,
            initialPosition,
            startupExistingFile,
            !hasPersistedCheckpoint
                ? (startupExistingFile
                    ? rawLogOptions.Value.StartupExistingFilePolicy
                    : rawLogOptions.Value.NewFilePolicy)
                : AppConst.Observability.CheckpointPolicyLabel);

        lock (sync)
        {
            if (states.TryGetValue(key.DocumentId, out var existing))
            {
                existing.UpdateDescriptor(descriptor);
                return existing;
            }

            states.Add(key.DocumentId, state);
            return state;
        }
    }

    public IReadOnlyList<FileIngestionState> Snapshot()
    {
        lock (sync)
        {
            return states.Values.ToArray();
        }
    }

    private async Task<long> ResolveInitialPositionAsync(
        RawLogFileDescriptor descriptor,
        bool startupExistingFile,
        CancellationToken cancellationToken)
    {
        var policy = startupExistingFile
            ? rawLogOptions.Value.StartupExistingFilePolicy
            : rawLogOptions.Value.NewFilePolicy;

        if (policy == FileStartPositionPolicy.Beginning)
        {
            return 0;
        }

        if (descriptor.Length.HasValue)
        {
            return descriptor.Length.Value;
        }

        var probe = await tailReader.ReadAsync(descriptor, 0, cancellationToken);
        if (probe.IsTruncated)
        {
            return 0;
        }

        return probe.FileLength;
    }
}
