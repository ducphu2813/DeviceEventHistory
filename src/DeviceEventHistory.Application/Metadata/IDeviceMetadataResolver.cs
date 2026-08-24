namespace DeviceEventHistory.Application.Metadata;

/// <summary>
/// Resolves configured source metadata without coupling the application layer to configuration storage.
/// Device-specific catalog resolution can be added behind this boundary later.
/// </summary>
public interface IDeviceMetadataResolver
{
    IReadOnlyCollection<SourceDefinition> GetSources();

    bool TryGetSource(string sourceId, out SourceDefinition? source);
}
