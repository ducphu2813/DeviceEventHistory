using System.Globalization;

using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

public sealed class LocalRawLogFileDiscovery : IRawLogSourceFileDiscovery
{
    public RawLogSourceMode Mode => RawLogSourceMode.Local;

    public Task<IReadOnlyList<RawLogFileDescriptor>> DiscoverAsync(
        AntennaSourceOptions source,
        DateOnly folderDate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var datePath = Path.Combine(
            source.RootPath,
            folderDate.Year.ToString("D4", CultureInfo.InvariantCulture),
            folderDate.Month.ToString("D2", CultureInfo.InvariantCulture),
            folderDate.Day.ToString("D2", CultureInfo.InvariantCulture));

        if (!Directory.Exists(datePath))
        {
            return Task.FromResult<IReadOnlyList<RawLogFileDescriptor>>([]);
        }

        var descriptors = new List<RawLogFileDescriptor>();
        foreach (var filePath in Directory.EnumerateFiles(datePath, source.FilePattern, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var length = TryGetLength(filePath);
            if (RawLogFileDescriptor.TryCreate(source, folderDate, fileName, filePath, length, out var descriptor) && descriptor is not null)
            {
                descriptors.Add(descriptor);
            }
        }

        return Task.FromResult<IReadOnlyList<RawLogFileDescriptor>>(
            descriptors.OrderBy(descriptor => descriptor.FileId).ToArray());
    }

    private static long? TryGetLength(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
