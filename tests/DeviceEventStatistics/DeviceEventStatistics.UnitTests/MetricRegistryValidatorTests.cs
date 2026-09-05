using DeviceEventStatistics.Application.Mapping;

namespace DeviceEventStatistics.UnitTests;

public sealed class MetricRegistryValidatorTests
{
    private static readonly MetricRegistryIdentity Identity = new(1, "v1", "v1");

    [Fact]
    public void Resolves_enabled_metrics_for_the_requested_contract()
    {
        var result = MetricRegistryValidator.Validate(
            Identity,
            [Entry("tag_read", 1), Entry("snapshot_observed", 14)],
            ["tag_read", "snapshot_observed"]);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["tag_read"]);
        Assert.Equal(14, result["snapshot_observed"]);
    }

    [Fact]
    public void Rejects_disabled_metrics()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MetricRegistryValidator.Validate(
            Identity,
            [Entry("tag_read", 1, isEnabled: false)],
            ["tag_read"]));

        Assert.StartsWith("STAT-SQL-METRIC-REGISTRY-DISABLED:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_missing_metrics()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MetricRegistryValidator.Validate(
            Identity,
            [],
            ["tag_read"]));

        Assert.StartsWith("STAT-SQL-METRIC-REGISTRY-REQUIRED-MISSING:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_duplicate_logical_rows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MetricRegistryValidator.Validate(
            Identity,
            [Entry("tag_read", 1), Entry("tag_read", 1)],
            ["tag_read"]));

        Assert.StartsWith("STAT-SQL-METRIC-REGISTRY-DUPLICATE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_mapping_or_ownership_version_mismatch()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => MetricRegistryValidator.Validate(
            Identity,
            [Entry("tag_read", 1, mappingVersion: "v2")],
            ["tag_read"]));

        Assert.StartsWith("STAT-SQL-METRIC-REGISTRY-VERSION-MISMATCH:", exception.Message, StringComparison.Ordinal);
    }

    private static MetricRegistryEntry Entry(
        string code,
        int key,
        bool isEnabled = true,
        string mappingVersion = "v1",
        string ownershipVersion = "v1") =>
        new(code, key, isEnabled, mappingVersion, ownershipVersion);
}
