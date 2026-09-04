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
                ProjectionEventDisposition.Ignored)
        ]);

        Assert.Equal(typeof(byte[]), table.Columns["EventId"]!.DataType);
        Assert.Equal(32, ((byte[])table.Rows[0]["EventId"]).Length);
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
}
