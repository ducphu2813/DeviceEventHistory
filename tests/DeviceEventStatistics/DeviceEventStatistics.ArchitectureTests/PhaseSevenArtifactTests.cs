using System.Xml.Linq;

namespace DeviceEventStatistics.ArchitectureTests;

public sealed class PhaseSevenArtifactTests
{
    [Fact]
    public void Mongo_driver_uses_the_first_version_that_resolves_security_fixed_compression_dependencies()
    {
        var root = FindRepositoryRoot();
        var projectPaths = new[]
        {
            Path.Combine(root, "src", "DeviceEventStatistics", "DeviceEventStatistics.Infrastructure", "DeviceEventStatistics.Infrastructure.csproj"),
            Path.Combine(root, "src", "DeviceEventHistory.Infrastructure", "DeviceEventHistory.Infrastructure.csproj")
        };

        foreach (var projectPath in projectPaths)
        {
            var project = XDocument.Load(projectPath);
            var package = project.Descendants("PackageReference")
                .Single(reference => (string?)reference.Attribute("Include") == "MongoDB.Driver");

            Assert.True(
                Version.Parse((string?)package.Attribute("Version") ?? "0.0.0") >= new Version(3, 9, 0),
                $"MongoDB.Driver in {projectPath} must be at least 3.9.0 to avoid the audited vulnerable transitive packages.");
        }
    }

    [Fact]
    public void Worker_does_not_redeclare_framework_provided_hosting_packages()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "DeviceEventStatistics",
            "DeviceEventStatistics.Worker",
            "DeviceEventStatistics.Worker.csproj");
        var project = XDocument.Load(projectPath);

        var packageNames = project.Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => include is not null)
            .ToArray();

        Assert.DoesNotContain("Microsoft.Extensions.Hosting", packageNames);
        Assert.DoesNotContain("Microsoft.Extensions.Diagnostics.HealthChecks", packageNames);
    }

    [Fact]
    public void Bootstrap_is_the_single_create_contract_and_legacy_follow_up_scripts_are_removed()
    {
        var migrationDirectory = GetMigrationDirectory();
        var migrations = Directory.GetFiles(migrationDirectory, "*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.Contains("009_CreateDeviceEventStatisticsSchema", migrations);
        Assert.Contains("010_CleanupDeviceEventStatisticsSchema", migrations);
        Assert.DoesNotContain("011_AddScopedProcessedEventContract", migrations);
        Assert.DoesNotContain("012_FixMetricRegistryV1", migrations);

        var bootstrap = File.ReadAllText(Path.Combine(migrationDirectory, "009_CreateDeviceEventStatisticsSchema.sql"));
        Assert.DoesNotContain("DROP TABLE", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AuditLastSourceDocumentId", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ProjectionProcessedEventTypeV2", bootstrap, StringComparison.Ordinal);
        Assert.Contains("CompanyId", bootstrap, StringComparison.Ordinal);
        Assert.Contains("DeviceId", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_is_idempotent_and_limited_to_statistics_objects()
    {
        var cleanup = File.ReadAllText(Path.Combine(GetMigrationDirectory(), "010_CleanupDeviceEventStatisticsSchema.sql"));

        Assert.Contains("DROP TABLE IF EXISTS [dbo].[DES.", cleanup, StringComparison.Ordinal);
        Assert.Contains("DROP TYPE IF EXISTS [dbo].", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("USE [", cleanup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HangFire", cleanup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", cleanup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP SCHEMA", cleanup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE IF EXISTS [dbo].[History", cleanup, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMigrationDirectory() => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "DeviceEventStatistics",
        "DeviceEventStatistics.Infrastructure",
        "SqlServer",
        "Migrations");

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
