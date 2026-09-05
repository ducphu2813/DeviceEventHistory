using DeviceEventStatistics.Application.Persistence;
using DeviceEventStatistics.Infrastructure.SqlServer.Mapping;

namespace DeviceEventStatistics.UnitTests;

public sealed class ProjectionTvpMapperTests
{
    [Fact]
    public void Maps_event_id_to_binary_sha256_and_preserves_sql_nulls()
    {
        var mapper = new ProjectionTvpMapper();
        var eventId = new string('a', 64);
        var sourcePersistedAt = new DateTimeOffset(2026, 9, 4, 1, 2, 3, TimeSpan.Zero);
        var table = mapper.MapProcessedEvents(
        [
            new ProcessedEventInput(
                eventId,
                "mongo-document-1",
                "erp_apphub",
                sourcePersistedAt,
                null,
                null,
                "v1",
                ProjectionEventDisposition.Ignored,
                2,
                101)
        ]);

        Assert.Equal(typeof(byte[]), table.Columns["EventId"]!.DataType);
        Assert.Equal(32, ((byte[])table.Rows[0]["EventId"]).Length);
        Assert.Equal(typeof(long), table.Columns["CompanyId"]!.DataType);
        Assert.Equal(2L, table.Rows[0]["CompanyId"]);
        Assert.Equal(101L, table.Rows[0]["DeviceId"]);
        Assert.Equal(DBNull.Value, table.Rows[0]["StatisticsDate"]);
        Assert.Equal("ignored", table.Rows[0]["Outcome"]);
        Assert.Equal(sourcePersistedAt.UtcDateTime, table.Rows[0]["SourcePersistedAtUtc"]);
    }

    [Fact]
    public void Rejects_non_lowercase_or_non_sha256_event_identity()
    {
        var mapper = new ProjectionTvpMapper();

        Assert.Throws<FormatException>(() => mapper.MapProcessedEvents(
        [
            new ProcessedEventInput(
                new string('A', 64),
                "mongo-document-1",
                "erp_apphub",
                DateTimeOffset.UtcNow,
                null,
                null,
                "v1",
                ProjectionEventDisposition.Ignored)
        ]));
    }

    [Fact]
    public void Hashes_composite_quality_identity_before_binary_persistence()
    {
        var mapper = new ProjectionTvpMapper();
        var table = mapper.MapQualityContributions(
        [
            new QualityContribution(
                new string('a', 64),
                "source-document|parsed_with_warnings",
                new DateOnly(2026, 9, 4),
                1,
                "raw_log",
                "source-1",
                "parsed_with_warnings",
                DateTimeOffset.UtcNow)
        ]);

        Assert.Equal(typeof(byte[]), table.Columns["QualityIdentity"]!.DataType);
        Assert.Equal(32, ((byte[])table.Rows[0]["QualityIdentity"]).Length);
    }
}
