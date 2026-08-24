namespace DeviceEventHistory.Application.Metadata;

/// <summary>
/// Stable metadata for one RFID.Antenna raw-log stream.
/// </summary>
public sealed record SourceDefinition
{
    public required string SourceId { get; init; }

    public required int CompanyId { get; init; }

    public required string RootPath { get; init; }

    public required string TimeZoneId { get; init; }

    public required string FilePattern { get; init; }

    public bool Enabled { get; init; }
}
