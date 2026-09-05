using System.Data;
using System.Security.Cryptography;
using System.Text;
using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Infrastructure.SqlServer.Mapping;

public sealed class ProjectionTvpMapper
{
    public DataTable MapProcessedEvents(IEnumerable<ProcessedEventInput> values) =>
        CreateTable(
            new[]
            {
            ("EventId", typeof(byte[])),
            ("SourceDocumentId", typeof(string)),
            ("SourceKind", typeof(string)),
            ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)),
            ("SourcePersistedAtUtc", typeof(DateTime)),
            ("StatisticsDate", typeof(DateTime)),
            ("TimelineAtUtc", typeof(DateTime)),
            ("MappingVersion", typeof(string)),
            ("Outcome", typeof(string)),
            },
            values,
            value =>
            {
                return new object?[]
                {
                    value.EventId.ToEventIdBytes(), value.SourceDocumentId, value.SourceKind,
                    value.CompanyId, value.DeviceId,
                    value.SourcePersistedAtUtc.UtcDateTime, value.StatisticsDate?.ToDateTime(),
                    value.TimelineAtUtc?.UtcDateTime, value.MappingVersion,
                    value.Outcome.ToContractValue()
                };
            });

    public DataTable MapAdmittedProcessedEvents(
        IEnumerable<ProcessedEventInput> values,
        IReadOnlySet<string> admittedEventIds) =>
        MapProcessedEvents(values.Where(value => admittedEventIds.Contains(value.EventId)));

    public DataTable MapMetricContributions(IEnumerable<MetricContribution> values) =>
        CreateTable(
            new[]
            {
            ("EventId", typeof(byte[])),
            ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)),
            ("StatisticsDate", typeof(DateTime)),
            ("MetricKey", typeof(int)),
            ("SourceKind", typeof(string)),
            ("TimelineAtUtc", typeof(DateTime)),
            ("SourcePersistedAtUtc", typeof(DateTime)),
            ("ParsedWithWarnings", typeof(bool)),
            ("TimeBasis", typeof(string)),
            },
            values,
            value => new object?[]
            {
                value.EventId.ToEventIdBytes(), value.CompanyId, value.DeviceId,
                value.StatisticsDate.ToDateTime(), value.MetricKey, value.SourceKind,
                value.TimelineAtUtc.UtcDateTime, value.SourcePersistedAtUtc.UtcDateTime,
                value.ParsedWithWarnings, value.TimeBasis.ToContractValue()
            });

    public DataTable MapDeviceSummaries(IEnumerable<DeviceSummaryContribution> values) =>
        CreateTable(
            new[]
            {
            ("EventId", typeof(byte[])),
            ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)),
            ("StatisticsDate", typeof(DateTime)),
            ("SourceKind", typeof(string)),
            ("IsError", typeof(bool)),
            ("IsWarning", typeof(bool)),
            ("TimelineAtUtc", typeof(DateTime)),
            },
            values,
            value => new object?[]
            {
                value.EventId.ToEventIdBytes(), value.CompanyId, value.DeviceId,
                value.StatisticsDate.ToDateTime(), value.SourceKind, value.IsError,
                value.IsWarning, value.TimelineAtUtc.UtcDateTime
            });

    public DataTable MapStateObservations(IEnumerable<StateObservationInput> values) =>
        CreateTable(
            new[]
            {
            ("EventId", typeof(byte[])),
            ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)),
            ("StatisticsDate", typeof(DateTime)),
            ("StateType", typeof(string)),
            ("ObservedState", typeof(string)),
            ("TimelineAtUtc", typeof(DateTime)),
            ("OpeningEvidenceKind", typeof(string)),
            },
            values,
            value => new object?[]
            {
                value.EventId.ToEventIdBytes(), value.CompanyId, value.DeviceId,
                value.StatisticsDate.ToDateTime(), value.StateType, value.ObservedState,
                value.TimelineAtUtc.UtcDateTime, value.OpeningEvidenceKind
            });

    public DataTable MapStateDailyContributions(IEnumerable<StateDailyContribution> values) =>
        CreateTable(
            new[]
            {
            ("CompanyId", typeof(long)), ("DeviceId", typeof(long)), ("StatisticsDate", typeof(DateTime)),
            ("StateType", typeof(string)), ("BucketStartAtUtc", typeof(DateTime)), ("BucketEndAtUtc", typeof(DateTime)),
            ("CalculatedThroughAtUtc", typeof(DateTime)), ("TimeZoneId", typeof(string)),
            ("OpeningState", typeof(string)), ("ClosingState", typeof(string)),
            ("OnlineSeconds", typeof(long)), ("OfflineSeconds", typeof(long)), ("UnknownSeconds", typeof(long)),
            ("ConnectedEventCount", typeof(long)), ("DisconnectedEventCount", typeof(long)), ("ReconnectCount", typeof(long)),
            ("OpeningEvidenceKind", typeof(string)), ("OpeningEvidenceEventId", typeof(byte[])),
            ("IsDirty", typeof(bool)), ("IsFinalized", typeof(bool)), ("CoverageStatus", typeof(string))
            },
            values,
            value => new object?[]
            {
                value.Key.CompanyId, value.Key.DeviceId, value.StatisticsDate.ToDateTime(), value.Key.StateType,
                value.BucketStartAtUtc.UtcDateTime, value.BucketEndAtUtc.UtcDateTime,
                value.CalculatedThroughAtUtc.UtcDateTime, value.TimeZoneId, value.OpeningState, value.ClosingState,
                value.OnlineSeconds, value.OfflineSeconds, value.UnknownSeconds, value.ConnectedEventCount,
                value.DisconnectedEventCount, value.ReconnectCount, value.OpeningEvidenceKind,
                value.OpeningEvidenceEventId?.ToEventIdBytes(), value.IsDirty, value.IsFinalized, value.CoverageStatus
            });

    public DataTable MapStateCursors(IEnumerable<StateCursorInput> values) =>
        CreateTable(
            new[]
            {
            ("CompanyId", typeof(long)), ("DeviceId", typeof(long)), ("StateType", typeof(string)),
            ("CurrentState", typeof(string)), ("StateSinceAtUtc", typeof(DateTime)),
            ("AccountedThroughAtUtc", typeof(DateTime)), ("LastTimelineAtUtc", typeof(DateTime)),
            ("LastEventId", typeof(byte[])), ("OpeningEvidenceKind", typeof(string))
            },
            values,
            value => new object?[]
            {
                value.Key.CompanyId, value.Key.DeviceId, value.Key.StateType, value.CurrentState,
                value.StateSinceAtUtc.UtcDateTime, value.AccountedThroughAtUtc.UtcDateTime,
                value.LastTimelineAtUtc.UtcDateTime, value.LastEventId.ToEventIdBytes(), value.OpeningEvidenceKind
            });

    public DataTable MapReconciliationRequests(IEnumerable<ReconciliationRequestInput> values) =>
        CreateTable(
            new[]
            {
            ("CompanyId", typeof(long)), ("DeviceId", typeof(long)), ("StateType", typeof(string)),
            ("FromStatisticsDate", typeof(DateTime)), ("ToStatisticsDate", typeof(DateTime)),
            ("ReasonCode", typeof(string)), ("RequestedAtUtc", typeof(DateTime)), ("EvidenceEventId", typeof(byte[]))
            },
            values,
            value => new object?[]
            {
                value.Key.CompanyId, value.Key.DeviceId, value.Key.StateType,
                value.FromStatisticsDate.ToDateTime(), value.ToStatisticsDate.ToDateTime(), value.ReasonCode,
                value.RequestedAtUtc.UtcDateTime, value.EvidenceEventId.ToEventIdBytes()
            });

    public DataTable MapQualityContributions(IEnumerable<QualityContribution> values) =>
        CreateTable(
            new[]
            {
            ("EventId", typeof(byte[])),
            ("QualityIdentity", typeof(byte[])),
            ("StatisticsDate", typeof(DateTime)),
            ("CompanyId", typeof(long)),
            ("SourceKind", typeof(string)),
            ("SourceId", typeof(string)),
            ("QualityCode", typeof(string)),
            ("SeenAtUtc", typeof(DateTime)),
            },
            values,
            value => new object?[]
            {
                value.EventId.ToEventIdBytes(), value.QualityIdentity.ToQualityIdentityBytes(), value.StatisticsDate.ToDateTime(),
                value.CompanyId, value.SourceKind, value.SourceId, value.QualityCode,
                value.SeenAtUtc.UtcDateTime
            });

    public DataTable MapFailures(IEnumerable<ProjectionFailureInput> values) =>
        CreateTable(
            new[]
            {
            ("FailureId", typeof(byte[])),
            ("EventId", typeof(byte[])),
            ("SourceEventIdentity", typeof(string)),
            ("CompanyId", typeof(long)),
            ("DeviceId", typeof(long)),
            ("SourceKind", typeof(string)),
            ("Category", typeof(string)),
            ("SourceEventName", typeof(string)),
            ("SourcePersistedAtUtc", typeof(DateTime)),
            ("ErrorCode", typeof(string)),
            ("ErrorStage", typeof(string)),
            ("ErrorMessage", typeof(string)),
            ("Retryable", typeof(bool)),
            ("RetryCount", typeof(int)),
            ("FirstFailedAtUtc", typeof(DateTime)),
            ("LastFailedAtUtc", typeof(DateTime)),
            },
            values,
            value => new object?[]
            {
                value.FailureId.ToEventIdBytes(), value.EventId?.ToEventIdBytes(),
                value.SourceEventIdentity, value.CompanyId, value.DeviceId, value.SourceKind,
                value.Category, value.SourceEventName, value.SourcePersistedAtUtc?.UtcDateTime,
                value.ErrorCode, value.ErrorStage, value.ErrorMessage, value.Retryable,
                value.RetryCount, value.FirstFailedAtUtc.UtcDateTime, value.LastFailedAtUtc.UtcDateTime
            });

    private static DataTable CreateTable<T>(
        (string Name, Type Type)[] columns,
        IEnumerable<T> values,
        Func<T, object?[]> selector)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns) table.Columns.Add(name, type);
        foreach (var value in values)
        {
            var row = table.NewRow();
            var fields = selector(value);
            for (var index = 0; index < fields.Length; index++) row[index] = fields[index] ?? DBNull.Value;
            table.Rows.Add(row);
        }

        return table;
    }
}

internal static class ProjectionTvpValueExtensions
{
    public static byte[] ToEventIdBytes(this string value)
    {
        if (value.Length != 64 || value.Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new FormatException(
                StatisticsContractConstants.Messages.MSG_EVENT_ID_INVALID);
        }

        return Convert.FromHexString(value);
    }

    public static byte[] ToQualityIdentityBytes(this string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public static DateTime ToDateTime(this DateOnly value) =>
        value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

    public static string ToContractValue(this ProjectionEventDisposition value) =>
        value switch
        {
            ProjectionEventDisposition.Aggregated => "aggregated",
            ProjectionEventDisposition.Ignored => "ignored",
            ProjectionEventDisposition.QualityOnly => "quality_only",
            ProjectionEventDisposition.FailedTerminal => "failed_terminal",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    public static string ToContractValue(this EventTimeBasis value) =>
        value switch
        {
            EventTimeBasis.Occurred => "occurred",
            EventTimeBasis.Received => "received",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
}
