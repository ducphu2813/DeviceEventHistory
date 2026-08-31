using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.AppHub.Transport;
using DeviceEventHistory.Infrastructure.Observability;
using DeviceEventHistory.Worker.HealthChecks;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.UnitTests;

public sealed class AppHubHealthTests
{
    [Fact]
    public void Health_state_distinguishes_connecting_running_degraded_and_unhealthy()
    {
        var state = new AppHubHealthState(
            TimeProvider.System,
            failureUnhealthyThreshold: 2);
        state.ConfigureSources(["source-a", "source-b"]);

        Assert.Equal(AppHubHealthStatus.Connecting, state.Snapshot.Status);

        state.MarkConnectionState("source-a", AppHubConnectionState.Running);
        Assert.Equal(AppHubHealthStatus.Degraded, state.Snapshot.Status);

        state.MarkConnectionState("source-b", AppHubConnectionState.Running);
        Assert.Equal(AppHubHealthStatus.Running, state.Snapshot.Status);
        Assert.All(state.Snapshot.Sources, source =>
            Assert.NotNull(source.LastSuccessfulJoinAtUtc));

        state.RecordCallbackReceived("source-a");
        state.SetChannelDepth("source-a", 4);
        Assert.Equal(4, state.Snapshot.Sources.Single(source =>
            source.SourceId == "source-a").ChannelDepth);
        Assert.NotNull(state.Snapshot.Sources.Single(source =>
            source.SourceId == "source-a").LastCallbackAtUtc);

        state.MarkConnectionFailure("source-a");
        Assert.Equal(AppHubHealthStatus.Degraded, state.Snapshot.Status);
        state.MarkConnectionFailure("source-a");
        state.MarkConnectionFailure("source-b");
        state.MarkConnectionFailure("source-b");

        Assert.Equal(AppHubHealthStatus.Unhealthy, state.Snapshot.Status);
        Assert.Equal(
            AppConst.Observability.HealthReasonAppHubUnavailable,
            state.Snapshot.Reason);
    }

    [Fact]
    public async Task Health_check_is_healthy_when_AppHub_is_disabled()
    {
        var state = new AppHubHealthState(TimeProvider.System, 2);
        var check = new AppHubHealthCheck(
            Options.Create(new WorkerOptions { Enabled = true }),
            Options.Create(new AppHubOptions { Enabled = false }),
            state);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Health_check_reports_connecting_without_treating_missing_callbacks_as_failure()
    {
        var state = new AppHubHealthState(TimeProvider.System, 2);
        state.ConfigureSources(["source-a"]);
        var check = new AppHubHealthCheck(
            Options.Create(new WorkerOptions { Enabled = true }),
            Options.Create(new AppHubOptions { Enabled = true }),
            state);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains(
            AppConst.Observability.HealthReasonAppHubConnecting,
            result.Description,
            StringComparison.Ordinal);
    }
}
