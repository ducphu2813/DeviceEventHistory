using System.Text.RegularExpressions;

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

        Assert.Equal(12, migrations.Length);
        Assert.Equal("001_CreateStatisticsSchema", migrations[0]);
        Assert.Equal("012_FixMetricRegistryV1", migrations[^1]);
        Assert.Contains("Checksum", File.ReadAllText(Path.Combine(root, "deploy", "device-event-statistics", "Apply-SqlMigrations.ps1")));
    }

    [Fact]
    public void SQL_migrations_keep_statistics_objects_in_the_configured_dbo_schema()
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
            if (!Path.GetFileName(file).StartsWith("009_", StringComparison.Ordinal))
            {
                Assert.Contains("__SCHEMA__", sql);
            }

            Assert.DoesNotContain("HangFire", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Standalone_statistics_bootstrap_does_not_select_or_modify_a_database()
    {
        var root = FindRepositoryRoot();
        var migrationPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Migrations",
            "009_CreateDeviceEventStatisticsSchema.sql");
        var sql = File.ReadAllText(migrationPath);

        Assert.Contains("[dbo].[DES.DeviceDailySnapshot]", sql);
        Assert.DoesNotContain("USE [", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sp_rename", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standalone_statistics_bootstrap_follows_database_table_conventions()
    {
        var root = FindRepositoryRoot();
        var migrationPath = Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Infrastructure",
            "SqlServer",
            "Migrations",
            "009_CreateDeviceEventStatisticsSchema.sql");
        var sql = File.ReadAllText(migrationPath);

        var tableCount = Regex.Matches(sql, @"\bCREATE TABLE \[dbo\]\.\[DES\.").Count;
        var identityPrimaryKeyCount = Regex.Matches(
            sql,
            @"\[\w+Id\] INT IDENTITY\(1, 1\) PRIMARY KEY").Count;

        Assert.Equal(tableCount, identityPrimaryKeyCount);
        Assert.DoesNotContain("NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE INDEX", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOREIGN KEY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REFERENCES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONSTRAINT [PK_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ProjectionRunId] INT IDENTITY(1, 1) PRIMARY KEY", sql);
        Assert.Contains("[RunId] uniqueidentifier NULL", sql);
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
