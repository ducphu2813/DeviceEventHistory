using System.Text;

using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;

public static class RawRecordContextFactory
{
    public static RawRecordContext Create(
        RawLogFileDescriptor file,
        FramedRawLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(record);

        return new RawRecordContext
        {
            SourceId = file.SourceId,
            CompanyId = file.CompanyId,
            FolderDate = file.FolderDate,
            FileId = file.FileId,
            FileName = file.FileName,
            RelativePath = file.RelativePath,
            TimeZoneId = file.TimeZoneId,
            OffsetStart = record.StartOffset,
            OffsetEnd = record.EndOffsetExclusive,
            RawPayloadBytes = record.Payload,
            RawPayloadText = Encoding.UTF8.GetString(record.Payload)
        };
    }
}
