namespace DeviceEventStatistics.ArchitectureTests;

public sealed class PhaseThreeArtifactTests
{
    [Fact]
    public void Contains_phase_three_reader_mapping_and_index_artifacts()
    {
        var root = FindRepositoryRoot();
        var statisticsRoot = Path.Combine(root, "src", "DeviceEventStatistics");

        Assert.True(File.Exists(Path.Combine(statisticsRoot, "DeviceEventStatistics.Application", "History", "HistoryEvent.cs")));
        Assert.True(File.Exists(Path.Combine(statisticsRoot, "DeviceEventStatistics.Application", "Mapping", "DeviceMetricMapperRegistry.cs")));
        Assert.True(File.Exists(Path.Combine(statisticsRoot, "DeviceEventStatistics.Infrastructure", "MongoDb", "Reading", "MongoHistoryEventReader.cs")));
        Assert.True(File.Exists(Path.Combine(statisticsRoot, "DeviceEventStatistics.Infrastructure", "MongoDb", "Indexes", "MongoHistoryIndexVerifier.cs")));
        Assert.True(File.Exists(Path.Combine(root, "deploy", "device-event-statistics", "Ensure-MongoHistoryStatisticsIndex.ps1")));
    }

    [Fact]
    public void Mongo_projection_does_not_fetch_raw_payload()
    {
        var root = FindRepositoryRoot();
        var projectionPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "MongoDb",
            "Reading",
            "MongoHistoryFieldProjection.cs");

        var source = File.ReadAllText(projectionPath);
        Assert.DoesNotContain("rawPayload", source, StringComparison.Ordinal);
        Assert.Contains("persistedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("eventId", source, StringComparison.Ordinal);
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
