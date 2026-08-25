using DeviceEventHistory.Application.Parsing;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Parsing;

public sealed class BlockTokenizer
{
    public BlockTokenizationResult Tokenize(string rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        var blocks = new List<RawBlockToken>();
        var issues = new List<RawParseIssue>();
        var index = 0;

        while (index < rawPayload.Length)
        {
            SkipWhitespace(rawPayload, ref index);
            if (index >= rawPayload.Length)
            {
                break;
            }

            if (IsTerminatorAt(rawPayload, index))
            {
                index += AppConst.RawLog.RecordTerminator.Length;
                continue;
            }

            var nameStart = index;
            while (index < rawPayload.Length && rawPayload[index] != '(')
            {
                index++;
            }

            if (index >= rawPayload.Length)
            {
                issues.Add(CreateMalformedIssue(rawPayload[nameStart..].Trim()));
                break;
            }

            var name = rawPayload[nameStart..index].Trim();
            if (name.Length == 0)
            {
                issues.Add(CreateMalformedIssue(string.Empty));
                index++;
                continue;
            }

            var closeIndex = FindClosingParenthesis(rawPayload, index);
            if (closeIndex < 0)
            {
                issues.Add(CreateMalformedIssue(name));
                break;
            }

            blocks.Add(new RawBlockToken
            {
                Name = name,
                Arguments = rawPayload[(index + 1)..closeIndex],
                RawText = rawPayload[nameStart..(closeIndex + 1)]
            });
            index = closeIndex + 1;
        }

        return new BlockTokenizationResult
        {
            Blocks = blocks,
            Issues = issues
        };
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || value[index] == '\uFEFF'))
        {
            index++;
        }
    }

    private static bool IsTerminatorAt(string value, int index) =>
        index + AppConst.RawLog.RecordTerminator.Length <= value.Length &&
        string.Equals(
            value.Substring(index, AppConst.RawLog.RecordTerminator.Length),
            AppConst.RawLog.RecordTerminator,
            StringComparison.Ordinal);

    private static int FindClosingParenthesis(string value, int openingIndex)
    {
        var depth = 1;
        for (var index = openingIndex + 1; index < value.Length; index++)
        {
            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static RawParseIssue CreateMalformedIssue(string blockName) =>
        new(
            AppConst.Parsing.InvalidRawBlock,
            AppConst.Messages.Format(AppConst.Messages.MSG_RAW_BLOCK_MALFORMED, blockName),
            false);
}
