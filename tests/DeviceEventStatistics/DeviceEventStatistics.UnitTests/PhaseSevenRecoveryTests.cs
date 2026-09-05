using DeviceEventStatistics.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventStatistics.UnitTests;

public sealed class PhaseSevenRecoveryTests
{
    [Theory]
    [InlineData(ProjectionMode.Bootstrap)]
    [InlineData(ProjectionMode.Backfill)]
    [InlineData(ProjectionMode.Rebuild)]
    public void Manual_modes_require_an_explicit_scope(ProjectionMode mode)
    {
        var worker = Options.Create(new WorkerOptions { Enabled = true });
        var options = new ProjectionOptions
        {
            Mode = mode,
            CoverageStartAtUtc = DateTimeOffset.UtcNow,
            ManualRange = new ManualRangeOptions
            {
                FromUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ToUtc = DateTimeOffset.UtcNow
            }
        };

        var result = new ProjectionOptionsValidator(worker).Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("STAT-CONFIG-MANUAL-SCOPE-REQUIRED", result.FailureMessage);
    }

    [Fact]
    public void Projection_run_retention_must_be_positive()
    {
        var worker = Options.Create(new WorkerOptions { Enabled = true });
        var options = new RetentionOptions { ProjectionRunRetentionDays = 0 };

        var result = new RetentionOptionsValidator(worker).Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("STAT-CONFIG-PROJECTION-RUN-RETENTION-POSITIVE", result.FailureMessage);
    }
}
