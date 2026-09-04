using DeviceEventStatistics.Infrastructure.Configuration;
using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.UnitTests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Disabled_worker_allows_external_dependencies_to_be_configured_later()
    {
        var worker = Options.Create(new WorkerOptions { Enabled = false });
        var result = new DatabaseSettingsOptionsValidator(worker).Validate(
            Options.DefaultName,
            new DatabaseSettingsOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_worker_requires_coverage_start_for_new_projection_definition()
    {
        var worker = Options.Create(new WorkerOptions { Enabled = true });
        var options = new ProjectionOptions { CoverageStartAtUtc = null };

        var result = new ProjectionOptionsValidator(worker).Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("STAT-CONFIG-COVERAGE-START-REQUIRED", result.FailureMessage);
    }

    [Fact]
    public void Enabled_worker_accepts_resume_from_stored_projection_definition()
    {
        var worker = Options.Create(new WorkerOptions { Enabled = true });
        var options = new ProjectionOptions
        {
            ResumeFromStoredDefinition = true,
            CoverageStartAtUtc = null
        };

        var result = new ProjectionOptionsValidator(worker).Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Retention_must_leave_history_headroom()
    {
        var worker = Options.Create(new WorkerOptions { Enabled = true });
        var options = new RetentionOptions
        {
            MongoHistoryRetentionDays = 2,
            MinimumHistoryHeadroomDays = 2
        };

        var result = new RetentionOptionsValidator(worker).Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("STAT-CONFIG-RETENTION-HEADROOM-INVALID", result.FailureMessage);
    }
}
