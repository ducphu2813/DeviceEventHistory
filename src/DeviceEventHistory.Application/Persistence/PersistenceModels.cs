using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;
using System.Globalization;

namespace DeviceEventHistory.Application.Persistence;

public sealed record IngestionCheckpointKey
{
    public required string SourceId { get; init; }

    public required DateOnly FolderDate { get; init; }

    public required long FileId { get; init; }

    public required string RelativePath { get; init; }

    public string DocumentId => string.Join(
        AppConst.MongoDb.CheckpointKeySeparator,
        SourceId,
        FolderDate.ToString(AppConst.MongoDb.CheckpointDateFormat, CultureInfo.InvariantCulture),
        FileId.ToString(CultureInfo.InvariantCulture),
        RelativePath);

    public static IngestionCheckpointKey From(RawRecordContext context) => new()
    {
        SourceId = context.SourceId,
        FolderDate = context.FolderDate,
        FileId = context.FileId,
        RelativePath = context.RelativePath
    };
}

public sealed record IngestionCheckpoint
{
    public required IngestionCheckpointKey Key { get; init; }

    public required long Position { get; init; }

    public string? LastEventId { get; init; }

    public string? LastRecordHash { get; init; }

    public long? ObservedFileLength { get; init; }

    public string? WorkerId { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required long Version { get; init; }
}

public sealed record CheckpointAdvanceRequest
{
    public required long Position { get; init; }

    public required string LastRecordHash { get; init; }

    public string? LastEventId { get; init; }

    public long? ObservedFileLength { get; init; }

    public required string WorkerId { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public enum CheckpointAdvanceStatus
{
    Advanced = 0,
    Conflict = 1
}

public sealed record CheckpointAdvanceResult(
    CheckpointAdvanceStatus Status,
    IngestionCheckpoint? Checkpoint)
{
    public bool IsAdvanced => Status == CheckpointAdvanceStatus.Advanced;
}

public sealed record PersistenceWriteResult(string Identity, bool WasAlreadyPersisted);

public sealed record RawRecordPersistenceOutcome(
    string PersistedIdentity,
    bool WasFailure,
    bool WasAlreadyPersisted,
    CheckpointAdvanceResult CheckpointResult)
{
    public bool IsConfirmed => CheckpointResult.IsAdvanced;
}
