using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Application.Ingestion;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using Newtonsoft.Json;

namespace DeviceEventHistory.Infrastructure.AppHub.Admission;

/// <summary>
/// Creates the source-neutral envelope at the SignalR callback boundary.
/// Serialization and hashing happen once, before the envelope is offered to a channel.
/// </summary>
public sealed class AppHubRawSourceEventFactory
{
    private readonly AppHubSourceOptions source;
    private readonly TimeProvider timeProvider;
    private long receiveSequence;

    public AppHubRawSourceEventFactory(
        AppHubSourceOptions source,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (string.IsNullOrWhiteSpace(source.SourceId))
        {
            throw new ArgumentException(
                AppConst.Messages.MSG_APPHUB_SOURCE_ID_REQUIRED,
                nameof(source));
        }

        this.source = source;
        this.timeProvider = timeProvider;
    }

    public RawSourceEvent Create(
        string connectionGeneration,
        string eventName,
        object[]? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionGeneration);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var orderedArgumentsJson = JsonConvert.SerializeObject(
            arguments ?? Array.Empty<object>(),
            Formatting.None);
        var payloadBytes = Encoding.UTF8.GetBytes(orderedArgumentsJson);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var envelope = new RawSourceEvent
        {
            IngestionEventId = string.Empty,
            SourceKind = AppConst.AppHub.SourceKind,
            SourceId = source.SourceId.Trim(),
            SourceApplication = AppConst.AppHub.Producer,
            SourceTransport = AppConst.AppHub.Transport,
            EventName = eventName.Trim(),
            ReceivedAtUtc = timeProvider.GetUtcNow(),
            RawArgumentsJson = orderedArgumentsJson,
            PayloadSha256 = payloadHash,
            PayloadSizeBytes = payloadBytes.LongLength,
            ConnectionGeneration = connectionGeneration.Trim(),
            ReceiveSequence = Interlocked.Increment(ref receiveSequence),
            DeliveryKind = AppConst.AppHub.DeliveryKind
        };

        return envelope with
        {
            IngestionEventId = RawSourceEventIdentityFactory.CreateEventId(envelope)
        };
    }
}
