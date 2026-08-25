namespace DeviceEventHistory.Application.Parsing;

public sealed record RawRecordContext
{
    public required string SourceId { get; init; }

    public required int CompanyId { get; init; }

    public required DateOnly FolderDate { get; init; }

    public required long FileId { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public required string TimeZoneId { get; init; }

    public required long OffsetStart { get; init; }

    public required long OffsetEnd { get; init; }

    public required byte[] RawPayloadBytes { get; init; }

    public required string RawPayloadText { get; init; }
}
