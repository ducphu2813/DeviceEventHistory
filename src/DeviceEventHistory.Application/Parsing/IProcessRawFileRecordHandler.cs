namespace DeviceEventHistory.Application.Parsing;

public interface IProcessRawFileRecordHandler
{
    RawRecordProcessingResult Handle(RawRecordContext context);
}
