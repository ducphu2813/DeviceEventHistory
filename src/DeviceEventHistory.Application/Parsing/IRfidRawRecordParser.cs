namespace DeviceEventHistory.Application.Parsing;

public interface IRfidRawRecordParser
{
    RawRecordParseResult Parse(RawRecordContext context);
}
