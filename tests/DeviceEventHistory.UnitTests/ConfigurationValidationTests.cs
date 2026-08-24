using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.UnitTests;

public sealed class ConfigurationValidationTests
{
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
            FilePattern = "File_*.txt"
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
                FilePattern = "File_*.txt"
            }
        ]
    };
}
