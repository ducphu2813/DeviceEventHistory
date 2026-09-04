using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Application.AppHub.Mapping;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Admission;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;

namespace DeviceEventHistory.UnitTests;

public sealed class AppHubMappingTests
{
    [Fact]
    public void Scanner_user_state_is_redacted_and_mapped_to_typed_v2_facts()
    {
        var source = CreateSource();
        var sourceEvent = new AppHubRawSourceEventFactory(source, TimeProvider.System).Create(
            "generation-1",
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
            [new
            {
                ConnectionId = "connection-secret",
                CompanyId = 2,
                UserId = 42,
                UserName = "private-user",
                Ip = "192.0.2.10",
                DateConnected = "2026-08-28T15:29:58",
                SessionType = 1,
                DeviceType = 2,
                DeviceId = 101,
                DeviceName = "Scanner A",
                GateId = 5,
                GateName = "Gate 5"
            }]);

        var result = CreateMapper(
            new AppHubSourceMappingOptions(source.SourceId, 2, false),
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect)
            .Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Null(result.Failure);
        var deviceEvent = result.Event!;
        Assert.Equal(AppConst.Categories.ScannerConnection, deviceEvent.Category);
        Assert.Equal(2, deviceEvent.CompanyId);
        Assert.Equal(101, deviceEvent.Device!.Id);
        Assert.Equal("connected", deviceEvent.Facts.Connection!.Status);
        Assert.Equal(42, deviceEvent.Facts.User!.UserId);
        Assert.Equal(1, deviceEvent.Facts.Scanner!.SessionType);
        Assert.Equal(2, deviceEvent.Facts.Scanner.DeviceType);
        Assert.Equal(
            sourceEvent.ReceivedAtUtc,
            deviceEvent.OccurredAtUtc);
        Assert.Equal(
            TimeZoneInfo.ConvertTime(
                sourceEvent.ReceivedAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById(AppConst.RawLog.DefaultTimeZoneId)),
            deviceEvent.OccurredAtLocal);
        Assert.Equal(AppConst.TimeBases.Received, deviceEvent.TimeBasis);
        Assert.Contains(AppConst.Parsing.SourceTimeUntrusted, deviceEvent.Parse.Warnings);
        Assert.DoesNotContain("\"ConnectionId\":", sourceEvent.RawArgumentsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", sourceEvent.RawArgumentsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.10", sourceEvent.RawArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("ConnectionIdHash", sourceEvent.RawArgumentsJson, StringComparison.Ordinal);
        Assert.Contains(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("connection-secret")))
                .ToLowerInvariant(),
            sourceEvent.RawArgumentsJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Dedicated_single_tenant_is_the_only_configured_tenant_fallback()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
            "[{\"DeviceId\":101}] ");

        var dedicatedResult = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 7, true),
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect)
            .Map(sourceEvent);
        var multiTenantResult = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 7, false),
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect)
            .Map(sourceEvent);

        Assert.Equal(7, dedicatedResult.Event!.CompanyId);
        Assert.Equal(AppConst.Parsing.TenantUnresolved, multiTenantResult.Failure!.Error.Code);
    }

    [Fact]
    public void Payload_and_configured_tenant_mismatch_becomes_non_retryable_failure()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
            "[{\"CompanyId\":2,\"DeviceId\":101}]");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 7, true),
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect)
            .Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.Equal(AppConst.Parsing.TenantMismatch, result.Failure!.Error.Code);
        Assert.False(result.Failure.Retryable);
        Assert.Contains("2", result.Failure.Error.Message, StringComparison.Ordinal);
        Assert.Contains("7", result.Failure.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_read_tag_maps_device_tag_and_epc_fields()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceReadTag,
            "[{\"CompanyId\":2,\"DeviceId\":101,\"TagId\":\"TAG-01\",\"Epc\":\"EPC-01\"}]");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", null, false),
            AppConst.AppHub.Callbacks.ReceiveDeviceReadTag)
            .Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Categories.TagRead, result.Event!.Category);
        Assert.Equal(AppConst.Parsing.StatusParsed, result.Event.Parse.Status);
        Assert.Equal(101, result.Event.Device!.Id);
        Assert.Equal("TAG-01", result.Event.Facts.TagRead!.TagId);
        Assert.Equal("EPC-01", result.Event.Facts.TagRead.EpcRaw);
        Assert.Null(result.Event.Facts.TagRead.RoutingFileId);
        Assert.Equal(sourceEvent.RawArgumentsJson, result.Event.RawPayload.ArgumentsJson);
    }

    [Fact]
    public void Device_connection_maps_device_and_source_connection_state()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveStateConnected,
            "[{\"DeviceId\":18,\"IsStart\":false,\"IsConnecting\":true,\"IsConnected\":false}]");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 2, true),
            AppConst.AppHub.Callbacks.ReceiveStateConnected)
            .Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Parsing.StatusParsed, result.Event!.Parse.Status);
        Assert.Equal(18, result.Event.Device!.Id);
        Assert.Equal(
            AppConst.CanonicalValues.ConnectionStatusConnecting,
            result.Event.Facts.Connection!.Status);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero),
            result.Event.OccurredAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 28, 15, 30, 0, TimeSpan.FromHours(7)),
            result.Event.OccurredAtLocal);
        Assert.Equal(AppConst.TimeBases.Received, result.Event.TimeBasis);
        Assert.False(result.Event.Facts.Connection.IsStart);
        Assert.True(result.Event.Facts.Connection.IsConnecting);
        Assert.False(result.Event.Facts.Connection.IsConnected);
        Assert.False(result.Event.Facts.Connection.IsSourceConnected);
    }

    [Theory]
    [InlineData(AppConst.AppHub.Callbacks.ReceiveGreenState, "green_light")]
    [InlineData(AppConst.AppHub.Callbacks.ReceiveRedState, "red_light")]
    public void Device_control_state_maps_device_control_facts(
        string eventName,
        string expectedControl)
    {
        var sourceEvent = CreateSourceEvent(
            eventName,
            "[{\"DeviceId\":40,\"On\":false}]");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 2, true),
            eventName)
            .Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Parsing.StatusParsed, result.Event!.Parse.Status);
        Assert.Equal(40, result.Event.Device!.Id);
        Assert.Equal(expectedControl, result.Event.Facts.DeviceControlState!.Control);
        Assert.Equal("off", result.Event.Facts.DeviceControlState.State);
        Assert.Equal("false", result.Event.Facts.DeviceControlState.RawState);
    }

    [Fact]
    public void Device_sensor_state_maps_timeout_and_device()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveTimeSensor,
            "[{\"DeviceId\":18,\"Timeout\":3}]");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 2, true),
            AppConst.AppHub.Callbacks.ReceiveTimeSensor)
            .Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Parsing.StatusParsed, result.Event!.Parse.Status);
        Assert.Equal(18, result.Event.Device!.Id);
        Assert.Equal(3, result.Event.Facts.SensorState!.Timeout);
        Assert.Equal("seconds", result.Event.Facts.SensorState.TimeoutUnit);
    }

    [Fact]
    public void Scanner_info_response_is_snapshot_and_not_connection_activity()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline,
            "[{\"CompanyId\":2,\"DeviceId\":101,\"SessionType\":1,\"DeviceType\":3}]");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", null, false),
            AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline)
            .Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Categories.DeviceSnapshot, result.Event!.Category);
        Assert.Equal(AppConst.DeliveryKinds.Snapshot, result.Event.Source.DeliveryKind);
        Assert.Equal("unknown", result.Event.Facts.Connection!.Status);
        Assert.Null(result.Event.Facts.Connection.IsSourceConnected);
    }

    [Fact]
    public void Malformed_json_becomes_deserialization_failure()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            "not-json");

        var result = CreateMapper(
            new AppHubSourceMappingOptions("apphub-source", 2, true),
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline)
            .Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.Equal(AppConst.Parsing.InvalidRecordFormat, result.Failure!.Error.Code);
        Assert.Equal(
            AppConst.IngestionStages.Deserialization,
            result.Failure.Error.Stage);
    }

    private static IRawSourceEventMapper CreateMapper(
        AppHubSourceMappingOptions options,
        string eventName) =>
        eventName switch
        {
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect
                or AppConst.AppHub.Callbacks.ReceiveDeviceScanDisconnect
                or AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline =>
                new ScannerEventMapper(CreateTenantResolver(options), eventName),
            AppConst.AppHub.Callbacks.ReceiveDeviceReadTag =>
                new DeviceReadTagEventMapper(CreateTenantResolver(options)),
            AppConst.AppHub.Callbacks.ReceiveStateConnected =>
                new DeviceConnectionEventMapper(CreateTenantResolver(options)),
            AppConst.AppHub.Callbacks.ReceiveGreenState
                or AppConst.AppHub.Callbacks.ReceiveRedState =>
                new DeviceControlStateEventMapper(CreateTenantResolver(options), eventName),
            AppConst.AppHub.Callbacks.ReceiveTimeSensor =>
                new DeviceSensorStateEventMapper(CreateTenantResolver(options)),
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline =>
                new DeviceOnlineEventMapper(CreateTenantResolver(options)),
            _ => throw new ArgumentOutOfRangeException(nameof(eventName))
        };

    private static AppHubTenantResolver CreateTenantResolver(
        AppHubSourceMappingOptions options) =>
        new(new AppHubSourceConfigurationProvider([options]));

    private static AppHubSourceOptions CreateSource() => new()
    {
        SourceId = "apphub-source",
        Endpoint = "https://erp.example.com/signalr",
        EnabledEvents = [AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect],
        AccessTokenEnvironmentVariable = "APPHUB_TEST_TOKEN"
    };

    private static RawSourceEvent CreateSourceEvent(
        string eventName,
        string argumentsJson) => new()
    {
        IngestionEventId = "event-id-" + eventName,
        SourceKind = AppConst.SourceKinds.ErpAppHub,
        SourceId = "apphub-source",
        SourceApplication = AppConst.AppHub.Producer,
        SourceTransport = AppConst.AppHub.Transport,
        EventName = eventName,
        ReceivedAtUtc = new DateTimeOffset(2026, 8, 28, 8, 30, 0, TimeSpan.Zero),
        RawArgumentsJson = argumentsJson,
        PayloadSha256 = "payload-hash",
        PayloadSizeBytes = Encoding.UTF8.GetByteCount(argumentsJson),
        ConnectionGeneration = "generation-1",
        ReceiveSequence = 1,
        DeliveryKind = AppConst.AppHub.DeliveryKind
    };
}
