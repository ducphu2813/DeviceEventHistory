using System.Globalization;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using Microsoft.Extensions.Logging;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

public sealed class RemoteHttpRawLogFileDiscovery(
    HttpClient httpClient,
    ILogger<RemoteHttpRawLogFileDiscovery>? discoveryLogger = null) : IRawLogSourceFileDiscovery
{
    private readonly ILogger<RemoteHttpRawLogFileDiscovery> logger =
        discoveryLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteHttpRawLogFileDiscovery>.Instance;

    public RawLogSourceMode Mode => RawLogSourceMode.RemoteHttp;

    public async Task<IReadOnlyList<RawLogFileDescriptor>> DiscoverAsync(
        AntennaSourceOptions source,
        DateOnly folderDate,
        CancellationToken cancellationToken)
    {
        var dateUri = BuildDateUri(source.RemoteBaseUrl, folderDate);
        using var response = await httpClient.GetAsync(
            dateUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogTrace(
                AppConst.Logging.RemoteDirectoryDiscoveredMessage,
                source.SourceId,
                folderDate,
                0);
            return [];
        }

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var descriptors = new List<RawLogFileDescriptor>();

        foreach (var fileName in RemoteDirectoryListingParser.ExtractFileNames(html, source.FilePattern).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fileUri = new Uri(dateUri, Uri.EscapeDataString(fileName));
            if (RawLogFileDescriptor.TryCreate(source, folderDate, fileName, fileUri.AbsoluteUri, null, out var descriptor) && descriptor is not null)
            {
                descriptors.Add(descriptor);
            }
        }

        var result = descriptors.OrderBy(descriptor => descriptor.FileId).ToArray();
        logger.LogTrace(
            AppConst.Logging.RemoteDirectoryDiscoveredMessage,
            source.SourceId,
            folderDate,
            result.Length);
        return result;
    }

    private static Uri BuildDateUri(string baseUrl, DateOnly folderDate)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/') + "/";
        var relativePath = string.Join(
            "/",
            folderDate.Year.ToString("D4", CultureInfo.InvariantCulture),
            folderDate.Month.ToString("D2", CultureInfo.InvariantCulture),
            folderDate.Day.ToString("D2", CultureInfo.InvariantCulture)) + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), relativePath);
    }
}
