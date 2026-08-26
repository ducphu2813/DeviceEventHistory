using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using Microsoft.Extensions.Logging;

namespace DeviceEventHistory.Infrastructure.Observability;

public static class LoggingScopes
{
    public static IDisposable BeginFileScope(
        ILogger logger,
        string workerId,
        RawLogFileDescriptor descriptor,
        long? offsetStart = null,
        long? offsetEnd = null,
        string? eventId = null,
        string? failureId = null,
        int? attempt = null,
        TimeSpan? duration = null,
        string? result = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(descriptor);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["WorkerId"] = workerId,
            ["SourceId"] = descriptor.SourceId,
            ["FolderDate"] = descriptor.FolderDate,
            ["FileId"] = descriptor.FileId,
            ["RelativePath"] = descriptor.RelativePath,
            ["OffsetStart"] = offsetStart,
            ["OffsetEnd"] = offsetEnd,
            ["EventId"] = eventId,
            ["FailureId"] = failureId,
            ["Attempt"] = attempt,
            ["DurationMilliseconds"] = duration?.TotalMilliseconds,
            ["Result"] = result
        };

        return logger.BeginScope(values)!;
    }
}
