namespace DeviceEventStatistics.ArchitectureTests;

public sealed class PhaseSixArtifactTests
{
    [Fact]
    public void Health_endpoints_are_exposed_with_separate_live_and_ready_contracts()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Worker",
            "Program.cs"));

        Assert.Contains("/health/live", program, StringComparison.Ordinal);
        Assert.Contains("/health/ready", program, StringComparison.Ordinal);
        Assert.Contains("Tags.Contains(\"live\"", program, StringComparison.Ordinal);
        Assert.Contains("Tags.Contains(\"ready\"", program, StringComparison.Ordinal);

        var writer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Worker",
            "HealthChecks",
            "HealthEndpointResponseWriter.cs"));
        Assert.DoesNotContain("Exception", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionString", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_and_time_contracts_are_deterministic()
    {
        var root = FindRepositoryRoot();
        var snapshot = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Application",
            "Observability",
            "StatisticsObservabilityContracts.cs"));
        var sweep = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Application",
            "Projection",
            "ProjectionSweep.cs"));
        var mapper = File.ReadAllText(Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Application",
            "Mapping",
            "ProjectionEventOutcomeMapper.cs"));

        Assert.Contains("OldestPendingRequiredFromAtUtc", snapshot, StringComparison.Ordinal);
        Assert.Contains("RetentionBoundaryAtUtc", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", sweep, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", mapper, StringComparison.Ordinal);
        Assert.Contains("new PeriodicTimer", File.ReadAllText(Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Worker",
            "Orchestration",
            "OperationalHealthHostedService.cs")), StringComparison.Ordinal);
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
