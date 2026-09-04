namespace DeviceEventStatistics.Domain.Common;

public static class StatisticsContractConstants
{
    public const string ProjectionName = "device_event_daily";
    public const string DefaultPartitionKey = "device_event_history";
    public const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";

    public static class Outcomes
    {
        public const string Aggregated = "aggregated";
        public const string Ignored = "ignored";
        public const string QualityOnly = "quality_only";
        public const string FailedTerminal = "failed_terminal";
    }

    public static class LeaseErrors
    {
        public const string NotOwned = "STAT-LEASE-NOT-OWNED";
        public const string Lost = "STAT-LEASE-LOST";
    }
}
