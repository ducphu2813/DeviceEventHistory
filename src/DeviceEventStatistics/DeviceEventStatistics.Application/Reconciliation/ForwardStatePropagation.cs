using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Reconciliation;

public sealed class ForwardStatePropagation
{
    public IReadOnlyList<PropagationRange> Split(
        DateOnly from,
        DateOnly to,
        DateOnly currentEdge,
        int maximumRangeDays)
    {
        if (from > to)
        {
            throw new ArgumentException(
                StatisticsContractConstants.Messages.MSG_RECONCILIATION_RANGE_INVALID,
                nameof(from));
        }

        if (maximumRangeDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRangeDays));
        }

        var effectiveTo = to > currentEdge ? currentEdge : to;
        if (from > effectiveTo)
        {
            return [];
        }

        var result = new List<PropagationRange>();
        var cursor = from;
        while (cursor <= effectiveTo)
        {
            var chunkEnd = cursor.AddDays(maximumRangeDays - 1);
            if (chunkEnd > effectiveTo)
            {
                chunkEnd = effectiveTo;
            }

            result.Add(new PropagationRange(cursor, chunkEnd));
            cursor = chunkEnd.AddDays(1);
        }

        return result;
    }
}

public sealed record PropagationRange(DateOnly FromStatisticsDate, DateOnly ToStatisticsDate);
