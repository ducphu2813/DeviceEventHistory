using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.Parsing;

public sealed class CanonicalDeviceEventMapper : IRawRecordCanonicalMapper
{
    public RawRecordProcessingResult Map(RawRecordParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == RawRecordParseStatus.Failed)
        {
            return new RawRecordProcessingResult
            {
                Failure = new RawRecordProcessingResult.CanonicalIngestionFailure
                {
                    FailureId = EventIdentityFactory.CreateFailureId(result.Context),
                    Code = result.Issues.FirstOrDefault(issue => !issue.IsWarning)?.Code ?? AppConst.Parsing.InvalidRecordFormat,
                    Message = result.Issues.FirstOrDefault(issue => !issue.IsWarning)?.Message ?? AppConst.Messages.MSG_RAW_RECORD_HEADER_REQUIRED,
                    ParserVersion = AppConst.RawLog.ParserVersion,
                    Context = result.Context,
                    Retryable = false
                }
            };
        }

        var parsed = result.Parsed;
        var header = parsed.Header!;
        var occurredAtLocal = ToLocalDateTimeOffset(result.Context, header.ReadTime);
        var signal = parsed.Signal;

        var category = parsed.BusinessEvent is not null
            ? AppConst.Categories.BusinessProcess
            : signal is not null
                ? AppConst.Categories.TagRead
                : AppConst.Categories.Unknown;

        return new RawRecordProcessingResult
        {
            Event = new CanonicalDeviceEvent
            {
                EventId = EventIdentityFactory.CreateEventId(result.Context),
                SchemaVersion = AppConst.RawLog.SchemaVersion,
                Category = category,
                SourceKind = AppConst.RawLog.SourceKind,
                CompanyId = result.Context.CompanyId,
                OccurredAtUtc = occurredAtLocal?.ToUniversalTime(),
                OccurredAtLocal = occurredAtLocal,
                Source = new CanonicalDeviceEvent.SourceContext
                {
                    Producer = AppConst.RawLog.Producer,
                    SourceId = result.Context.SourceId,
                    FileId = result.Context.FileId,
                    FileName = result.Context.FileName,
                    RelativePath = result.Context.RelativePath,
                    FolderDate = result.Context.FolderDate,
                    OffsetStart = result.Context.OffsetStart,
                    OffsetEnd = result.Context.OffsetEnd
                },
                Device = new CanonicalDeviceEvent.DeviceContext
                {
                    Id = header.DeviceId,
                    GateId = header.GateId
                },
                RawPayload = new CanonicalDeviceEvent.RawPayloadContext
                {
                    Format = AppConst.RawLog.PayloadFormat,
                    Text = result.Context.RawPayloadText,
                    Sha256 = EventIdentityFactory.ComputePayloadHash(result.Context)
                },
                Facts = MapFacts(result.Context.FileId, parsed, result.Context),
                Parse = new CanonicalDeviceEvent.ParseContext
                {
                    Status = result.Status == RawRecordParseStatus.Parsed
                        ? AppConst.Parsing.StatusParsed
                        : AppConst.Parsing.StatusParsedWithWarnings,
                    ParserVersion = AppConst.RawLog.ParserVersion,
                    Warnings = result.Issues.Where(issue => issue.IsWarning).Select(issue => issue.Message).ToArray(),
                    Errors = result.Issues.Where(issue => !issue.IsWarning).Select(issue => issue.Message).ToArray()
                }
            }
        };
    }

    private static CanonicalDeviceEvent.FactsContext MapFacts(
        long fileId,
        ParsedRfidRawRecord parsed,
        RawRecordContext context) =>
        new()
        {
            TagRead = parsed.Header?.TagId is { Length: > 0 } tagId
                ? new CanonicalDeviceEvent.TagReadFacts
                {
                    TagId = tagId,
                    RoutingFileId = fileId
                }
                : null,
            GateState = parsed.GateState is null
                ? null
                : new CanonicalDeviceEvent.GateStateFacts
                {
                    StateCode = parsed.GateState.StateCode,
                    RawValue = parsed.GateState.RawValue
                },
            Signal = parsed.Signal is null
                ? null
                : new CanonicalDeviceEvent.SignalFacts
                {
                    AntennaPort = parsed.Signal.AntennaPort,
                    FirstSeenAtLocal = ToLocalDateTimeOffset(context, parsed.Signal.FirstSeenAtLocal),
                    LastSeenAtLocal = ToLocalDateTimeOffset(context, parsed.Signal.LastSeenAtLocal),
                    SeenCount = parsed.Signal.SeenCount,
                    TxPower = parsed.Signal.TxPower,
                    DopplerFrequency = parsed.Signal.DopplerFrequency,
                    PhaseAngle = parsed.Signal.PhaseAngle,
                    ChannelMhz = parsed.Signal.ChannelMhz,
                    PeakRssiDbm = parsed.Signal.PeakRssiDbm
                },
            BusinessEvent = parsed.BusinessEvent is null
                ? null
                : new CanonicalDeviceEvent.BusinessEventFacts
                {
                    EventType = parsed.BusinessEvent.EventType,
                    ProcessId = parsed.BusinessEvent.ProcessId,
                    Quantity = parsed.BusinessEvent.Quantity,
                    ProcessIdsRaw = parsed.BusinessEvent.ProcessIdsRaw,
                    ProcessIds = parsed.BusinessEvent.ProcessIds,
                    Second = parsed.BusinessEvent.Second
                },
            StyleProcess = parsed.StyleProcess is null
                ? null
                : new CanonicalDeviceEvent.StyleProcessFacts
                {
                    ProcessCustomRaw = parsed.StyleProcess.ProcessCustomRaw,
                    ProcessCustom = parsed.StyleProcess.ProcessCustom
                },
            User = parsed.User is null
                ? null
                : new CanonicalDeviceEvent.UserFacts
                {
                    UserId = parsed.User.UserId
                }
        };

    private static DateTimeOffset? ToLocalDateTimeOffset(RawRecordContext context, TimeSpan? time)
    {
        if (!time.HasValue || !TryGetTimeZone(context.TimeZoneId, out var timeZone))
        {
            return null;
        }

        var localDateTime = context.FolderDate.ToDateTime(TimeOnly.FromTimeSpan(time.Value), DateTimeKind.Unspecified);
        var offset = timeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private static DateTimeOffset? ToLocalDateTimeOffset(RawRecordContext context, DateTime? localDateTime)
    {
        if (!localDateTime.HasValue || !TryGetTimeZone(context.TimeZoneId, out var timeZone))
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(localDateTime.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }

    private static bool TryGetTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
