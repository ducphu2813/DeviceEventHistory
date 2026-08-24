using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace DeviceEventHistory.Worker.Configuration;

public sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return string.IsNullOrWhiteSpace(options.WorkerId)
            ? ValidateOptionsResult.Fail("WorkerId is required when the Worker is enabled.")
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
        if (options.DefaultRetentionDays <= 0) failures.Add("DefaultRetentionDays must be greater than zero.");
        if (options.FailureRetentionDays <= 0) failures.Add("FailureRetentionDays must be greater than zero.");
        if (options.PersistenceRetryCount < 0) failures.Add("PersistenceRetryCount cannot be negative.");
        if (options.ShutdownTimeout <= TimeSpan.Zero) failures.Add("ShutdownTimeout must be greater than zero.");
        if (options.MaxRawPayloadBytes <= 0) failures.Add("MaxRawPayloadBytes must be greater than zero.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class RfidRawLogOptionsValidator(IOptions<WorkerOptions> workerOptions)
    : IValidateOptions<RfidRawLogOptions>
{
    private static readonly Regex FilePatternRegex = new(
        @"^File_[A-Za-z0-9_?*.-]+\.txt$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ValidateOptionsResult Validate(string? name, RfidRawLogOptions options)
    {
        if (!workerOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.PollInterval <= TimeSpan.Zero) failures.Add("PollInterval must be greater than zero.");
        if (options.ReadBufferBytes <= 0) failures.Add("ReadBufferBytes must be greater than zero.");
        if (options.MaxRecordBytes <= 0) failures.Add("MaxRecordBytes must be greater than zero.");
        if (options.LookbackDays < 0) failures.Add("LookbackDays cannot be negative.");
        if (options.MaxConcurrentFiles <= 0) failures.Add("MaxConcurrentFiles must be greater than zero.");

        ValidatePolicy(options.StartupExistingFilePolicy, nameof(options.StartupExistingFilePolicy), failures);
        ValidatePolicy(options.NewFilePolicy, nameof(options.NewFilePolicy), failures);

        if (options.Sources.Count == 0)
        {
            failures.Add("At least one raw-log source is required when the Worker is enabled.");
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

        if (sourceId.Length == 0) failures.Add($"{prefix}.SourceId is required.");
        else if (!sourceIds.Add(sourceId)) failures.Add($"{prefix}.SourceId '{sourceId}' is duplicated.");
        if (source.CompanyId <= 0) failures.Add($"{prefix}.CompanyId must be greater than zero.");

        ValidateAbsoluteRootPath(source.RootPath, prefix, failures);
        ValidateTimeZone(source.TimeZoneId, prefix, failures);
        ValidateFilePattern(source.FilePattern, prefix, failures);
    }

    private static void ValidateAbsoluteRootPath(string rootPath, string prefix, ICollection<string> failures)
    {
        var value = rootPath.Trim();
        if (value.Length == 0 || !Path.IsPathRooted(value))
        {
            failures.Add($"{prefix}.RootPath must be an absolute path.");
            return;
        }

        if (ContainsParentDirectorySegment(value))
        {
            failures.Add($"{prefix}.RootPath cannot contain a parent-directory segment ('..').");
        }

        try
        {
            _ = Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            failures.Add($"{prefix}.RootPath is not a valid path.");
        }
    }

    private static void ValidateTimeZone(string timeZoneId, string prefix, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            failures.Add($"{prefix}.TimeZoneId is required.");
            return;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            failures.Add($"{prefix}.TimeZoneId '{timeZoneId}' was not found on this system.");
        }
        catch (InvalidTimeZoneException)
        {
            failures.Add($"{prefix}.TimeZoneId '{timeZoneId}' is invalid.");
        }
    }

    private static void ValidateFilePattern(string filePattern, string prefix, ICollection<string> failures)
    {
        var value = filePattern.Trim();
        if (!FilePatternRegex.IsMatch(value) || ContainsParentDirectorySegment(value))
        {
            failures.Add($"{prefix}.FilePattern must be a safe File_*.txt file-name pattern without path traversal.");
        }
    }

    private static void ValidatePolicy(FileStartPositionPolicy policy, string propertyName, ICollection<string> failures)
    {
        if (!Enum.IsDefined(policy)) failures.Add($"{propertyName} has an unsupported value.");
    }

    private static bool ContainsParentDirectorySegment(string value) =>
        value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
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
            failures.Add("ConnectionString is required through configuration or the configured environment variable.");
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
            failures.Add($"{propertyName} contains invalid MongoDB database-name characters.");
        }
    }

    private static void ValidateCollectionName(string value, string propertyName, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Contains('\0') || value.StartsWith("system.", StringComparison.Ordinal))
        {
            failures.Add($"{propertyName} is not a valid MongoDB collection name.");
        }
    }
}

public sealed class ConfigurationOptionsRegistration : IConfigureOptions<MongoDbOptions>
{
    public void Configure(MongoDbOptions options) => options.ApplyEnvironmentConnectionString();
}
