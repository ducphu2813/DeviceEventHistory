using System.Diagnostics.Metrics;
using DeviceEventHistory.Application.Observability;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.Observability;

namespace DeviceEventHistory.UnitTests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Health_state_moves_from_ready_to_degraded_to_unhealthy()
    {
        var state = new IngestionHealthState(
            TimeProvider.System,
            mongoFailureUnhealthyThreshold: 2,
            sourceFailureUnhealthyThreshold: 2,
            progressStaleAfter: TimeSpan.FromMinutes(5));
        state.ConfigureSources(["source-a"]);
        state.MarkLive();

        Assert.Equal(IngestionHealthStatus.Degraded, state.Snapshot.Status);

        state.MarkStartupReady();
        state.MarkSourceAvailable("source-a");
        Assert.Equal(IngestionHealthStatus.Ready, state.Snapshot.Status);

        state.MarkSourceUnavailable("source-a");
        Assert.Equal(IngestionHealthStatus.Degraded, state.Snapshot.Status);

        state.MarkSourceUnavailable("source-a");
        Assert.Equal(IngestionHealthStatus.Unhealthy, state.Snapshot.Status);
        Assert.Equal(
            AppConst.Observability.HealthReasonSourceUnavailable,
            state.Snapshot.Reason);
    }

    [Fact]
    public void Truncated_file_is_reported_as_unhealthy_without_raw_payload()
    {
        var state = new IngestionHealthState(
            TimeProvider.System,
            mongoFailureUnhealthyThreshold: 3,
            sourceFailureUnhealthyThreshold: 3,
            progressStaleAfter: TimeSpan.FromMinutes(5));
        state.ConfigureSources(["source-a"]);
        state.MarkLive();
        state.MarkStartupReady();
        state.MarkSourceAvailable("source-a");
        state.MarkFileTruncated("source-a", 7);

        var snapshot = state.Snapshot;
        Assert.Equal(IngestionHealthStatus.Unhealthy, snapshot.Status);
        Assert.Equal(AppConst.Observability.HealthReasonFileTruncated, snapshot.Reason);
        Assert.DoesNotContain(snapshot.Files, file => file.LastResult.Contains("raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Metrics_keep_source_and_file_labels_bounded()
    {
        var state = new IngestionHealthState(
            TimeProvider.System,
            mongoFailureUnhealthyThreshold: 3,
            sourceFailureUnhealthyThreshold: 3,
            progressStaleAfter: TimeSpan.FromMinutes(5));
        var metrics = new IngestionMetrics(state);
        var observed = new List<(string Name, IReadOnlyList<KeyValuePair<string, object?>> Tags)>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AppConst.Observability.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            observed.Add((instrument.Name, tags.ToArray()));
        });
        listener.Start();

        metrics.RecordBytesRead("source-a", 7, 128);
        listener.RecordObservableInstruments();

        var measurement = Assert.Single(
            observed,
            item => item.Name == AppConst.Observability.MetricBytesRead);
        Assert.Contains(measurement.Tags, tag =>
            tag.Key == AppConst.Observability.TagSourceId &&
            Equals(tag.Value, "source-a"));
        Assert.Contains(measurement.Tags, tag =>
            tag.Key == AppConst.Observability.TagFileId &&
            Equals(tag.Value, 7L));
        Assert.DoesNotContain(measurement.Tags, tag =>
            string.Equals(tag.Key, "RelativePath", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(measurement.Tags, tag =>
            string.Equals(tag.Key, "EventId", StringComparison.OrdinalIgnoreCase));
    }
}
