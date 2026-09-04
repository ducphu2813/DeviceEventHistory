using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;

namespace DeviceEventHistory.Infrastructure.AppHub.Transport;

internal static class AppHubSignalRQueryFactory
{
    public static IDictionary<string, string> Create(AppHubSourceOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var accessToken = GetConfiguredValue(
            source.AccessToken,
            source.AccessTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return CreateCredentialQuery(
                GetCredentialQueryKey(accessToken),
                accessToken);
        }

        var jwtToken = GetConfiguredValue(
            source.TokenJwt,
            source.TokenJwtEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(jwtToken))
        {
            return CreateCredentialQuery(
                AppConst.AppHub.JwtTokenQueryKey,
                jwtToken);
        }

        throw new InvalidOperationException(AppConst.Messages.MSG_APPHUB_CREDENTIAL_VALUE_REQUIRED);
    }

    private static IDictionary<string, string> CreateCredentialQuery(
        string credentialQueryKey,
        string credentialValue) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [credentialQueryKey] = credentialValue,
            [AppConst.AppHub.SessionTypeQueryKey] = AppConst.AppHub.AccountSessionTypeValue
        };

    private static string GetCredentialQueryKey(string token) =>
        IsJwtFormat(token)
            ? AppConst.AppHub.JwtTokenQueryKey
            : AppConst.AppHub.AccessTokenQueryKey;

    private static bool IsJwtFormat(string token)
    {
        var segments = token.Split('.');
        return segments.Length == 3 && segments.All(segment => segment.Length > 0);
    }

    private static string? GetConfiguredValue(
        string? directValue,
        string? environmentVariableName)
    {
        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        return string.IsNullOrWhiteSpace(environmentVariableName)
            ? null
            : Environment.GetEnvironmentVariable(environmentVariableName.Trim());
    }
}
