using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Domain.State;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.Worker.Configuration;

internal static class ConfigurationValidationErrors
{
    public const string WorkerIdRequired = "STAT-CONFIG-WORKER-ID-REQUIRED";
    public const string ShutdownTimeoutPositive = "STAT-CONFIG-SHUTDOWN-TIMEOUT-POSITIVE";
    public const string ProjectionVersionPositive = "STAT-CONFIG-PROJECTION-VERSION-POSITIVE";
    public const string MetricSetVersionPositive = "STAT-CONFIG-METRIC-SET-VERSION-POSITIVE";
    public const string ProjectionNameRequired = "STAT-CONFIG-PROJECTION-NAME-REQUIRED";
    public const string MappingVersionRequired = "STAT-CONFIG-MAPPING-VERSION-REQUIRED";
    public const string ModeUnsupported = "STAT-CONFIG-MODE-UNSUPPORTED";
    public const string CoverageStartRequired = "STAT-CONFIG-COVERAGE-START-REQUIRED";
    public const string BatchSizePositive = "STAT-CONFIG-BATCH-SIZE-POSITIVE";
    public const string ContributionsPositive = "STAT-CONFIG-CONTRIBUTIONS-POSITIVE";
    public const string PollIntervalPositive = "STAT-CONFIG-POLL-INTERVAL-POSITIVE";
    public const string SafetyDelayNonNegative = "STAT-CONFIG-SAFETY-DELAY-NON-NEGATIVE";
    public const string OverlapNonNegative = "STAT-CONFIG-OVERLAP-NON-NEGATIVE";
    public const string DeepDiscoveryPositive = "STAT-CONFIG-DEEP-DISCOVERY-POSITIVE";
    public const string LeaseDurationPositive = "STAT-CONFIG-LEASE-DURATION-POSITIVE";
    public const string LeaseRenewIntervalPositive = "STAT-CONFIG-LEASE-RENEW-INTERVAL-POSITIVE";
    public const string LeaseRenewBeforeExpiry = "STAT-CONFIG-LEASE-RENEW-BEFORE-EXPIRY";
    public const string RetryCountPositive = "STAT-CONFIG-RETRY-COUNT-POSITIVE";
    public const string RetryMinDelayPositive = "STAT-CONFIG-RETRY-MIN-DELAY-POSITIVE";
    public const string RetryMaxDelayAfterMin = "STAT-CONFIG-RETRY-MAX-DELAY-AFTER-MIN";
    public const string ManualRangeRequired = "STAT-CONFIG-MANUAL-RANGE-REQUIRED";
    public const string ManualRangeOrdered = "STAT-CONFIG-MANUAL-RANGE-ORDERED";
    public const string ScopeRequiredForManualMode = "STAT-CONFIG-MANUAL-SCOPE-REQUIRED";
    public const string ScopeIdPositive = "STAT-CONFIG-SCOPE-ID-POSITIVE";
    public const string ScopeIdDuplicated = "STAT-CONFIG-SCOPE-ID-DUPLICATED";
    public const string StateTypeRequired = "STAT-CONFIG-STATE-TYPE-REQUIRED";
    public const string StateTypeDuplicated = "STAT-CONFIG-STATE-TYPE-DUPLICATED";
    public const string StateTypeUnsupported = "STAT-CONFIG-STATE-TYPE-UNSUPPORTED";
    public const string ForwardPropagationPositive = "STAT-CONFIG-FORWARD-PROPAGATION-POSITIVE";
    public const string StateRefreshIntervalPositive = "STAT-CONFIG-STATE-REFRESH-INTERVAL-POSITIVE";
    public const string StateRefreshPageSizePositive = "STAT-CONFIG-STATE-REFRESH-PAGE-SIZE-POSITIVE";
    public const string ReconciliationIntervalPositive = "STAT-CONFIG-RECONCILIATION-INTERVAL-POSITIVE";
    public const string RollingDaysPositive = "STAT-CONFIG-ROLLING-DAYS-POSITIVE";
    public const string RequestsPositive = "STAT-CONFIG-REQUESTS-POSITIVE";
    public const string AttemptsPositive = "STAT-CONFIG-ATTEMPTS-POSITIVE";
    public const string RangeDaysPositive = "STAT-CONFIG-RANGE-DAYS-POSITIVE";
    public const string RetentionPositive = "STAT-CONFIG-RETENTION-POSITIVE";
    public const string RetentionHeadroomInvalid = "STAT-CONFIG-RETENTION-HEADROOM-INVALID";
    public const string RecoveryLookbackPositive = "STAT-CONFIG-RECOVERY-LOOKBACK-POSITIVE";
    public const string ProjectionRunRetentionPositive = "STAT-CONFIG-PROJECTION-RUN-RETENTION-POSITIVE";
    public const string LagWarningPositive = "STAT-CONFIG-LAG-WARNING-POSITIVE";
    public const string LagViolationAfterWarning = "STAT-CONFIG-LAG-VIOLATION-AFTER-WARNING";
    public const string HealthIntervalPositive = "STAT-CONFIG-HEALTH-INTERVAL-POSITIVE";
    public const string TimeZoneFixed = "STAT-CONFIG-TIMEZONE-FIXED";
    public const string UtcOffsetFixed = "STAT-CONFIG-UTC-OFFSET-FIXED";
    public const string ConnectionStringRequired = "STAT-CONFIG-CONNECTION-STRING-REQUIRED";
    public const string EnvironmentVariableRequired = "STAT-CONFIG-ENVIRONMENT-VARIABLE-REQUIRED";
    public const string DatabaseNameInvalid = "STAT-CONFIG-DATABASE-NAME-INVALID";
    public const string CollectionNameInvalid = "STAT-CONFIG-COLLECTION-NAME-INVALID";
    public const string IndexNameInvalid = "STAT-CONFIG-INDEX-NAME-INVALID";
    public const string SchemaNameInvalid = "STAT-CONFIG-SCHEMA-NAME-INVALID";
    public const string CommandTimeoutPositive = "STAT-CONFIG-COMMAND-TIMEOUT-POSITIVE";
}

public sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.WorkerId))
        {
            failures.Add(ConfigurationValidationErrors.WorkerIdRequired);
        }

        if (options.ShutdownTimeout <= TimeSpan.Zero)
        {
            failures.Add(ConfigurationValidationErrors.ShutdownTimeoutPositive);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class ProjectionOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<ProjectionOptions>
{
    public ValidateOptionsResult Validate(string? name, ProjectionOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!Enum.IsDefined(options.Mode)) failures.Add(ConfigurationValidationErrors.ModeUnsupported);
        if (string.IsNullOrWhiteSpace(options.Name)) failures.Add(ConfigurationValidationErrors.ProjectionNameRequired);
        if (options.ProjectionVersion <= 0) failures.Add(ConfigurationValidationErrors.ProjectionVersionPositive);
        if (options.MetricSetVersion <= 0) failures.Add(ConfigurationValidationErrors.MetricSetVersionPositive);
        if (string.IsNullOrWhiteSpace(options.MappingVersion)) failures.Add(ConfigurationValidationErrors.MappingVersionRequired);
        if (!options.ResumeFromStoredDefinition && options.CoverageStartAtUtc is null)
        {
            failures.Add(ConfigurationValidationErrors.CoverageStartRequired);
        }

        if (options.BatchSize <= 0) failures.Add(ConfigurationValidationErrors.BatchSizePositive);
        if (options.MaxContributionsPerBatch <= 0) failures.Add(ConfigurationValidationErrors.ContributionsPositive);
        if (options.PollInterval <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.PollIntervalPositive);
        if (options.ReadSafetyDelay < TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.SafetyDelayNonNegative);
        if (options.OverlapWindow < TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.OverlapNonNegative);
        if (options.DeepDiscoveryInterval <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.DeepDiscoveryPositive);
        if (options.LeaseDuration <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.LeaseDurationPositive);
        if (options.LeaseRenewInterval <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.LeaseRenewIntervalPositive);
        if (options.LeaseRenewInterval >= options.LeaseDuration)
        {
            failures.Add(ConfigurationValidationErrors.LeaseRenewBeforeExpiry);
        }

        if (options.PersistenceRetryCount <= 0) failures.Add(ConfigurationValidationErrors.RetryCountPositive);
        if (options.RetryMinDelay <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.RetryMinDelayPositive);
        if (options.RetryMaxDelay < options.RetryMinDelay)
        {
            failures.Add(ConfigurationValidationErrors.RetryMaxDelayAfterMin);
        }

        ValidateScope(options.Scope, failures);
        ValidateManualRange(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateScope(ProjectionScopeOptions? scope, ICollection<string> failures)
    {
        if (scope is null)
        {
            return;
        }

        ValidateIds(scope.CompanyIds, failures);
        ValidateIds(scope.DeviceIds, failures);
    }

    private static void ValidateIds(IEnumerable<long>? ids, ICollection<string> failures)
    {
        if (ids is null)
        {
            return;
        }

        var seen = new HashSet<long>();
        foreach (var id in ids)
        {
            if (id <= 0) failures.Add(ConfigurationValidationErrors.ScopeIdPositive);
            if (!seen.Add(id)) failures.Add(ConfigurationValidationErrors.ScopeIdDuplicated);
        }
    }

    private static void ValidateManualRange(ProjectionOptions options, ICollection<string> failures)
    {
        var requiresRange = options.Mode is ProjectionMode.Bootstrap or ProjectionMode.Backfill or ProjectionMode.Rebuild;
        if (requiresRange && (options.ManualRange?.FromUtc is null || options.ManualRange.ToUtc is null))
        {
            failures.Add(ConfigurationValidationErrors.ManualRangeRequired);
            return;
        }

        if (options.ManualRange?.FromUtc is not null &&
            options.ManualRange.ToUtc is not null &&
            options.ManualRange.FromUtc >= options.ManualRange.ToUtc)
        {
            failures.Add(ConfigurationValidationErrors.ManualRangeOrdered);
        }

        if (requiresRange &&
            (options.Scope is null || options.Scope.CompanyIds.Count == 0 || options.Scope.DeviceIds.Count == 0))
        {
            failures.Add(ConfigurationValidationErrors.ScopeRequiredForManualMode);
        }
    }
}

public sealed class StateOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<StateOptions>
{
    public ValidateOptionsResult Validate(string? name, StateOptions options)
    {
        if (!workerOptions.Value.Enabled || !options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.MaxForwardPropagationDays <= 0)
        {
            failures.Add(ConfigurationValidationErrors.ForwardPropagationPositive);
        }
        if (options.RefreshInterval <= TimeSpan.Zero)
        {
            failures.Add(ConfigurationValidationErrors.StateRefreshIntervalPositive);
        }
        if (options.RefreshPageSize <= 0)
        {
            failures.Add(ConfigurationValidationErrors.StateRefreshPageSizePositive);
        }

        var stateTypes = options.StateTypes ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stateType in stateTypes)
        {
            if (string.IsNullOrWhiteSpace(stateType)) failures.Add(ConfigurationValidationErrors.StateTypeRequired);
            else if (!StateTypes.Supported.Contains(stateType.Trim())) failures.Add(ConfigurationValidationErrors.StateTypeUnsupported);
            else if (!seen.Add(stateType.Trim())) failures.Add(ConfigurationValidationErrors.StateTypeDuplicated);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class ReconciliationOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<ReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, ReconciliationOptions options)
    {
        if (!workerOptions.Value.Enabled || !options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.ScheduleInterval <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.ReconciliationIntervalPositive);
        if (options.RollingDays <= 0) failures.Add(ConfigurationValidationErrors.RollingDaysPositive);
        if (options.MaxRequestsPerRun <= 0) failures.Add(ConfigurationValidationErrors.RequestsPositive);
        if (options.MaxAttempts <= 0) failures.Add(ConfigurationValidationErrors.AttemptsPositive);
        if (options.MaxRangeDays <= 0) failures.Add(ConfigurationValidationErrors.RangeDaysPositive);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class RetentionOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<RetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, RetentionOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.MongoHistoryRetentionDays <= 0) failures.Add(ConfigurationValidationErrors.RetentionPositive);
        if (options.MinimumHistoryHeadroomDays < 0 ||
            options.MongoHistoryRetentionDays <= options.MinimumHistoryHeadroomDays)
        {
            failures.Add(ConfigurationValidationErrors.RetentionHeadroomInvalid);
        }

        if (options.RecoveryLookback <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.RecoveryLookbackPositive);
        if (options.ProjectionRunRetentionDays <= 0)
        {
            failures.Add(ConfigurationValidationErrors.ProjectionRunRetentionPositive);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class ObservabilityOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<ObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, ObservabilityOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.LagWarningAfter <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.LagWarningPositive);
        if (options.LagViolationAfter <= options.LagWarningAfter)
        {
            failures.Add(ConfigurationValidationErrors.LagViolationAfterWarning);
        }

        if (options.HealthCheckInterval <= TimeSpan.Zero) failures.Add(ConfigurationValidationErrors.HealthIntervalPositive);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class MetadataOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<MetadataOptions>
{
    public ValidateOptionsResult Validate(string? name, MetadataOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!string.Equals(options.TimeZoneId, "Asia/Ho_Chi_Minh", StringComparison.Ordinal))
        {
            failures.Add(ConfigurationValidationErrors.TimeZoneFixed);
        }

        if (options.UtcOffsetMinutes != 420)
        {
            failures.Add(ConfigurationValidationErrors.UtcOffsetFixed);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class DatabaseSettingsOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<DatabaseSettingsOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseSettingsOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        ValidateMongo(options.MongoDb, failures);
        ValidateSql(options.SqlServer, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateMongo(MongoHistoryDatabaseOptions? options, ICollection<string> failures)
    {
        if (options is null)
        {
            failures.Add(ConfigurationValidationErrors.ConnectionStringRequired);
            return;
        }

        ValidateConnection(options.ConnectionString, failures);
        ValidateEnvironmentVariable(
            options.ConnectionString,
            options.ConnectionStringEnvironmentVariable,
            failures);
        ValidateDatabaseName(options.DatabaseName, failures);
        ValidateCollectionName(options.HistoryCollection, failures);

        foreach (var indexName in options.RequiredHistoryIndexNames ?? [])
        {
            if (string.IsNullOrWhiteSpace(indexName) || indexName.Contains('\0'))
            {
                failures.Add(ConfigurationValidationErrors.IndexNameInvalid);
            }
        }
    }

    private static void ValidateSql(SqlStatisticsDatabaseOptions? options, ICollection<string> failures)
    {
        if (options is null)
        {
            failures.Add(ConfigurationValidationErrors.ConnectionStringRequired);
            return;
        }

        ValidateConnection(options.ConnectionString, failures);
        ValidateEnvironmentVariable(
            options.ConnectionString,
            options.ConnectionStringEnvironmentVariable,
            failures);
        ValidateDatabaseName(options.DatabaseName, failures);
        ValidateSqlIdentifier(options.SchemaName, ConfigurationValidationErrors.SchemaNameInvalid, failures);
        if (options.CommandTimeoutSeconds <= 0) failures.Add(ConfigurationValidationErrors.CommandTimeoutPositive);
    }

    private static void ValidateConnection(string? value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value)) failures.Add(ConfigurationValidationErrors.ConnectionStringRequired);
    }

    private static void ValidateEnvironmentVariable(
        string? connectionString,
        string? value,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(value))
        {
            failures.Add(ConfigurationValidationErrors.EnvironmentVariableRequired);
        }
    }

    private static void ValidateDatabaseName(string? value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-')))
        {
            failures.Add(ConfigurationValidationErrors.DatabaseNameInvalid);
        }
    }

    private static void ValidateCollectionName(string? value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0') || value.Contains('$') || value.Length > 120)
        {
            failures.Add(ConfigurationValidationErrors.CollectionNameInvalid);
        }
    }

    private static void ValidateSqlIdentifier(string? value, string error, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            failures.Add(error);
        }
    }
}

public sealed class DatabaseSettingsOptionsRegistration : IConfigureOptions<DatabaseSettingsOptions>
{
    public void Configure(DatabaseSettingsOptions options)
    {
        options.MongoDb.ApplyEnvironmentConnectionString();
        options.SqlServer.ApplyEnvironmentConnectionString();
    }
}
