using System.Collections.Immutable;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Application.Mapping;

public sealed record MetricRegistryIdentity(
    int MetricSetVersion,
    string MappingVersion,
    string OwnershipVersion);

public sealed record MetricRegistryEntry(
    string? MetricCode,
    int? MetricKey,
    bool? IsEnabled,
    string? MappingVersion,
    string? OwnershipVersion);

public sealed record ResolvedMetricRegistry(
    MetricRegistryIdentity Identity,
    IReadOnlyDictionary<string, int> MetricKeys)
{
    public int Count => MetricKeys.Count;

    public int this[string metricCode] => MetricKeys[metricCode];
}

public static class MetricRegistryValidator
{
    public static ResolvedMetricRegistry Validate(
        MetricRegistryIdentity identity,
        IEnumerable<MetricRegistryEntry> entries,
        IReadOnlyCollection<string> requiredMetricCodes)
    {
        var required = requiredMetricCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var grouped = entries
            .Where(entry => entry.MetricCode is not null)
            .GroupBy(entry => entry.MetricCode!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var duplicateCodes = required
            .Where(code => grouped.TryGetValue(code, out var rows) && rows.Length > 1)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (duplicateCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_DUPLICATE,
                    string.Join(',', duplicateCodes)));
        }

        var missingCodes = required
            .Where(code => !grouped.ContainsKey(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (missingCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_REQUIRED_MISSING,
                    string.Join(',', missingCodes)));
        }

        var disabledCodes = required
            .Where(code => grouped[code][0].IsEnabled is not true)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (disabledCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_DISABLED,
                    string.Join(',', disabledCodes)));
        }

        var mismatchedCodes = required
            .Where(code => !string.Equals(grouped[code][0].MappingVersion, identity.MappingVersion, StringComparison.Ordinal) ||
                           !string.Equals(grouped[code][0].OwnershipVersion, identity.OwnershipVersion, StringComparison.Ordinal))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (mismatchedCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_VERSION_MISMATCH,
                    string.Join(',', mismatchedCodes)));
        }

        var invalidCodes = required
            .Where(code => grouped[code][0].MetricKey is not > 0)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        if (invalidCodes.Length > 0)
        {
            throw new InvalidOperationException(
                StatisticsContractConstants.Messages.Format(
                    StatisticsContractConstants.Messages.MSG_SQL_METRIC_REGISTRY_INVALID,
                    string.Join(',', invalidCodes)));
        }

        var metricKeys = required.ToImmutableDictionary(
            code => code,
            code => grouped[code][0].MetricKey!.Value,
            StringComparer.Ordinal);
        return new(identity, metricKeys);
    }
}
