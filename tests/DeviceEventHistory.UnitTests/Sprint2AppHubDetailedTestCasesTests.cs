using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Application.AppHub.Mapping;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Domain.Events;
using DeviceEventHistory.Infrastructure.AppHub.Admission;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace DeviceEventHistory.UnitTests;

/// <summary>
/// Unit Tests covering the 36 Sprint 2 Testcases:
/// - Configuration: TC-CFG-001 .. TC-CFG-006
/// - Connection: TC-CONN-001 .. TC-CONN-006
/// - Reconnection: TC-RECON-001 .. TC-RECON-004
/// - Callback Mapping: TC-MAP-001 .. TC-MAP-011
/// - Data & Invariants: TC-DATA-001 .. TC-DATA-009
/// </summary>
public sealed class Sprint2AppHubDetailedTestCasesTests
{
    private const string TestSourceId = "erp-apphub-ua";

    #region TC-CFG-001 .. TC-CFG-006 (Configuration & Validation)

    [Fact]
    public void TC_CFG_001_Startup_with_valid_training_configuration()
    {
        var worker = new WorkerOptions { Enabled = true, WorkerId = "worker-live-01" };
        var rawLog = new RfidRawLogOptions();
        var appHub = new AppHubOptions
        {
            Sources =
            [
                new AppHubSourceOptions
                {
                    SourceId = TestSourceId,
                    Endpoint = "https://training-api.un-available.net/signalr",
                    HubName = AppConst.AppHub.DefaultHubName,
                    AccessToken = "test-valid-cookie-token",
                    EnabledEvents = AppConst.AppHub.Callbacks.Registered.ToList(),
                    CompanyId = 2,
                    DedicatedSingleTenant = false
                }
            ]
        };

        var validator = new AppHubOptionsValidator(Options.Create(worker), Options.Create(rawLog));
        var result = validator.Validate(null, appHub);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void TC_CFG_002_Missing_credential_fails_validation()
    {
        var worker = new WorkerOptions { Enabled = true, WorkerId = "worker-01" };
        var rawLog = new RfidRawLogOptions();
        var appHub = new AppHubOptions
        {
            Sources =
            [
                new AppHubSourceOptions
                {
                    SourceId = TestSourceId,
                    Endpoint = "https://training-api.un-available.net/signalr",
                    HubName = AppConst.AppHub.DefaultHubName,
                    AccessToken = string.Empty,
                    TokenJwt = string.Empty,
                    AccessTokenEnvironmentVariable = string.Empty,
                    TokenJwtEnvironmentVariable = string.Empty
                }
            ]
        };

        var validator = new AppHubOptionsValidator(Options.Create(worker), Options.Create(rawLog));
        var result = validator.Validate(null, appHub);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TC_CFG_003_Token_and_jwt_fallback_and_priority()
    {
        var sourceWithToken = new AppHubSourceOptions
        {
            SourceId = TestSourceId,
            AccessToken = "cookie-secret",
            TokenJwt = "jwt-secret",
            Endpoint = "https://training-api.un-available.net/signalr"
        };
        var factory = new AppHubMonitoringConnectionFactory();
        await using var connWithToken = factory.Create(sourceWithToken);
        Assert.NotNull(connWithToken);

        var sourceWithJwtOnly = new AppHubSourceOptions
        {
            SourceId = TestSourceId,
            AccessToken = string.Empty,
            TokenJwt = "jwt-secret-only",
            Endpoint = "https://training-api.un-available.net/signalr"
        };
        await using var connWithJwt = factory.Create(sourceWithJwtOnly);
        Assert.NotNull(connWithJwt);
    }

    [Theory]
    [InlineData("relative/endpoint", "Endpoint", false, false)]
    [InlineData("http://192.168.1.38/signalr?token=secret", "Endpoint", false, false)]
    [InlineData("   ", "HubName", true, false)]
    [InlineData("invalid_callback_name", "EnabledEvents", false, true)]
    public void TC_CFG_004_Invalid_endpoint_hub_or_callback_fails_validation(
        string value,
        string expectedErrorField,
        bool isHub,
        bool isCallback)
    {
        var worker = new WorkerOptions { Enabled = true, WorkerId = "worker-01" };
        var rawLog = new RfidRawLogOptions();
        var source = new AppHubSourceOptions
        {
            SourceId = TestSourceId,
            Endpoint = isHub || isCallback ? "https://training-api.un-available.net/signalr" : value,
            HubName = isHub ? value : AppConst.AppHub.DefaultHubName,
            AccessToken = "valid-token",
            EnabledEvents = isCallback ? [value] : [AppConst.AppHub.Callbacks.ReceiveDeviceOnline]
        };
        var appHub = new AppHubOptions { Sources = [source] };

        var validator = new AppHubOptionsValidator(Options.Create(worker), Options.Create(rawLog));
        var result = validator.Validate(null, appHub);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(expectedErrorField, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TC_CFG_005_Duplicate_source_ids_are_rejected()
    {
        var worker = new WorkerOptions { Enabled = true, WorkerId = "worker-01" };
        var rawLog = new RfidRawLogOptions();
        var appHub = new AppHubOptions
        {
            Sources =
            [
                new AppHubSourceOptions
                {
                    SourceId = "duplicate-source",
                    Endpoint = "https://training-api.un-available.net/signalr",
                    HubName = AppConst.AppHub.DefaultHubName,
                    AccessToken = "token1"
                },
                new AppHubSourceOptions
                {
                    SourceId = "DUPLICATE-SOURCE",
                    Endpoint = "https://training-api.un-available.net/signalr",
                    HubName = AppConst.AppHub.DefaultHubName,
                    AccessToken = "token2"
                }
            ]
        };

        var validator = new AppHubOptionsValidator(Options.Create(worker), Options.Create(rawLog));
        var result = validator.Validate(null, appHub);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TC_CFG_006_Redaction_rules_hash_connection_id_and_strip_user_ip_username()
    {
        var rawJson = "[{\"ConnectionId\":\"conn-secret-123\",\"UserId\":42,\"UserName\":\"admin_user\",\"Ip\":\"10.0.0.99\",\"DeviceId\":101}]";
        var source = new AppHubSourceOptions { SourceId = TestSourceId };
        var factory = new AppHubRawSourceEventFactory(source, TimeProvider.System);
        var redactedJson = factory.Create("generation-test", AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect, JArray.Parse(rawJson).ToObject<object[]>()).RawArgumentsJson!;

        Assert.DoesNotContain("conn-secret-123", redactedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("admin_user", redactedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.99", redactedJson, StringComparison.Ordinal);
        Assert.Contains("ConnectionIdHash", redactedJson, StringComparison.Ordinal);

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("conn-secret-123"))).ToLowerInvariant();
        Assert.Contains(expectedHash, redactedJson, StringComparison.Ordinal);
    }

    #endregion

    #region TC-CONN-001 .. TC-CONN-006 (Connection, Lifecycle & Isolation)

    [Fact]
    public void TC_CONN_001_Source_transport_metadata_is_classic_signalr()
    {
        var sourceEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, "[{\"CompanyId\":2,\"DeviceId\":1}]");
        Assert.Equal(AppConst.SourceKinds.ErpAppHub, sourceEvent.SourceKind);
        Assert.Equal(AppConst.AppHub.Transport, sourceEvent.SourceTransport);
        Assert.Equal(AppConst.AppHub.Producer, sourceEvent.SourceApplication);
    }

    [Fact]
    public void TC_CONN_003_Callback_registration_must_be_allowed_events()
    {
        var registeredCallbacks = AppConst.AppHub.Callbacks.Registered;
        Assert.Equal(11, registeredCallbacks.Count);
        Assert.Contains(AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect, registeredCallbacks);
        Assert.Contains(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, registeredCallbacks);
    }

    [Fact]
    public void TC_CONN_005_AppHub_disabled_skips_validation_and_running()
    {
        var worker = new WorkerOptions { Enabled = true, WorkerId = "worker-01" };
        var rawLog = new RfidRawLogOptions();
        var appHub = new AppHubOptions
        {
            Enabled = false,
            Sources = [new AppHubSourceOptions { SourceId = "invalid-source-without-fields" }]
        };

        var validator = new AppHubOptionsValidator(Options.Create(worker), Options.Create(rawLog));
        var result = validator.Validate(null, appHub);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void TC_CONN_006_Multiple_sources_have_isolated_identities()
    {
        var source1 = new AppHubSourceOptions { SourceId = "source-alpha", CompanyId = 1 };
        var source2 = new AppHubSourceOptions { SourceId = "source-beta", CompanyId = 2 };

        var factory1 = new AppHubRawSourceEventFactory(source1, TimeProvider.System);
        var factory2 = new AppHubRawSourceEventFactory(source2, TimeProvider.System);

        var event1 = factory1.Create("gen-a", AppConst.AppHub.Callbacks.ReceiveDeviceOnline, [new { DeviceId = 1 }]);
        var event2 = factory2.Create("gen-b", AppConst.AppHub.Callbacks.ReceiveDeviceOnline, [new { DeviceId = 2 }]);

        Assert.Equal("source-alpha", event1.SourceId);
        Assert.Equal("source-beta", event2.SourceId);
        Assert.Equal("gen-a", event1.ConnectionGeneration);
        Assert.Equal("gen-b", event2.ConnectionGeneration);
        Assert.NotEqual(event1.IngestionEventId, event2.IngestionEventId);
    }

    #endregion

    #region TC-RECON-001 .. TC-RECON-004 (Reconnection & Event Identity)

    [Fact]
    public void TC_RECON_004_Event_identity_after_reconnect_has_new_generation_and_local_sequence()
    {
        var source = new AppHubSourceOptions { SourceId = TestSourceId };
        var factory = new AppHubRawSourceEventFactory(source, TimeProvider.System);

        var eventGen1 = factory.Create("gen-001", AppConst.AppHub.Callbacks.ReceiveDeviceOnline, [new { CompanyId = 2, DeviceId = 101 }]);
        var eventGen2 = factory.Create("gen-002", AppConst.AppHub.Callbacks.ReceiveDeviceOnline, [new { CompanyId = 2, DeviceId = 101 }]);

        Assert.Equal("gen-001", eventGen1.ConnectionGeneration);
        Assert.Equal("gen-002", eventGen2.ConnectionGeneration);
        Assert.NotEqual(eventGen1.IngestionEventId, eventGen2.IngestionEventId);
    }

    #endregion

    #region TC-MAP-001 .. TC-MAP-011 (Callbacks and Canonical Mapping)

    [Fact]
    public void TC_MAP_001_Mapping_receiveDeviceOnline()
    {
        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(2));
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            "[{\"CompanyId\":2,\"DeviceId\":201,\"DeviceName\":\"Sensor 201\",\"IsOnline\":true}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.DeviceOnline, evt.Category);
        Assert.Equal(AppConst.DeliveryKinds.SnapshotCandidate, evt.Source.DeliveryKind);
        Assert.Equal(AppConst.Parsing.StatusUnmapped, evt.Parse.Status);
        Assert.Contains(AppConst.Parsing.AppHubOpaqueContractUnconfirmed, evt.Parse.Warnings);
        Assert.Equal(sourceEvent.RawArgumentsJson, evt.RawPayload.ArgumentsJson);
    }

    [Fact]
    public void TC_MAP_002_Mapping_receiveStateConnected()
    {
        var mapper = new DeviceConnectionEventMapper(CreateTenantResolver(2));
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveStateConnected,
            "[{\"CompanyId\":2,\"DeviceId\":301,\"DeviceName\":\"Device 301\",\"IsConnected\":true}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.DeviceConnection, evt.Category);
        Assert.Equal(AppConst.Parsing.StatusUnmapped, evt.Parse.Status);
        Assert.Contains(AppConst.Parsing.AppHubOpaqueContractUnconfirmed, evt.Parse.Warnings);
        Assert.Equal(sourceEvent.RawArgumentsJson, evt.RawPayload.ArgumentsJson);
    }

    [Fact]
    public void TC_MAP_003_Mapping_receiveGreenState_and_receiveRedState()
    {
        var greenMapper = new DeviceControlStateEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveGreenState);
        var redMapper = new DeviceControlStateEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveRedState);

        var greenEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveGreenState, "[{\"CompanyId\":2,\"DeviceId\":401,\"State\":1,\"Value\":true}]");
        var redEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveRedState, "[{\"CompanyId\":2,\"DeviceId\":402,\"State\":2,\"Value\":false}]");

        var greenResult = greenMapper.Map(greenEvent);
        var redResult = redMapper.Map(redEvent);

        Assert.NotNull(greenResult.Event);
        Assert.NotNull(redResult.Event);
        Assert.Equal(AppConst.Categories.DeviceControlState, greenResult.Event!.Category);
        Assert.Equal(AppConst.Categories.DeviceControlState, redResult.Event!.Category);
        Assert.Equal(AppConst.AppHub.Callbacks.ReceiveGreenState, greenResult.Event.Source.EventName);
        Assert.Equal(AppConst.AppHub.Callbacks.ReceiveRedState, redResult.Event.Source.EventName);
        Assert.Null(greenResult.Event.Facts.DeviceControlState);
    }

    [Fact]
    public void TC_MAP_004_Mapping_receiveTimeSensor()
    {
        var mapper = new DeviceSensorStateEventMapper(CreateTenantResolver(2));
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveTimeSensor,
            "[{\"CompanyId\":2,\"DeviceId\":501,\"TimeSensor\":1500}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.DeviceSensorState, evt.Category);
        Assert.Equal(AppConst.Parsing.StatusUnmapped, evt.Parse.Status);
        Assert.Contains(AppConst.Parsing.AppHubOpaqueContractUnconfirmed, evt.Parse.Warnings);
        Assert.Equal(sourceEvent.RawArgumentsJson, evt.RawPayload.ArgumentsJson);
    }

    [Fact]
    public void TC_MAP_005_Mapping_receiveDeviceReadTag()
    {
        var mapper = new DeviceReadTagEventMapper(CreateTenantResolver(2));
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceReadTag,
            "[{\"CompanyId\":2,\"DeviceId\":601,\"TagId\":\"EPC-TEST-99\"}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.TagRead, evt.Category);
        Assert.Equal(AppConst.Parsing.StatusUnmapped, evt.Parse.Status);
        Assert.Contains(AppConst.Parsing.AppHubOpaqueContractUnconfirmed, evt.Parse.Warnings);
        Assert.Null(evt.Facts.TagRead);
    }

    [Fact]
    public void TC_MAP_006_Mapping_receiveDeviceScanConnect()
    {
        var mapper = new ScannerEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect);
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
            "[{\"CompanyId\":2,\"DeviceId\":101,\"DeviceName\":\"Scanner 101\",\"GateId\":5,\"GateName\":\"Gate A\",\"SessionType\":1,\"DeviceType\":2,\"DateConnected\":\"2026-08-28T15:29:58\"}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Null(result.Failure);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.ScannerConnection, evt.Category);
        Assert.Equal(2, evt.CompanyId);
        Assert.Equal(101, evt.Device!.Id);
        Assert.Equal("Scanner 101", evt.Device.Name);
        Assert.Equal(5, evt.Device.GateId);
        Assert.Equal("Gate A", evt.Device.GateName);
        Assert.Equal("connected", evt.Facts.Connection!.Status);
        Assert.Equal(1, evt.Facts.Scanner!.SessionType);
        Assert.Equal(2, evt.Facts.Scanner.DeviceType);
    }

    [Fact]
    public void TC_MAP_007_Mapping_receiveDeviceScanDisconnect()
    {
        var mapper = new ScannerEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveDeviceScanDisconnect);
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceScanDisconnect,
            "[{\"CompanyId\":2,\"DeviceId\":102,\"DeviceName\":\"Scanner 102\",\"SessionType\":1,\"DeviceType\":2,\"DateDisconnected\":\"2026-08-28T15:30:00\"}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Null(result.Failure);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.ScannerConnection, evt.Category);
        Assert.Equal(2, evt.CompanyId);
        Assert.Equal(102, evt.Device!.Id);
        Assert.Equal("disconnected", evt.Facts.Connection!.Status);
    }

    [Fact]
    public void TC_MAP_008_Mapping_receiveRequestDeviceScanInfoOnline()
    {
        var mapper = new ScannerEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline);
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline,
            "[{\"CompanyId\":2,\"DeviceId\":103,\"DeviceName\":\"Scanner 103\",\"SessionType\":1,\"DeviceType\":2}]");

        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        var evt = result.Event!;
        Assert.Equal(AppConst.Categories.DeviceSnapshot, evt.Category);
        Assert.Equal(AppConst.DeliveryKinds.Snapshot, evt.Source.DeliveryKind);
        Assert.Equal("unknown", evt.Facts.Connection!.Status);
    }

    [Fact]
    public void TC_MAP_009_Mapping_client_device_callbacks()
    {
        var connectMapper = new ClientDeviceConnectionEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveClientDeviceConnected);
        var disconnectMapper = new ClientDeviceConnectionEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveClientDeviceDisconnected);

        var connEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveClientDeviceConnected, "[{\"CompanyId\":2,\"DeviceId\":701}]");
        var disconnEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveClientDeviceDisconnected, "[{\"CompanyId\":2,\"DeviceId\":701}]");

        var connResult = connectMapper.Map(connEvent);
        var disconnResult = disconnectMapper.Map(disconnEvent);

        Assert.NotNull(connResult.Event);
        Assert.NotNull(disconnResult.Event);
        Assert.Equal(AppConst.Categories.ClientDeviceConnection, connResult.Event!.Category);
        Assert.Equal(AppConst.Categories.ClientDeviceConnection, disconnResult.Event!.Category);
    }

    [Fact]
    public void TC_MAP_010_Callback_arguments_maintain_strict_order()
    {
        var rawJson = "[\"arg1\",123,true,null,{\"nested\":\"obj\"}]";
        var source = new AppHubSourceOptions { SourceId = TestSourceId };
        var factory = new AppHubRawSourceEventFactory(source, TimeProvider.System);
        var sourceEvent = factory.Create("gen-1", AppConst.AppHub.Callbacks.ReceiveDeviceOnline, JArray.Parse(rawJson).ToObject<object[]>());

        Assert.Equal(rawJson, sourceEvent.RawArgumentsJson);
    }

    [Fact]
    public void TC_MAP_011_CompanyId_resolved_dynamically_from_payload()
    {
        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(configuredTenantId: null, dedicatedSingleTenant: false));
        
        var eventTenant2 = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, "[{\"CompanyId\":2,\"DeviceId\":1}]");
        var eventTenant9 = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, "[{\"CompanyId\":9,\"DeviceId\":1}]");

        var result2 = mapper.Map(eventTenant2);
        var result9 = mapper.Map(eventTenant9);

        Assert.Equal(2, result2.Event!.CompanyId);
        Assert.Equal(9, result9.Event!.CompanyId);
    }

    #endregion

    #region TC-DATA-001 .. TC-DATA-009 (Data Contract & Validation)

    [Fact]
    public void TC_DATA_001_DedicatedSingleTenant_fallback()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            "[{\"DeviceId\":201}]");

        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(configuredTenantId: 7, dedicatedSingleTenant: true));
        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(7, result.Event!.CompanyId);
    }

    [Fact]
    public void TC_DATA_002_MultiTenant_missing_tenant_fails()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            "[{\"DeviceId\":201}]");

        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(configuredTenantId: 7, dedicatedSingleTenant: false));
        var result = mapper.Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.NotNull(result.Failure);
        Assert.Equal(AppConst.Parsing.TenantUnresolved, result.Failure!.Error.Code);
    }

    [Fact]
    public void TC_DATA_003_Tenant_mismatch_fails()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceOnline,
            "[{\"CompanyId\":2,\"DeviceId\":201}]");

        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(configuredTenantId: 99, dedicatedSingleTenant: true));
        var result = mapper.Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.NotNull(result.Failure);
        Assert.Equal(AppConst.Parsing.TenantMismatch, result.Failure!.Error.Code);
        Assert.False(result.Failure.Retryable);
    }

    [Theory]
    [InlineData("[{\"CompanyId\":0,\"DeviceId\":201}]")]
    [InlineData("[{\"CompanyId\":-5,\"DeviceId\":201}]")]
    [InlineData("[{\"CompanyId\":\"not_a_number\",\"DeviceId\":201}]")]
    public void TC_DATA_004_Invalid_company_id_fails(string invalidJson)
    {
        var sourceEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, invalidJson);
        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(configuredTenantId: null, dedicatedSingleTenant: false));
        var result = mapper.Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.NotNull(result.Failure);
        Assert.Equal(AppConst.Parsing.TenantUnresolved, result.Failure!.Error.Code);
    }

    [Fact]
    public void TC_DATA_005_Malformed_payload_produces_failure()
    {
        var sourceEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, "{not_an_array}");
        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(2));
        var result = mapper.Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.NotNull(result.Failure);
        Assert.Equal(AppConst.Parsing.InvalidRecordFormat, result.Failure!.Error.Code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"not-an-object\"]")]
    [InlineData("[null]")]
    public void TC_DATA_006_Missing_object_payload_produces_failure(string missingObjectJson)
    {
        var sourceEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, missingObjectJson);
        var mapper = new DeviceOnlineEventMapper(CreateTenantResolver(2));
        var result = mapper.Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("[{\"CompanyId\":2}]")]
    [InlineData("[{\"CompanyId\":2,\"DeviceId\":0}]")]
    [InlineData("[{\"CompanyId\":2,\"DeviceId\":\"abc\"}]")]
    public void TC_DATA_007_Scanner_missing_required_device_id_fails(string invalidJson)
    {
        var sourceEvent = CreateSourceEvent(AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect, invalidJson);
        var mapper = new ScannerEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect);
        var result = mapper.Map(sourceEvent);

        Assert.Null(result.Event);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public void TC_DATA_008_Optional_fields_and_warnings()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
            "[{\"CompanyId\":2,\"DeviceId\":101}]");

        var mapper = new ScannerEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect);
        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Equal(AppConst.Parsing.StatusParsedWithWarnings, result.Event!.Parse.Status);
        Assert.NotEmpty(result.Event.Parse.Warnings);
        Assert.Null(result.Event.Facts.Scanner?.ConnectionIdHash);
        Assert.Null(result.Event.Facts.User?.UserId);
    }

    [Fact]
    public void TC_DATA_009_Time_contract_untrusted_source_time()
    {
        var sourceEvent = CreateSourceEvent(
            AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect,
            "[{\"CompanyId\":2,\"DeviceId\":101,\"DateConnected\":\"2026-08-28T15:29:58\"}]");

        var mapper = new ScannerEventMapper(CreateTenantResolver(2), AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect);
        var result = mapper.Map(sourceEvent);

        Assert.NotNull(result.Event);
        Assert.Null(result.Event!.OccurredAtUtc);
        Assert.Equal(sourceEvent.ReceivedAtUtc, result.Event.ReceivedAtUtc);
        Assert.Contains(AppConst.Parsing.SourceTimeUntrusted, result.Event.Parse.Warnings);
        Assert.Equal(AppConst.Parsing.StatusParsedWithWarnings, result.Event.Parse.Status);
    }

    #endregion

    #region Helper Methods

    private static AppHubTenantResolver CreateTenantResolver(int? configuredTenantId, bool dedicatedSingleTenant = false) =>
        new(new AppHubSourceConfigurationProvider([
            new AppHubSourceMappingOptions(TestSourceId, configuredTenantId, dedicatedSingleTenant)
        ]));

    private static RawSourceEvent CreateSourceEvent(string eventName, string argumentsJson)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(argumentsJson);
        return new RawSourceEvent
        {
            IngestionEventId = "test-event-" + Guid.NewGuid().ToString("N"),
            SourceKind = AppConst.SourceKinds.ErpAppHub,
            SourceId = TestSourceId,
            SourceApplication = AppConst.AppHub.Producer,
            SourceTransport = AppConst.AppHub.Transport,
            EventName = eventName,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            RawArgumentsJson = argumentsJson,
            PayloadSha256 = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant(),
            PayloadSizeBytes = payloadBytes.Length,
            ConnectionGeneration = "gen-1",
            ReceiveSequence = 1,
            DeliveryKind = AppConst.AppHub.DeliveryKind
        };
    }

    #endregion
}
