namespace DeviceEventStatistics.ArchitectureTests;

public sealed class PhaseTwoArtifactTests
{
    [Fact]
    public void Contains_ordered_and_checksum_tracked_sql_migrations()
    {
        var root = FindRepositoryRoot();
        var migrationDirectory = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Migrations");
        var migrations = Directory.GetFiles(migrationDirectory, "*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(5, migrations.Length);
        Assert.Equal("001_CreateStatisticsSchema", migrations[0]);
        Assert.Equal("005_SeedMetricSetV1", migrations[^1]);
        Assert.Contains("Checksum", File.ReadAllText(Path.Combine(root, "deploy", "device-event-statistics", "Apply-SqlMigrations.ps1")));
    }

    [Fact]
    public void SQL_migrations_keep_statistics_objects_in_the_dedicated_schema()
    {
        var root = FindRepositoryRoot();
        var migrationDirectory = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Migrations");

        foreach (var file in Directory.GetFiles(migrationDirectory, "*.sql"))
        {
            var sql = File.ReadAllText(file);
            Assert.Contains("__SCHEMA__", sql);
            Assert.DoesNotContain("HangFire", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        }
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
