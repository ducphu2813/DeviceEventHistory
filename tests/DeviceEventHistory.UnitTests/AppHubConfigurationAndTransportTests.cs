using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.UnitTests;

public sealed class AppHubConfigurationAndTransportTests
{
    [Fact]
    public void Valid_AppHub_configuration_passes_validation()
    {
        var options = CreateAppHubOptions();
        var validator = new AppHubOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }),
            Options.Create(new RfidRawLogOptions()));

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AppHub_configuration_rejects_invalid_endpoint_callback_and_tenant_settings()
    {
        var options = CreateAppHubOptions();
        var source = options.Sources[0];
        source.Endpoint = "https://user:password@example.com/signalr?token=secret";
        source.CompanyId = 0;
        source.DedicatedSingleTenant = true;
        source.ChannelCapacity = 0;
        source.EnqueueTimeout = TimeSpan.Zero;
        source.ReconnectMinDelay = TimeSpan.FromSeconds(5);
        source.ReconnectMaxDelay = TimeSpan.FromSeconds(1);
        source.EnabledEvents = ["unknownCallback", AppConst.AppHub.Callbacks.ReceiveDeviceOnline];
        source.AccessTokenEnvironmentVariable = null;
        source.TokenJwtEnvironmentVariable = null;
        var validator = new AppHubOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }),
            Options.Create(new RfidRawLogOptions()));

        var result = validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("Endpoint", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("CompanyId", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("ChannelCapacity", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("EnqueueTimeout", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("Reconnect", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("unsupported callback", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("AccessToken/TokenJwt", StringComparison.Ordinal));
    }

    [Fact]
    public void AppHub_source_ids_are_unique_across_raw_log_and_AppHub_sources()
    {
        var rawLog = new RfidRawLogOptions
        {
            Sources =
            [
                new AntennaSourceOptions { SourceId = "shared-source" }
            ]
        };
        var validator = new AppHubOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }),
            Options.Create(rawLog));

        var result = validator.Validate(null, CreateAppHubOptions("SHARED-SOURCE"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Redacted_summary_exposes_only_AppHub_host_and_credential_status()
    {
        var variableName = $"APPHUB_TEST_TOKEN_{Guid.NewGuid():N}";
        const string secret = "user-cookie-secret";
        Environment.SetEnvironmentVariable(variableName, secret);

        try
        {
            var appHub = CreateAppHubOptions();
            appHub.Sources[0].AccessTokenEnvironmentVariable = variableName;
            var summary = new ConfigurationRedactor().CreateSummary(
                new WorkerOptions { Enabled = true, WorkerId = "worker-01" },
                new RfidRawLogOptions(),
                new DeviceEventHistory.Infrastructure.MongoDb.Configuration.MongoDbOptions(),
                appHub);

            Assert.DoesNotContain(secret, summary.ToString(), StringComparison.Ordinal);
            Assert.Equal("erp.example.com", summary.AppHubSources.Single().EndpointHost);
            Assert.True(summary.AppHubSources.Single().CredentialConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Direct_AppHub_credential_is_validated_and_redacted()
    {
        const string secret = "direct-user-cookie-secret";
        var appHub = CreateAppHubOptions();
        var source = appHub.Sources[0];
        source.AccessToken = secret;
        source.AccessTokenEnvironmentVariable = null;

        var validator = new AppHubOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }),
            Options.Create(new RfidRawLogOptions()));
        var validation = validator.Validate(null, appHub);

        var summary = new ConfigurationRedactor().CreateSummary(
            new WorkerOptions { Enabled = true, WorkerId = "worker-01" },
            new RfidRawLogOptions(),
            new DeviceEventHistory.Infrastructure.MongoDb.Configuration.MongoDbOptions(),
            appHub);

        Assert.True(validation.Succeeded);
        Assert.DoesNotContain(secret, summary.ToString(), StringComparison.Ordinal);
        Assert.True(summary.AppHubSources.Single().CredentialConfigured);
    }

    [Fact]
    public void Jwt_credential_uses_tokenjwt_and_account_session_type()
    {
        var source = CreateAppHubOptions().Sources[0];
        source.AccessToken = "header.payload.signature";
        source.TokenJwt = null;

        var query = AppHubSignalRQueryFactory.Create(source);

        Assert.Equal("header.payload.signature", query[AppConst.AppHub.JwtTokenQueryKey]);
        Assert.Equal(AppConst.AppHub.AccountSessionTypeValue, query[AppConst.AppHub.SessionTypeQueryKey]);
        Assert.False(query.ContainsKey(AppConst.AppHub.AccessTokenQueryKey));
    }

    [Fact]
    public void User_cookie_credential_uses_token_and_account_session_type()
    {
        var source = CreateAppHubOptions().Sources[0];
        source.AccessToken = "encrypted-user-cookie";
        source.TokenJwt = null;

        var query = AppHubSignalRQueryFactory.Create(source);

        Assert.Equal("encrypted-user-cookie", query[AppConst.AppHub.AccessTokenQueryKey]);
        Assert.Equal(AppConst.AppHub.AccountSessionTypeValue, query[AppConst.AppHub.SessionTypeQueryKey]);
        Assert.False(query.ContainsKey(AppConst.AppHub.JwtTokenQueryKey));
    }

    [Fact]
    public async Task Connection_registers_callbacks_before_start_and_joins_after_connect()
    {
        var client = new FakeSignalRClient();
        await using var connection = new AppHubMonitoringConnection(
            "apphub-source",
            AppConst.AppHub.DefaultHubName,
            client,
            "generation-1");

        connection.RegisterCallback("receiveDeviceOnline", _ => { });
        await connection.StartAsync(CancellationToken.None);

        Assert.Equal(
            ["create-proxy", "register:receiveDeviceOnline", "start", "join-monitoring", "join-anten"],
            client.Calls);
        Assert.Equal(AppHubConnectionState.Running, connection.State);
    }

    [Fact]
    public async Task Reconnect_rejoins_monitoring_without_registering_callbacks_again()
    {
        var client = new FakeSignalRClient();
        await using var connection = new AppHubMonitoringConnection(
            "apphub-source",
            AppConst.AppHub.DefaultHubName,
            client,
            "generation-1");

        connection.RegisterCallback("receiveDeviceOnline", _ => { });
        await connection.StartAsync(CancellationToken.None);
        client.RaiseReconnecting();
        client.RaiseReconnected();
        client.RaiseReconnected();

        await WaitUntilAsync(() => connection.State == AppHubConnectionState.Running);

        Assert.Equal(2, client.Proxy.JoinMonitoringCount);
        Assert.Equal(2, client.Proxy.JoinAntenCount);
        Assert.Equal(1, client.Proxy.RegistrationCount);
    }

    [Fact]
    public async Task Callback_registration_after_start_is_rejected()
    {
        var client = new FakeSignalRClient();
        await using var connection = new AppHubMonitoringConnection(
            "apphub-source",
            AppConst.AppHub.DefaultHubName,
            client,
            "generation-1");

        await connection.StartAsync(CancellationToken.None);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            connection.RegisterCallback("receiveDeviceOnline", _ => { }));

        Assert.Equal(
            AppConst.Messages.MSG_APPHUB_CALLBACK_REGISTERED_AFTER_START,
            exception.Message);
    }

    private static AppHubOptions CreateAppHubOptions(string sourceId = "apphub-source") => new()
    {
        Enabled = true,
        Sources =
        [
            new AppHubSourceOptions
            {
                SourceId = sourceId,
                Endpoint = "https://erp.example.com/signalr",
                HubName = AppConst.AppHub.DefaultHubName,
                CompanyId = null,
                DedicatedSingleTenant = false,
                EnabledEvents = [AppConst.AppHub.Callbacks.ReceiveDeviceOnline],
                AccessTokenEnvironmentVariable = "APPHUB_TEST_TOKEN"
            }
        ]
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class FakeSignalRClient : IAppHubSignalRClient
    {
        public List<string> Calls { get; } = [];

        public FakeSignalRProxy Proxy { get; }

        private Action? closed;

        public FakeSignalRClient()
        {
            Proxy = new FakeSignalRProxy(Calls);
        }

        public event Action? Reconnecting;

        public event Action? Reconnected;

        public event Action? Closed
        {
            add => closed += value;
            remove => closed -= value;
        }

        public IAppHubSignalRProxy CreateProxy(string hubName)
        {
            Calls.Add("create-proxy");
            return Proxy;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Calls.Add("start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RaiseReconnecting() => Reconnecting?.Invoke();

        public void RaiseReconnected() => Reconnected?.Invoke();

        public void Dispose() => closed?.Invoke();
    }

    private sealed class FakeSignalRProxy(List<string> calls) : IAppHubSignalRProxy
    {
        public int RegistrationCount { get; private set; }

        public int JoinMonitoringCount { get; private set; }

        public int JoinAntenCount { get; private set; }

        public IDisposable RegisterCallback(string eventName, Action<object[]> callback)
        {
            calls.Add($"register:{eventName}");
            RegistrationCount++;
            return new CallbackSubscription();
        }

        public Task JoinMonitoringAsync(CancellationToken cancellationToken)
        {
            calls.Add("join-monitoring");
            JoinMonitoringCount++;
            return Task.CompletedTask;
        }

        public Task JoinAntenAsync(CancellationToken cancellationToken)
        {
            calls.Add("join-anten");
            JoinAntenCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CallbackSubscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
