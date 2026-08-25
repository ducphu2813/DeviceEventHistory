using System.Net;
using System.Text.RegularExpressions;

using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Infrastructure.RfidRawLog.Discovery;

internal static partial class RemoteDirectoryListingParser
{
    [GeneratedRegex("<a\\s+[^>]*href\\s*=\\s*[\\\"'](?<href>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorRegex();

    public static IEnumerable<string> ExtractFileNames(string html, string filePattern)
    {
        var filePatternRegex = CreateFilePatternRegex(filePattern);
        foreach (Match match in AnchorRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (string.IsNullOrWhiteSpace(href) || href.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = ExtractSinglePathSegment(href);
            if (fileName is not null && filePatternRegex.IsMatch(fileName))
            {
                yield return fileName;
            }
        }
    }

    private static string? ExtractSinglePathSegment(string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
        {
            if (!string.IsNullOrEmpty(absoluteUri.Query) || !string.IsNullOrEmpty(absoluteUri.Fragment))
            {
                return null;
            }

            href = absoluteUri.AbsolutePath;
        }

        href = href.Split('?', '#')[0].TrimEnd('/');
        if (href.Contains('\\'))
        {
            return null;
        }

        var lastSlashIndex = href.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            href = href[(lastSlashIndex + 1)..];
        }

        if (href.Length == 0)
        {
            return null;
        }

        try
        {
            return Uri.UnescapeDataString(href);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static Regex CreateFilePatternRegex(string pattern)
    {
        var expression = Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        return new Regex($"^{expression}$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
