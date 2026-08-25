using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.Parsing;

public static class EventIdentityFactory
{
    public static string CreateEventId(RawRecordContext context) =>
        ComputeHash(BuildIdentity(AppConst.Identity.EventPrefix, context));

    public static string CreateFailureId(RawRecordContext context) =>
        ComputeHash(BuildIdentity(AppConst.Identity.FailurePrefix, context));

    public static string ComputePayloadHash(RawRecordContext context) =>
        Convert.ToHexString(SHA256.HashData(context.RawPayloadBytes)).ToLowerInvariant();

    private static string BuildIdentity(string prefix, RawRecordContext context) =>
        string.Join(
            AppConst.Identity.Separator,
            prefix,
            context.SourceId,
            context.RelativePath,
            context.OffsetStart,
            context.OffsetEnd,
            ComputePayloadHash(context));

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
