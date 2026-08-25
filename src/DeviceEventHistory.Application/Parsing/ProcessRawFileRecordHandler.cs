namespace DeviceEventHistory.Application.Parsing;

public sealed class ProcessRawFileRecordHandler(
    IRfidRawRecordParser parser,
    IRawRecordCanonicalMapper mapper) : IProcessRawFileRecordHandler
{
    public RawRecordProcessingResult Handle(RawRecordContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return mapper.Map(parser.Parse(context));
    }
}
