using System.Globalization;
using System.Text.RegularExpressions;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

public sealed record RawLogFileDescriptor
{
    private static readonly Regex FileNameRegex = new(
        AppConst.RawLog.FileNameRegex,
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public required string SourceId { get; init; }

    public required int CompanyId { get; init; }

    public required RawLogSourceMode Mode { get; init; }

    public required DateOnly FolderDate { get; init; }

    public required long FileId { get; init; }

    public required string FileName { get; init; }

    public required string Location { get; init; }

    public long? Length { get; init; }

    public static bool TryCreate(
        AntennaSourceOptions source,
        DateOnly folderDate,
        string fileName,
        string location,
        long? length,
        out RawLogFileDescriptor? descriptor)
    {
        var match = FileNameRegex.Match(fileName);
        if (!match.Success ||
            !long.TryParse(match.Groups["fileId"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var fileId))
        {
            descriptor = null;
            return false;
        }

        descriptor = new RawLogFileDescriptor
        {
            SourceId = source.SourceId.Trim(),
            CompanyId = source.CompanyId,
            Mode = source.Mode,
            FolderDate = folderDate,
            FileId = fileId,
            FileName = fileName,
            Location = location,
            Length = length
        };

        return true;
    }
}
