using System.Globalization;

using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;

public sealed class RfidRawRecordParser(BlockTokenizer tokenizer) : IRfidRawRecordParser
{
    private static readonly HashSet<string> KnownBlocks =
    [
        AppConst.RawLog.HeaderBlock,
        AppConst.RawLog.GateStateBlock,
        AppConst.RawLog.SignalBlock,
        AppConst.RawLog.BusinessEventBlock,
        AppConst.RawLog.StyleProcessBlock,
        AppConst.RawLog.UserBlock
    ];

    public RawRecordParseResult Parse(RawRecordContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tokenization = tokenizer.Tokenize(context.RawPayloadText);
        var issues = new List<RawParseIssue>(tokenization.Issues);

        foreach (var block in tokenization.Blocks.Where(block => !KnownBlocks.Contains(block.Name)))
        {
            issues.Add(new RawParseIssue(
                AppConst.Parsing.UnknownRawBlock,
                AppConst.Messages.Format(AppConst.Messages.MSG_RAW_BLOCK_UNKNOWN, block.Name),
                true));
        }

        var headerTokens = tokenization.Blocks
            .Where(block => string.Equals(block.Name, AppConst.RawLog.HeaderBlock, StringComparison.Ordinal))
            .ToArray();
        var headerToken = headerTokens.LastOrDefault();
        ParsedRfidRawRecord.RfidHeaderFacts? header = null;
        if (headerTokens.Length != 1)
        {
            issues.Add(new RawParseIssue(
                AppConst.Parsing.InvalidRecordFormat,
                AppConst.Messages.MSG_RAW_RECORD_HEADER_REQUIRED,
                false));
        }

        if (headerToken is not null)
        {
            header = ParseHeader(headerToken, issues);
        }

        var timeZoneIsValid = TryGetTimeZone(context.TimeZoneId);
        if (!timeZoneIsValid)
        {
            issues.Add(new RawParseIssue(
                AppConst.Parsing.InvalidSourceTimeZone,
                AppConst.Messages.Format(AppConst.Messages.MSG_RAW_TIME_ZONE_INVALID, context.TimeZoneId),
                false));
        }

        var parsed = new ParsedRfidRawRecord
        {
            Header = header,
            GateState = ParseGateState(GetLastBlock(tokenization.Blocks, AppConst.RawLog.GateStateBlock), issues),
            Signal = ParseSignal(GetLastBlock(tokenization.Blocks, AppConst.RawLog.SignalBlock), issues),
            BusinessEvent = ParseBusinessEvent(GetLastBlock(tokenization.Blocks, AppConst.RawLog.BusinessEventBlock), issues),
            StyleProcess = ParseStyleProcess(GetLastBlock(tokenization.Blocks, AppConst.RawLog.StyleProcessBlock), issues),
            User = ParseUser(GetLastBlock(tokenization.Blocks, AppConst.RawLog.UserBlock), issues)
        };

        var hasErrors = issues.Any(issue => !issue.IsWarning);
        return new RawRecordParseResult
        {
            Context = context,
            Parsed = parsed,
            Status = hasErrors
                ? RawRecordParseStatus.Failed
                : issues.Count == 0
                    ? RawRecordParseStatus.Parsed
                    : RawRecordParseStatus.ParsedWithWarnings,
            Issues = issues
        };
    }

    private static ParsedRfidRawRecord.RfidHeaderFacts ParseHeader(
        RawBlockToken token,
        ICollection<RawParseIssue> issues)
    {
        var fields = SplitFields(token.Arguments);
        AddFieldCountIssue(token, fields, AppConst.RawLog.HeaderFieldCount, issues);

        return new ParsedRfidRawRecord.RfidHeaderFacts
        {
            TagId = ParseRequiredString(token, fields, 0, issues),
            ReadTimeText = GetField(fields, 1),
            ReadTime = ParseTimeSpan(token, fields, 1, issues),
            DeviceId = ParseInt(token, fields, 2, issues),
            GateId = ParseInt(token, fields, 3, issues)
        };
    }

    private static ParsedRfidRawRecord.GateStateFacts? ParseGateState(
        RawBlockToken? token,
        ICollection<RawParseIssue> issues)
    {
        if (token is null)
        {
            return null;
        }

        var fields = SplitFields(token.Arguments);
        AddFieldCountIssue(token, fields, AppConst.RawLog.GateStateFieldCount, issues);
        return new ParsedRfidRawRecord.GateStateFacts
        {
            StateCode = ParseInt(token, fields, 0, issues),
            RawValue = GetField(fields, 0)
        };
    }

    private static ParsedRfidRawRecord.SignalFacts? ParseSignal(
        RawBlockToken? token,
        ICollection<RawParseIssue> issues)
    {
        if (token is null)
        {
            return null;
        }

        var fields = SplitFields(token.Arguments);
        AddFieldCountIssue(token, fields, AppConst.RawLog.SignalFieldCount, issues);
        return new ParsedRfidRawRecord.SignalFacts
        {
            AntennaPort = ParseInt(token, fields, 0, issues),
            FirstSeenAtLocal = ParseDateTime(token, fields, 1, issues),
            LastSeenAtLocal = ParseDateTime(token, fields, 2, issues),
            SeenCount = ParseInt(token, fields, 3, issues),
            TxPower = ParseInt(token, fields, 4, issues),
            DopplerFrequency = ParseDouble(token, fields, 5, issues),
            PhaseAngle = ParseDouble(token, fields, 6, issues),
            ChannelMhz = ParseDouble(token, fields, 7, issues),
            PeakRssiDbm = ParseDouble(token, fields, 8, issues)
        };
    }

    private static ParsedRfidRawRecord.BusinessEventFacts? ParseBusinessEvent(
        RawBlockToken? token,
        ICollection<RawParseIssue> issues)
    {
        if (token is null)
        {
            return null;
        }

        var fields = SplitFields(token.Arguments);
        AddFieldCountIssue(token, fields, AppConst.RawLog.BusinessEventFieldCount, issues);
        var processIdsRaw = GetField(fields, 3);
        return new ParsedRfidRawRecord.BusinessEventFacts
        {
            EventType = ParseInt(token, fields, 0, issues),
            ProcessId = ParseInt(token, fields, 1, issues),
            Quantity = ParseInt(token, fields, 2, issues),
            ProcessIdsRaw = processIdsRaw,
            ProcessIds = ParseIntegerList(processIdsRaw),
            Second = ParseInt(token, fields, 4, issues)
        };
    }

    private static ParsedRfidRawRecord.StyleProcessFacts? ParseStyleProcess(
        RawBlockToken? token,
        ICollection<RawParseIssue> issues)
    {
        if (token is null)
        {
            return null;
        }

        var fields = SplitFields(token.Arguments);
        AddFieldCountIssue(token, fields, AppConst.RawLog.StyleProcessFieldCount, issues);
        var rawValue = GetField(fields, 0);
        return new ParsedRfidRawRecord.StyleProcessFacts
        {
            ProcessCustomRaw = rawValue,
            ProcessCustom = ParseIntegerList(rawValue)
        };
    }

    private static ParsedRfidRawRecord.UserFacts? ParseUser(
        RawBlockToken? token,
        ICollection<RawParseIssue> issues)
    {
        if (token is null)
        {
            return null;
        }

        var fields = SplitFields(token.Arguments);
        AddFieldCountIssue(token, fields, AppConst.RawLog.UserFieldCount, issues);
        return new ParsedRfidRawRecord.UserFacts
        {
            UserId = ParseInt(token, fields, 0, issues)
        };
    }

    private static TimeSpan? ParseTimeSpan(
        RawBlockToken token,
        IReadOnlyList<string> fields,
        int index,
        ICollection<RawParseIssue> issues)
    {
        var value = GetField(fields, index);
        if (string.IsNullOrWhiteSpace(value))
        {
            AddInvalidFieldIssue(token, index, value, issues);
            return null;
        }

        var formats = new[] { AppConst.RawLog.HeaderTimeFormat, AppConst.RawLog.HeaderTimeShortFormat };
        if (TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        AddInvalidFieldIssue(token, index, value, issues);
        return null;
    }

    private static DateTime? ParseDateTime(
        RawBlockToken token,
        IReadOnlyList<string> fields,
        int index,
        ICollection<RawParseIssue> issues)
    {
        var value = GetField(fields, index);
        var formats = new[] { AppConst.RawLog.SignalDateTimeFormat, AppConst.RawLog.SignalDateTimeShortFormat };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            return DateTime.SpecifyKind(result, DateTimeKind.Unspecified);
        }

        AddInvalidFieldIssue(token, index, value, issues);
        return null;
    }

    private static int? ParseInt(
        RawBlockToken token,
        IReadOnlyList<string> fields,
        int index,
        ICollection<RawParseIssue> issues)
    {
        var value = GetField(fields, index);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        AddInvalidFieldIssue(token, index, value, issues);
        return null;
    }

    private static double? ParseDouble(
        RawBlockToken token,
        IReadOnlyList<string> fields,
        int index,
        ICollection<RawParseIssue> issues)
    {
        var value = GetField(fields, index);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        AddInvalidFieldIssue(token, index, value, issues);
        return null;
    }

    private static string? ParseRequiredString(
        RawBlockToken token,
        IReadOnlyList<string> fields,
        int index,
        ICollection<RawParseIssue> issues)
    {
        var value = GetField(fields, index);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        AddInvalidFieldIssue(token, index, value, issues);
        return null;
    }

    private static IReadOnlyList<int>? ParseIntegerList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var values = value.Split(AppConst.RawLog.ProcessListSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>(values.Length);
        foreach (var item in values)
        {
            if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return null;
            }

            result.Add(parsed);
        }

        return result;
    }

    private static RawBlockToken? GetLastBlock(IReadOnlyList<RawBlockToken> blocks, string name) =>
        blocks.LastOrDefault(block => string.Equals(block.Name, name, StringComparison.Ordinal));

    private static IReadOnlyList<string> SplitFields(string arguments) =>
        arguments.Split(',', StringSplitOptions.None)
            .Select(field => field.Trim())
            .ToArray();

    private static string? GetField(IReadOnlyList<string> fields, int index) =>
        index < fields.Count ? fields[index] : null;

    private static void AddFieldCountIssue(
        RawBlockToken token,
        IReadOnlyCollection<string> fields,
        int expected,
        ICollection<RawParseIssue> issues)
    {
        if (fields.Count != expected)
        {
            issues.Add(new RawParseIssue(
                AppConst.Parsing.InvalidRawBlock,
                AppConst.Messages.Format(AppConst.Messages.MSG_RAW_BLOCK_FIELD_COUNT, token.Name, expected, fields.Count),
                false));
        }
    }

    private static void AddInvalidFieldIssue(
        RawBlockToken token,
        int index,
        string? value,
        ICollection<RawParseIssue> issues) =>
        issues.Add(new RawParseIssue(
            AppConst.Parsing.InvalidRawBlock,
            AppConst.Messages.Format(AppConst.Messages.MSG_RAW_BLOCK_FIELD_INVALID, token.Name, index, value ?? string.Empty),
            false));

    private static bool TryGetTimeZone(string timeZoneId)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
