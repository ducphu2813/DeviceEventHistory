using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.Ingestion;

public static class RawSourceEventIdentityFactory
{
    public static string CreateEventId(RawSourceEvent sourceEvent) =>
        ComputeHash(BuildIdentity(sourceEvent, AppConst.Identity.EventPrefix));

    public static string CreateFailureId(RawSourceEvent sourceEvent) =>
        ComputeHash(BuildIdentity(sourceEvent, AppConst.Identity.FailurePrefix));

    private static string BuildIdentity(RawSourceEvent sourceEvent, string outcome) =>
        string.Join(
            AppConst.Identity.Separator,
            outcome,
            sourceEvent.SourceId,
            sourceEvent.ConnectionGeneration,
            sourceEvent.ReceiveSequence,
            sourceEvent.EventName,
            sourceEvent.PayloadSha256);

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
