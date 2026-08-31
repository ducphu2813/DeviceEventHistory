namespace DeviceEventHistory.Application.Ingestion;

public interface IRawSourceEventMapper
{
    string SourceKind { get; }

    string EventName { get; }

    CanonicalIngestionResult Map(RawSourceEvent sourceEvent);
}
