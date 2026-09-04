using System.Xml.Linq;

namespace DeviceEventStatistics.ArchitectureTests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Project_references_follow_the_clean_architecture_direction()
    {
        AssertProjectReferences(
            Project("DeviceEventStatistics.Application"),
            "DeviceEventStatistics.Domain");
        AssertProjectReferences(
            Project("DeviceEventStatistics.Infrastructure"),
            "DeviceEventStatistics.Application",
            "DeviceEventStatistics.Domain");
        AssertProjectReferences(
            Project("DeviceEventStatistics.Worker"),
            "DeviceEventStatistics.Application",
            "DeviceEventStatistics.Infrastructure");
    }

    [Fact]
    public void Domain_and_application_do_not_reference_infrastructure_or_external_database_packages()
    {
        AssertDoesNotReference(
            Project("DeviceEventStatistics.Domain"),
            "DeviceEventStatistics.Infrastructure",
            "MongoDB.Driver",
            "MongoDB.Bson",
            "Microsoft.Data.SqlClient");
        AssertDoesNotReference(
            Project("DeviceEventStatistics.Application"),
            "DeviceEventStatistics.Infrastructure",
            "MongoDB.Driver",
            "MongoDB.Bson",
            "Microsoft.Data.SqlClient");
    }

    [Fact]
    public void Statistics_projects_do_not_reference_history_or_legacy_projects()
    {
        foreach (var projectName in new[]
                 {
                     "DeviceEventStatistics.Domain",
                     "DeviceEventStatistics.Application",
                     "DeviceEventStatistics.Infrastructure",
                     "DeviceEventStatistics.Worker"
                 })
        {
            AssertDoesNotReference(
                Project(projectName),
                "DeviceEventHistory.Worker",
                "DeviceEventHistory.Infrastructure",
                "ERP",
                "RFID");
        }
    }

    private static void AssertProjectReferences(string projectPath, params string[] expectedProjects)
    {
        var references = ReadProjectReferences(projectPath);
        foreach (var expectedProject in expectedProjects)
        {
            Assert.Contains(expectedProject, references, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void AssertDoesNotReference(string projectPath, params string[] forbiddenNames)
    {
        var references = ReadAllDependencyNames(projectPath);
        foreach (var forbiddenName in forbiddenNames)
        {
            Assert.DoesNotContain(references, name =>
                name.Contains(forbiddenName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static IReadOnlyCollection<string> ReadProjectReferences(string projectPath) =>
        LoadProject(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")?.Value ?? string.Empty))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

    private static IReadOnlyCollection<string> ReadAllDependencyNames(string projectPath)
    {
        var document = LoadProject(projectPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }

    private static XDocument LoadProject(string projectPath) => XDocument.Load(projectPath);

    private static string Project(string projectName)
    {
        var root = FindRepositoryRoot();
        return Path.Combine(
            root,
            "src",
            "DeviceEventStatistics",
            projectName,
            $"{projectName}.csproj");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeviceEventStatistics.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for architecture tests.");
    }
}
