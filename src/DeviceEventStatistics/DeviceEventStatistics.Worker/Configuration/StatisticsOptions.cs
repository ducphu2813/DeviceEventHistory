namespace DeviceEventStatistics.Worker.Configuration;

public sealed class WorkerOptions
{
    public const string SectionName = "DeviceEventStatistics";

    public bool Enabled { get; set; }

    public string WorkerId { get; set; } = "device-event-statistics-worker";

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public enum ProjectionMode
{
    Incremental,
    Reconciliation,
    Bootstrap,
    Backfill,
    Rebuild
}

public sealed class ProjectionOptions
{
    public const string SectionName = "DeviceEventStatistics:Projection";

    public ProjectionMode Mode { get; set; } = ProjectionMode.Incremental;

    public string Name { get; set; } = "device_event_daily";

    public int ProjectionVersion { get; set; } = 1;

    public int MetricSetVersion { get; set; } = 1;

    public string MappingVersion { get; set; } = "v1";

    public bool ResumeFromStoredDefinition { get; set; }

    public DateTimeOffset? CoverageStartAtUtc { get; set; }

    public int BatchSize { get; set; } = 500;

    public int MaxContributionsPerBatch { get; set; } = 5000;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ReadSafetyDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan OverlapWindow { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan DeepDiscoveryInterval { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan LeaseRenewInterval { get; set; } = TimeSpan.FromSeconds(20);

    public int PersistenceRetryCount { get; set; } = 5;

    public TimeSpan RetryMinDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    public ProjectionScopeOptions Scope { get; set; } = new();

    public ManualRangeOptions ManualRange { get; set; } = new();
}

public sealed class ProjectionScopeOptions
{
    public List<long> CompanyIds { get; set; } = [];

    public List<long> DeviceIds { get; set; } = [];
}

public sealed class ManualRangeOptions
{
    public DateTimeOffset? FromUtc { get; set; }

    public DateTimeOffset? ToUtc { get; set; }
}

public sealed class StateOptions
{
    public const string SectionName = "DeviceEventStatistics:State";

    public bool Enabled { get; set; } = true;

    public List<string> StateTypes { get; set; } = [];

    public int MaxForwardPropagationDays { get; set; } = 31;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int RefreshPageSize { get; set; } = 100;
}

public sealed class ReconciliationOptions
{
    public const string SectionName = "DeviceEventStatistics:Reconciliation";

    public bool Enabled { get; set; } = true;

    public TimeSpan ScheduleInterval { get; set; } = TimeSpan.FromHours(1);

    public int RollingDays { get; set; } = 3;

    public int MaxRequestsPerRun { get; set; } = 100;

    public int MaxAttempts { get; set; } = 5;

    public int MaxRangeDays { get; set; } = 31;
}

public sealed class RetentionOptions
{
    public const string SectionName = "DeviceEventStatistics:Retention";

    public int MongoHistoryRetentionDays { get; set; } = 7;

    public int MinimumHistoryHeadroomDays { get; set; } = 2;

    public TimeSpan RecoveryLookback { get; set; } = TimeSpan.FromHours(1);

    public int ProjectionRunRetentionDays { get; set; } = 90;
}

public sealed class ObservabilityOptions
{
    public const string SectionName = "DeviceEventStatistics:Observability";

    public TimeSpan LagWarningAfter { get; set; } = TimeSpan.FromHours(12);

    public TimeSpan LagViolationAfter { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class MetadataOptions
{
    public const string SectionName = "DeviceEventStatistics:Metadata";

    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    public int UtcOffsetMinutes { get; set; } = 420;
}
