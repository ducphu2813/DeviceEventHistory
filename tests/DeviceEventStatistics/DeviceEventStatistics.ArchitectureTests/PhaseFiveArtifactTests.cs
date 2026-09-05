namespace DeviceEventStatistics.ArchitectureTests;

public sealed class PhaseFiveArtifactTests
{
    [Fact]
    public void Metric_registry_contract_is_versioned_and_seeded_with_only_mapper_metrics_active()
    {
        var root = FindRepositoryRoot();
        var migrationDirectory = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Migrations");
        var bootstrap = File.ReadAllText(
            Path.Combine(migrationDirectory, "009_CreateDeviceEventStatisticsSchema.sql"));
        var upgrade = File.ReadAllText(Path.Combine(migrationDirectory, "012_FixMetricRegistryV1.sql"));

        Assert.Contains("'tag_read'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("'device_error'", bootstrap, StringComparison.Ordinal);
        Assert.Contains("'device_error', 'Device error', 'error', 'count', 'error', 'erp_apphub', 0, 0", bootstrap, StringComparison.Ordinal);
        Assert.Contains("'snapshot_observed', 'Snapshot observed', 'connection', 'count', 'snapshot', 'erp_apphub', 0, 1", bootstrap, StringComparison.Ordinal);
        Assert.Contains("JOIN", upgrade, StringComparison.Ordinal);
        Assert.Contains("[MetricKey]", upgrade, StringComparison.Ordinal);
        Assert.Contains("[MetricSetVersion]", upgrade, StringComparison.Ordinal);
        Assert.Contains("[MetricCode]", upgrade, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", upgrade, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", upgrade, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_verifies_the_immutable_metric_registry_before_readiness()
    {
        var root = FindRepositoryRoot();
        var startupPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Worker",
            "HostedServices",
            "StartupInitializationHostedService.cs");
        var startup = File.ReadAllText(startupPath);

        Assert.Contains("ResolveRegistryAsync", startup, StringComparison.Ordinal);
        Assert.Contains("RequiredMetricCodes", startup, StringComparison.Ordinal);
        Assert.Contains("MarkReady", startup, StringComparison.Ordinal);
        Assert.True(
            startup.IndexOf("ResolveRegistryAsync", StringComparison.Ordinal) <
            startup.IndexOf("MarkReady", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeviceEventStatistics.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
