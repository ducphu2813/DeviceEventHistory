using DeviceEventHistory.Domain.Events;

namespace DeviceEventHistory.Application.Parsing;

public interface IRawRecordCanonicalMapper
{
    RawRecordProcessingResult Map(RawRecordParseResult result);
}
