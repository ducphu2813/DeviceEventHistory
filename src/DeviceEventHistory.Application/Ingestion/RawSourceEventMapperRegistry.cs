using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.Ingestion;

public sealed class RawSourceEventMapperRegistry
{
    private readonly IReadOnlyDictionary<string, IRawSourceEventMapper> mappers;
    private readonly IRawSourceEventMapper fallbackMapper;

    public RawSourceEventMapperRegistry(
        IEnumerable<IRawSourceEventMapper> registeredMappers,
        IRawSourceEventMapper fallbackMapper)
    {
        ArgumentNullException.ThrowIfNull(registeredMappers);
        ArgumentNullException.ThrowIfNull(fallbackMapper);

        this.fallbackMapper = fallbackMapper;
        var mapperMap = new Dictionary<string, IRawSourceEventMapper>(StringComparer.Ordinal);

        foreach (var mapper in registeredMappers)
        {
            ArgumentNullException.ThrowIfNull(mapper);
            var key = CreateKey(mapper.SourceKind, mapper.EventName);
            if (!mapperMap.TryAdd(key, mapper))
            {
                throw new InvalidOperationException(
                    AppConst.Messages.Format(
                        AppConst.Messages.MSG_RAW_SOURCE_EVENT_MAPPER_KEY_DUPLICATED,
                        key));
            }
        }

        mappers = mapperMap;
    }

    public CanonicalIngestionResult Map(RawSourceEvent sourceEvent)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);

        var key = CreateKey(sourceEvent.SourceKind, sourceEvent.EventName);
        return mappers.TryGetValue(key, out var mapper)
            ? mapper.Map(sourceEvent)
            : fallbackMapper.Map(sourceEvent);
    }

    public static string CreateKey(string sourceKind, string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return string.Join(AppConst.Identity.Separator, sourceKind, eventName);
    }
}
