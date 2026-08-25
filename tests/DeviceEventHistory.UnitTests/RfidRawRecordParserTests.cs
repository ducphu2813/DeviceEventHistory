using System.Text;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;

namespace DeviceEventHistory.UnitTests;

public sealed class RfidRawRecordParserTests
{
    private readonly IRfidRawRecordParser parser = new RfidRawRecordParser(new BlockTokenizer());
    private readonly IRawRecordCanonicalMapper mapper = new CanonicalDeviceEventMapper();

    [Fact]
    public void Tag_read_record_parses_all_antenna_signal_fields_and_maps_to_canonical_event()
    {
        var context = CreateContext(
            "@(TAG001,14:30:20,101,5)b(0)t(1,22/08/2026 14:30:19,22/08/2026 14:30:20,3,20,0,0,920,-55)e(0)");

        var parsed = parser.Parse(context);
        var result = mapper.Map(parsed);

        Assert.Equal(RawRecordParseStatus.Parsed, parsed.Status);
        Assert.Empty(parsed.Issues);
        Assert.NotNull(result.Event);
        Assert.Null(result.Failure);
        Assert.Equal(AppConst.Categories.TagRead, result.Event!.Category);
        Assert.Equal("TAG001", result.Event.Facts.TagRead!.TagId);
        Assert.Equal(12, result.Event.Facts.TagRead.RoutingFileId);
        Assert.Equal(101, result.Event.Device!.Id);
        Assert.Equal(5, result.Event.Device.GateId);
        Assert.Equal(1, result.Event.Facts.Signal!.AntennaPort);
        Assert.Equal(3, result.Event.Facts.Signal.SeenCount);
        Assert.Equal(-55, result.Event.Facts.Signal.PeakRssiDbm);
        Assert.Equal(context.RawPayloadText, result.Event.RawPayload.Text);
        Assert.Equal(context.OffsetStart, result.Event.Source.OffsetStart);
        Assert.Equal(context.OffsetEnd, result.Event.Source.OffsetEnd);
        Assert.NotEmpty(result.Event.EventId);
    }

    [Fact]
    public void Business_record_parses_event_process_style_and_user_blocks_without_inventing_device_identity()
    {
        var context = CreateContext(
            "@(TAG002,12:00:00,0,0)te(2,1001,1,1001-1002,0)sp(1001-1002)u(501)e(0)");

        var result = mapper.Map(parser.Parse(context));

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Categories.BusinessProcess, result.Event!.Category);
        Assert.Equal(0, result.Event.Device!.Id);
        Assert.Equal(0, result.Event.Device.GateId);
        Assert.Equal(2, result.Event.Facts.BusinessEvent!.EventType);
        Assert.Equal(1001, result.Event.Facts.BusinessEvent.ProcessId);
        Assert.Equal([1001, 1002], result.Event.Facts.BusinessEvent.ProcessIds);
        Assert.Equal([1001, 1002], result.Event.Facts.StyleProcess!.ProcessCustom);
        Assert.Equal(501, result.Event.Facts.User!.UserId);
    }

    [Fact]
    public void Unknown_block_is_preserved_as_warning_without_discarding_known_facts()
    {
        var context = CreateContext("@(TAG003,08:00:00,101,5)x(unmapped)e(0)");

        var parsed = parser.Parse(context);
        var result = mapper.Map(parsed);

        Assert.Equal(RawRecordParseStatus.ParsedWithWarnings, parsed.Status);
        Assert.Contains(parsed.Issues, issue => issue.IsWarning && issue.Code == AppConst.Parsing.UnknownRawBlock);
        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Categories.Unknown, result.Event!.Category);
        Assert.Equal("parsed_with_warnings", result.Event.Parse.Status);
        Assert.Null(result.Event.Facts.Signal);
    }

    [Fact]
    public void Malformed_complete_record_is_routed_to_failure_with_deterministic_identity()
    {
        var context = CreateContext("@(TAG004,not-a-time,101,5)e(0)");

        var first = mapper.Map(parser.Parse(context));
        var second = mapper.Map(parser.Parse(context));

        Assert.Null(first.Event);
        Assert.NotNull(first.Failure);
        Assert.Equal(AppConst.Parsing.InvalidRawBlock, first.Failure!.Code);
        Assert.Equal(first.Failure.FailureId, second.Failure!.FailureId);
        Assert.Equal(context.RawPayloadText, first.Failure.Context.RawPayloadText);
    }

    [Fact]
    public void Event_identity_changes_when_only_the_source_offset_changes()
    {
        var first = mapper.Map(parser.Parse(CreateContext(
            "@(TAG005,08:00:00,101,5)b(0)t(1,22/08/2026 08:00:00,22/08/2026 08:00:01,1,20,0,0,920,-55)e(0)",
            offsetStart: 10)));
        var second = mapper.Map(parser.Parse(CreateContext(
            "@(TAG005,08:00:00,101,5)b(0)t(1,22/08/2026 08:00:00,22/08/2026 08:00:01,1,20,0,0,920,-55)e(0)",
            offsetStart: 20)));

        Assert.NotEqual(first.Event!.EventId, second.Event!.EventId);
    }

    [Fact]
    public void Context_factory_carries_framed_bytes_and_absolute_offsets_into_parser_context()
    {
        var source = new AntennaSourceOptions
        {
            SourceId = "antenna-site-a",
            Mode = RawLogSourceMode.Local,
            RootPath = "D:/RFID/RawData",
            CompanyId = 2,
            TimeZoneId = "UTC",
            FilePattern = AppConst.RawLog.DefaultFilePattern
        };
        Assert.True(RawLogFileDescriptor.TryCreate(
            source,
            new DateOnly(2026, 8, 22),
            "File_12.txt",
            "D:/RFID/RawData/2026/08/22/File_12.txt",
            null,
            out var descriptor));

        var payload = Encoding.UTF8.GetBytes("@(TAG006,08:00:00,101,5)e(0)");
        var record = new FramedRawLogRecord
        {
            StartOffset = 100,
            EndOffsetExclusive = 100 + payload.Length,
            Payload = payload
        };

        var context = RawRecordContextFactory.Create(descriptor!, record);

        Assert.Equal("2026/08/22/File_12.txt", context.RelativePath);
        Assert.Equal(100, context.OffsetStart);
        Assert.Equal(100 + payload.Length, context.OffsetEnd);
        Assert.Equal(payload, context.RawPayloadBytes);
        Assert.Equal("@(TAG006,08:00:00,101,5)e(0)", context.RawPayloadText);
    }

    private static RawRecordContext CreateContext(string text, long offsetStart = 100)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        return new RawRecordContext
        {
            SourceId = "antenna-site-a",
            CompanyId = 2,
            FolderDate = new DateOnly(2026, 8, 22),
            FileId = 12,
            FileName = "File_12.txt",
            RelativePath = "2026/08/22/File_12.txt",
            TimeZoneId = "UTC",
            OffsetStart = offsetStart,
            OffsetEnd = offsetStart + payload.Length,
            RawPayloadBytes = payload,
            RawPayloadText = text
        };
    }
}
