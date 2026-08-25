using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;
using DeviceEventHistory.Infrastructure.RfidRawLog.Framing;
using DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

namespace DeviceEventHistory.UnitTests;

public sealed class RawLogFileDiscoveryTests
{
    [Fact]
    public async Task Local_discovery_returns_numeric_file_ids_from_the_requested_date_folder()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var dateFolder = Path.Combine(root, "2026", "08", "24");
            Directory.CreateDirectory(dateFolder);
            await File.WriteAllTextAsync(Path.Combine(dateFolder, "File_12.txt"), "@(... )e(0)\r\n");
            await File.WriteAllTextAsync(Path.Combine(dateFolder, "File_invalid.txt"), "ignored");

            var source = CreateLocalSource(root);
            var files = await new LocalRawLogFileDiscovery().DiscoverAsync(
                source,
                new DateOnly(2026, 8, 24),
                CancellationToken.None);

            var file = Assert.Single(files);
            Assert.Equal(12, file.FileId);
            Assert.Equal(new DateOnly(2026, 8, 24), file.FolderDate);
            Assert.Equal(Path.Combine(dateFolder, "File_12.txt"), file.Location);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Remote_discovery_reads_file_links_from_the_date_directory_listing()
    {
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("http://antenna.test/logs/RawData/2026/08/24/", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(CreateResponse(
                "<html><a href=\"/logs/RawData/2026/08/24/File_7.txt\">File_7.txt</a><a href=\"File_bad.txt\">bad</a></html>"));
        });
        using var client = new HttpClient(handler);
        var source = CreateRemoteSource("http://antenna.test/logs/RawData/");

        var files = await new RemoteHttpRawLogFileDiscovery(client).DiscoverAsync(
            source,
            new DateOnly(2026, 8, 24),
            CancellationToken.None);

        var file = Assert.Single(files);
        Assert.Equal(7, file.FileId);
        Assert.Equal("http://antenna.test/logs/RawData/2026/08/24/File_7.txt", file.Location);
    }

    [Fact]
    public async Task Local_tail_reader_reads_from_a_byte_offset_and_reports_truncation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var filePath = Path.Combine(root, "File_3.txt");
            await File.WriteAllTextAsync(filePath, "abcđef");
            var source = CreateLocalSource(root);
            Assert.True(RawLogFileDescriptor.TryCreate(
                source,
                new DateOnly(2026, 8, 24),
                "File_3.txt",
                filePath,
                new FileInfo(filePath).Length,
                out var descriptor));

            var reader = new LocalRawLogTailReader();
            var result = await reader.ReadAsync(descriptor!, 3, 16, CancellationToken.None);
            Assert.Equal("đef", Encoding.UTF8.GetString(result.Data));
            Assert.Equal(7, result.NextOffset);
            Assert.False(result.IsTruncated);

            var truncated = await reader.ReadAsync(descriptor!, 99, 16, CancellationToken.None);
            Assert.True(truncated.IsTruncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Remote_tail_reader_uses_http_range_and_preserves_offsets()
    {
        const string content = "0123456789";
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("bytes=4-7", request.Headers.Range!.ToString());
            var response = CreateResponse(content[4..8], HttpStatusCode.PartialContent);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, 7, content.Length);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var source = CreateRemoteSource("http://antenna.test/logs/RawData/");
        Assert.True(RawLogFileDescriptor.TryCreate(
            source,
            new DateOnly(2026, 8, 24),
            "File_9.txt",
            "http://antenna.test/logs/RawData/2026/08/24/File_9.txt",
            null,
            out var descriptor));

        var result = await new RemoteHttpRawLogTailReader(client).ReadAsync(
            descriptor!,
            4,
            4,
            CancellationToken.None);

        Assert.Equal("4567", Encoding.UTF8.GetString(result.Data));
        Assert.Equal(8, result.NextOffset);
        Assert.Equal(10, result.FileLength);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task Remote_tail_reader_uses_the_common_message_when_range_is_ignored()
    {
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(CreateResponse("full content")));
        using var client = new HttpClient(handler);
        var source = CreateRemoteSource("http://antenna.test/logs/RawData/");
        Assert.True(RawLogFileDescriptor.TryCreate(
            source,
            new DateOnly(2026, 8, 24),
            "File_9.txt",
            "http://antenna.test/logs/RawData/2026/08/24/File_9.txt",
            null,
            out var descriptor));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RemoteHttpRawLogTailReader(client).ReadAsync(descriptor!, 4, 4, CancellationToken.None));

        Assert.Equal(
            AppConst.Messages.Format(AppConst.Messages.MSG_REMOTE_RANGE_REQUEST_IGNORED, "File_9.txt"),
            exception.Message);
    }

    [Fact]
    public async Task Discovery_and_tail_reader_use_common_messages_when_an_adapter_is_missing()
    {
        var source = CreateRemoteSource("http://antenna.test/logs/RawData/");
        var discoveryException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RawLogFileDiscovery(
                    new RfidRawLogOptions(),
                    [],
                    TimeProvider.System)
                .DiscoverAsync(source, CancellationToken.None));

        Assert.Equal(
            AppConst.Messages.Format(AppConst.Messages.MSG_NO_RAW_LOG_DISCOVERY_ADAPTER, RawLogSourceMode.RemoteHttp),
            discoveryException.Message);

        Assert.True(RawLogFileDescriptor.TryCreate(
            source,
            new DateOnly(2026, 8, 24),
            "File_9.txt",
            "http://antenna.test/logs/RawData/2026/08/24/File_9.txt",
            null,
            out var descriptor));
        var readerException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RawLogTailReader(16, []).ReadAsync(descriptor!, 0, CancellationToken.None));

        Assert.Equal(
            AppConst.Messages.Format(AppConst.Messages.MSG_NO_RAW_LOG_TAIL_READER, RawLogSourceMode.RemoteHttp),
            readerException.Message);
    }

    [Fact]
    public void Framer_emits_multiple_records_and_keeps_exact_byte_offsets()
    {
        var framer = new RawLogRecordFramer(1024);
        var data = Encoding.UTF8.GetBytes("@(a)đe(0)\r\n@(b)e(0)\npartial");

        var records = framer.Append(data, 10);

        Assert.Equal(2, records.Count);
        Assert.Equal("@(a)đe(0)\r\n", Encoding.UTF8.GetString(records[0].Payload));
        Assert.Equal("@(b)e(0)\n", Encoding.UTF8.GetString(records[1].Payload));
        Assert.Equal(10, records[0].StartOffset);
        Assert.Equal(10 + records[0].Payload.Length, records[0].EndOffsetExclusive);
        Assert.Equal(records[0].EndOffsetExclusive, records[1].StartOffset);
        Assert.Equal(records[1].StartOffset + records[1].Payload.Length, records[1].EndOffsetExclusive);
        Assert.Equal(7, framer.PendingByteCount);
    }

    [Fact]
    public void Framer_supports_partial_utf8_and_terminator_bytes_across_chunks()
    {
        var framer = new RawLogRecordFramer(1024);
        var data = Encoding.UTF8.GetBytes("@(tag=đ)e(0)\r\n");

        var first = framer.Append(data.AsMemory(0, 6), 0);
        var second = framer.Append(data.AsMemory(6, 2), 6);
        var third = framer.Append(data.AsMemory(8), 8);

        Assert.Empty(first);
        Assert.Empty(second);
        var record = Assert.Single(third);
        Assert.Equal(0, record.StartOffset);
        Assert.Equal(data.Length, record.EndOffsetExclusive);
        Assert.Equal("@(tag=đ)e(0)\r\n", Encoding.UTF8.GetString(record.Payload));
    }

    [Fact]
    public void Framer_rejects_records_larger_than_the_configured_limit()
    {
        var framer = new RawLogRecordFramer(5);

        var exception = Assert.Throws<RawLogRecordTooLargeException>(() =>
            framer.Append(Encoding.UTF8.GetBytes("123456"), 0));

        Assert.Equal(
            AppConst.Messages.Format(AppConst.Messages.MSG_RAW_LOG_RECORD_TOO_LARGE, 5),
            exception.Message);
    }

    [Fact]
    public void Framer_uses_the_common_message_when_chunks_are_not_contiguous()
    {
        var framer = new RawLogRecordFramer(1024);
        framer.Append(Encoding.UTF8.GetBytes("partial"), 10);

        var exception = Assert.Throws<ArgumentException>(() =>
            framer.Append(Encoding.UTF8.GetBytes("next"), 100));

        Assert.StartsWith(AppConst.Messages.MSG_RAW_LOG_CHUNK_NOT_CONTIGUOUS, exception.Message);
    }

    private static AntennaSourceOptions CreateLocalSource(string root) => new()
    {
        SourceId = "antenna-site-a",
        Mode = RawLogSourceMode.Local,
        RootPath = root,
        CompanyId = 2,
        TimeZoneId = "UTC",
        FilePattern = "File_*.txt"
    };

    private static AntennaSourceOptions CreateRemoteSource(string baseUrl) => new()
    {
        SourceId = "antenna-site-a",
        Mode = RawLogSourceMode.RemoteHttp,
        RemoteBaseUrl = baseUrl,
        CompanyId = 2,
        TimeZoneId = "UTC",
        FilePattern = "File_*.txt"
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "device-event-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static HttpResponseMessage CreateResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/html")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
