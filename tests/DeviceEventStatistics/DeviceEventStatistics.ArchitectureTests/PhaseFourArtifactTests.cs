namespace DeviceEventStatistics.ArchitectureTests;

public sealed class PhaseFourArtifactTests
{
    [Fact]
    public void Scoped_processed_event_contract_is_versioned_and_verified()
    {
        var root = FindRepositoryRoot();
        var migrationPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Migrations",
            "011_AddScopedProcessedEventContract.sql");
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("ProjectionProcessedEventTypeV2", migration, StringComparison.Ordinal);
        Assert.Contains("CompanyId", migration, StringComparison.Ordinal);
        Assert.Contains("DeviceId", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP", migration, StringComparison.OrdinalIgnoreCase);

        var verifierPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Schema",
            "SqlSchemaVerifier.cs");
        var verifier = File.ReadAllText(verifierPath);
        Assert.Contains("011_AddScopedProcessedEventContract", verifier, StringComparison.Ordinal);
        Assert.Contains("ProjectionProcessedEventTypeV2", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_scope_is_pushed_to_mongo_and_sql()
    {
        var root = FindRepositoryRoot();
        var queryPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "MongoDb",
            "Reading",
            "MongoHistoryQuery.cs");
        var query = File.ReadAllText(queryPath);
        Assert.Contains("companyId", query, StringComparison.Ordinal);
        Assert.Contains("device.id", query, StringComparison.Ordinal);
        Assert.Contains("timelineAtUtc", query, StringComparison.Ordinal);

        var rebuildPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Stores",
            "SqlProjectionRebuildStore.cs");
        var rebuild = File.ReadAllText(rebuildPath);
        Assert.Contains("[CompanyId] = @companyId", rebuild, StringComparison.Ordinal);
        Assert.Contains("[DeviceId] = @deviceId", rebuild, StringComparison.Ordinal);
        Assert.Contains("COALESCE(existing.[CompanyId]", rebuild, StringComparison.Ordinal);
        Assert.Contains("COALESCE(existing.[DeviceId]", rebuild, StringComparison.Ordinal);
        Assert.Contains("ProjectionProcessedEventTypeV2", rebuild, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM {Table(\"IngestionQualityDaily\")}", rebuild, StringComparison.Ordinal);
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
