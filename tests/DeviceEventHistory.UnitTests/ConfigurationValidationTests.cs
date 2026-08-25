using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.UnitTests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Option_defaults_are_defined_by_shared_application_constants()
    {
        var worker = new WorkerOptions();
        var rawLog = new RfidRawLogOptions();
        var mongo = new MongoDbOptions();
        var ingestion = new IngestionOptions();

        Assert.Equal(AppConst.Defaults.WorkerEnabled, worker.Enabled);
        Assert.Equal(AppConst.Defaults.WorkerId, worker.WorkerId);
        Assert.Equal(AppConst.Defaults.ReadBufferBytes, rawLog.ReadBufferBytes);
        Assert.Equal(AppConst.Defaults.MaxRecordBytes, rawLog.MaxRecordBytes);
        Assert.Equal(AppConst.RawLog.DefaultFilePattern, new AntennaSourceOptions().FilePattern);
        Assert.Equal(AppConst.MongoDb.DefaultDatabaseName, mongo.DatabaseName);
        Assert.Equal(AppConst.Defaults.DefaultRetentionDays, ingestion.DefaultRetentionDays);
    }

    [Fact]
    public void Enabled_valid_configuration_passes_all_validators()
    {
        var worker = new WorkerOptions { Enabled = true, WorkerId = "worker-01" };
        var rawLog = CreateRawLog();
        var mongo = new MongoDbOptions { ConnectionString = "mongodb://localhost:27017" };
        var ingestion = new IngestionOptions();

        Assert.True(new WorkerOptionsValidator().Validate(null, worker).Succeeded);
        Assert.True(new RfidRawLogOptionsValidator(Options.Create(worker)).Validate(null, rawLog).Succeeded);
        Assert.True(new MongoDbOptionsValidator(Options.Create(worker)).Validate(null, mongo).Succeeded);
        Assert.True(new IngestionOptionsValidator(Options.Create(worker)).Validate(null, ingestion).Succeeded);
    }

    [Fact]
    public void Duplicate_source_ids_are_rejected_case_insensitively()
    {
        var options = CreateRawLog();
        options.Sources.Add(new AntennaSourceOptions
        {
            SourceId = "ANTENNA-SITE-A",
            RootPath = "D:/RFID/RawData-2",
            CompanyId = 2,
            TimeZoneId = "UTC",
            FilePattern = AppConst.RawLog.DefaultFilePattern
        });

        var result = new RfidRawLogOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }))
            .Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, failure => failure.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("relative/raw-data")]
    [InlineData("D:/RFID/../RawData")]
    public void Relative_or_traversal_root_paths_are_rejected(string rootPath)
    {
        var options = CreateRawLog();
        options.Sources[0].RootPath = rootPath;

        var result = new RfidRawLogOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }))
            .Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, failure => failure.Contains("RootPath", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../File_*.txt")]
    [InlineData("D:/logs/File_*.txt")]
    [InlineData("File_*.log")]
    public void Unsafe_file_patterns_are_rejected(string filePattern)
    {
        var options = CreateRawLog();
        options.Sources[0].FilePattern = filePattern;

        var result = new RfidRawLogOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }))
            .Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, failure => failure.Contains("FilePattern", StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_worker_skips_runtime_dependency_requirements()
    {
        var worker = new WorkerOptions { Enabled = false };
        var rawLog = new RfidRawLogOptions();
        var mongo = new MongoDbOptions();

        Assert.True(new RfidRawLogOptionsValidator(Options.Create(worker)).Validate(null, rawLog).Succeeded);
        Assert.True(new MongoDbOptionsValidator(Options.Create(worker)).Validate(null, mongo).Succeeded);
    }

    [Fact]
    public void Remote_http_source_configuration_is_valid_without_a_local_root_path()
    {
        var options = CreateRawLog();
        options.Sources[0].Mode = RawLogSourceMode.RemoteHttp;
        options.Sources[0].RootPath = string.Empty;
        options.Sources[0].RemoteBaseUrl = "http://192.168.1.38:8091/logs/RawData/";

        var result = new RfidRawLogOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }))
            .Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("relative/logs")]
    [InlineData("ftp://192.168.1.38/logs/")]
    [InlineData("http://192.168.1.38/logs/?token=secret")]
    public void Remote_http_source_rejects_unsafe_base_urls(string baseUrl)
    {
        var options = CreateRawLog();
        options.Sources[0].Mode = RawLogSourceMode.RemoteHttp;
        options.Sources[0].RootPath = string.Empty;
        options.Sources[0].RemoteBaseUrl = baseUrl;

        var result = new RfidRawLogOptionsValidator(
            Options.Create(new WorkerOptions { Enabled = true, WorkerId = "worker-01" }))
            .Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("RemoteBaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void Redacted_summary_does_not_expose_mongo_connection_string()
    {
        const string connectionString = "mongodb://user:super-secret-password@localhost:27017";
        var summary = new ConfigurationRedactor().CreateSummary(
            new WorkerOptions { Enabled = true, WorkerId = "worker-01" },
            CreateRawLog(),
            new MongoDbOptions { ConnectionString = connectionString });

        Assert.True(summary.MongoConnectionStringConfigured);
        Assert.DoesNotContain(connectionString, summary.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-password", summary.ToString(), StringComparison.Ordinal);
    }

    private static RfidRawLogOptions CreateRawLog() => new()
    {
        Sources =
        [
            new AntennaSourceOptions
            {
                SourceId = "antenna-site-a",
                RootPath = "D:/RFID/RawData",
                CompanyId = 2,
                TimeZoneId = "UTC",
            FilePattern = AppConst.RawLog.DefaultFilePattern
            }
        ]
    };
}
