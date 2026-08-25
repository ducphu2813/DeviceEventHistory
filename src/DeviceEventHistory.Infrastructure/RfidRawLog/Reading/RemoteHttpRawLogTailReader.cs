using System.Net;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Reading;

public sealed class RemoteHttpRawLogTailReader(HttpClient httpClient) : IRawLogSourceTailReader
{
    public RawLogSourceMode Mode => RawLogSourceMode.RemoteHttp;

    public async Task<RawLogTailReadResult> ReadAsync(
        RawLogFileDescriptor file,
        long offset,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Location);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + maxBytes - 1);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return CreateRangeNotSatisfiableResult(offset, response);
        }

        if (offset > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                AppConst.Messages.Format(AppConst.Messages.MSG_REMOTE_RANGE_REQUEST_IGNORED, file.FileName));
        }

        response.EnsureSuccessStatusCode();
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var data = await ReadAtMostAsync(contentStream, maxBytes, cancellationToken);
        var fileLength = response.Content.Headers.ContentRange?.Length ??
                         response.Content.Headers.ContentLength ??
                         offset + data.Length;

        return new RawLogTailReadResult
        {
            StartOffset = offset,
            NextOffset = offset + data.Length,
            FileLength = fileLength,
            Data = data,
            IsTruncated = false
        };
    }

    private static RawLogTailReadResult CreateRangeNotSatisfiableResult(
        long offset,
        HttpResponseMessage response)
    {
        var fileLength = response.Content.Headers.ContentRange?.Length;
        if (fileLength.HasValue && offset <= fileLength.Value)
        {
            return new RawLogTailReadResult
            {
                StartOffset = offset,
                NextOffset = offset,
                FileLength = fileLength.Value,
                Data = [],
                IsTruncated = false
            };
        }

        return new RawLogTailReadResult
        {
            StartOffset = offset,
            NextOffset = offset,
            FileLength = fileLength ?? 0,
            Data = [],
            IsTruncated = true
        };
    }

    private static async Task<byte[]> ReadAtMostAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[maxBytes];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        if (totalRead != buffer.Length)
        {
            Array.Resize(ref buffer, totalRead);
        }

        return buffer;
    }
}
