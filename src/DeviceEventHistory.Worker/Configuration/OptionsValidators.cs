using System.Text.RegularExpressions;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Configuration;

internal static class ValidationMessageFormatter
{
    public static string Format(string message, params object[] arguments) =>
        AppConst.Messages.Format(message, arguments);
}

public sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return string.IsNullOrWhiteSpace(options.WorkerId)
            ? ValidateOptionsResult.Fail(AppConst.Messages.MSG_WORKER_ID_REQUIRED)
            : ValidateOptionsResult.Success;
    }
}

public sealed class IngestionOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<IngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.DefaultRetentionDays <= 0) failures.Add(AppConst.Messages.MSG_DEFAULT_RETENTION_DAYS_POSITIVE);
        if (options.FailureRetentionDays <= 0) failures.Add(AppConst.Messages.MSG_FAILURE_RETENTION_DAYS_POSITIVE);
        if (options.PersistenceRetryCount < 0) failures.Add(AppConst.Messages.MSG_PERSISTENCE_RETRY_COUNT_NON_NEGATIVE);
        if (options.ShutdownTimeout <= TimeSpan.Zero) failures.Add(AppConst.Messages.MSG_SHUTDOWN_TIMEOUT_POSITIVE);
        if (options.MaxRawPayloadBytes <= 0) failures.Add(AppConst.Messages.MSG_MAX_RAW_PAYLOAD_BYTES_POSITIVE);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class RfidRawLogOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<RfidRawLogOptions>
{
    private static readonly Regex FilePatternRegex = new(
        AppConst.RawLog.FilePatternRegex,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ValidateOptionsResult Validate(string? name, RfidRawLogOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.PollInterval <= TimeSpan.Zero) failures.Add(AppConst.Messages.MSG_POLL_INTERVAL_POSITIVE);
        if (options.ReadBufferBytes <= 0) failures.Add(AppConst.Messages.MSG_READ_BUFFER_BYTES_POSITIVE);
        if (options.MaxRecordBytes <= 0) failures.Add(AppConst.Messages.MSG_MAX_RECORD_BYTES_POSITIVE);
        if (options.LookbackDays < 0) failures.Add(AppConst.Messages.MSG_LOOKBACK_DAYS_NON_NEGATIVE);
        if (options.MaxConcurrentFiles <= 0) failures.Add(AppConst.Messages.MSG_MAX_CONCURRENT_FILES_POSITIVE);
        if (options.MaxBytesPerTurn <= 0) failures.Add(AppConst.Messages.MSG_MAX_BYTES_PER_TURN_POSITIVE);
        if (options.MaxRecordsPerTurn <= 0) failures.Add(AppConst.Messages.MSG_MAX_RECORDS_PER_TURN_POSITIVE);
        if (options.MaxTurnDuration <= TimeSpan.Zero) failures.Add(AppConst.Messages.MSG_MAX_TURN_DURATION_POSITIVE);

        ValidatePolicy(options.StartupExistingFilePolicy, nameof(options.StartupExistingFilePolicy), failures);
        ValidatePolicy(options.NewFilePolicy, nameof(options.NewFilePolicy), failures);

        if (options.Sources.Count == 0)
        {
            failures.Add(AppConst.Messages.MSG_SOURCES_REQUIRED);
        }

        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < options.Sources.Count; index++)
        {
            ValidateSource(options.Sources[index], index, sourceIds, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSource(
        AntennaSourceOptions source,
        int index,
        ISet<string> sourceIds,
        ICollection<string> failures)
    {
        var prefix = $"Sources[{index}]";
        var sourceId = source.SourceId.Trim();

        if (sourceId.Length == 0) failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_SOURCE_ID_REQUIRED, prefix));
        else if (!sourceIds.Add(sourceId)) failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_SOURCE_ID_DUPLICATED, prefix, sourceId));
        if (source.CompanyId <= 0) failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_COMPANY_ID_POSITIVE, prefix));

        ValidateSourceMode(source.Mode, prefix, failures);
        if (source.Mode == RawLogSourceMode.Local)
        {
            ValidateAbsoluteRootPath(source.RootPath, prefix, failures);
        }
        else if (source.Mode == RawLogSourceMode.RemoteHttp)
        {
            ValidateRemoteBaseUrl(source.RemoteBaseUrl, prefix, failures);
        }

        ValidateTimeZone(source.TimeZoneId, prefix, failures);
        ValidateFilePattern(source.FilePattern, prefix, failures);
    }

    private static void ValidateSourceMode(RawLogSourceMode mode, string prefix, ICollection<string> failures)
    {
        if (!Enum.IsDefined(mode))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_SOURCE_MODE_UNSUPPORTED, prefix));
        }
    }

    private static void ValidateRemoteBaseUrl(string baseUrl, string prefix, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_REMOTE_BASE_URL_REQUIRED, prefix));
            return;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_REMOTE_BASE_URL_INVALID, prefix));
        }
    }

    private static void ValidateAbsoluteRootPath(string rootPath, string prefix, ICollection<string> failures)
    {
        var value = rootPath.Trim();
        if (value.Length == 0 || !Path.IsPathRooted(value))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_ROOT_PATH_ABSOLUTE, prefix));
            return;
        }

        if (ContainsParentDirectorySegment(value))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_ROOT_PATH_NO_PARENT_DIRECTORY, prefix));
        }

        try
        {
            _ = Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_ROOT_PATH_INVALID, prefix));
        }
    }

    private static void ValidateTimeZone(string timeZoneId, string prefix, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_TIME_ZONE_REQUIRED, prefix));
            return;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_TIME_ZONE_NOT_FOUND, prefix, timeZoneId));
        }
        catch (InvalidTimeZoneException)
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_TIME_ZONE_INVALID, prefix, timeZoneId));
        }
    }

    private static void ValidateFilePattern(string filePattern, string prefix, ICollection<string> failures)
    {
        var value = filePattern.Trim();
        if (!FilePatternRegex.IsMatch(value) || ContainsParentDirectorySegment(value))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_FILE_PATTERN_SAFE, prefix, AppConst.RawLog.DefaultFilePattern));
        }
    }

    private static void ValidatePolicy(FileStartPositionPolicy policy, string propertyName, ICollection<string> failures)
    {
        if (!Enum.IsDefined(policy)) failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_POLICY_UNSUPPORTED, propertyName));
    }

    private static bool ContainsParentDirectorySegment(string value) =>
        value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == AppConst.Path.ParentDirectorySegment);
}

public sealed class MongoDbOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<MongoDbOptions>
{
    public ValidateOptionsResult Validate(string? name, MongoDbOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add(AppConst.Messages.MSG_CONNECTION_STRING_REQUIRED);
        }

        ValidateDatabaseName(options.DatabaseName, nameof(options.DatabaseName), failures);
        ValidateCollectionName(options.HistoryCollection, nameof(options.HistoryCollection), failures);
        ValidateCollectionName(options.FailureCollection, nameof(options.FailureCollection), failures);
        ValidateCollectionName(options.CheckpointCollection, nameof(options.CheckpointCollection), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateDatabaseName(string value, string propertyName, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character is '/' or '\\' or '.' or '"' or '$' or '\0'))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_MONGO_DATABASE_NAME_INVALID, propertyName));
        }
    }

    private static void ValidateCollectionName(string value, string propertyName, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > AppConst.MongoDb.MaxCollectionNameLength || value.Contains('\0') || value.StartsWith(AppConst.MongoDb.SystemCollectionPrefix, StringComparison.Ordinal))
        {
            failures.Add(ValidationMessageFormatter.Format(AppConst.Messages.MSG_MONGO_COLLECTION_NAME_INVALID, propertyName));
        }
    }
}

public sealed class ConfigurationOptionsRegistration : IConfigureOptions<MongoDbOptions>
{
    public void Configure(MongoDbOptions options) => options.ApplyEnvironmentConnectionString();
}
