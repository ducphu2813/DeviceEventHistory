using System.Globalization;
using System.Text.Json;
using DeviceEventHistory.Domain.Common;

namespace DeviceEventHistory.Application.AppHub.Mapping;

public sealed class AppHubTenantResolver(
    IAppHubSourceConfigurationProvider sourceConfigurationProvider)
{
    public AppHubTenantResolution Resolve(
        string sourceId,
        JsonElement? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(sourceConfigurationProvider);

        var sourceOptions = sourceConfigurationProvider.TryGet(
                sourceId,
                out var configuredOptions)
            ? configuredOptions
            : new AppHubSourceMappingOptions(sourceId.Trim(), null, false);

        var payloadCompanyId = TryReadCompanyId(payload, out var propertyPresent);
        if (propertyPresent && payloadCompanyId is null)
        {
            return AppHubTenantResolution.Unresolved;
        }

        if (payloadCompanyId is int resolvedPayloadCompanyId)
        {
            if (sourceOptions.CompanyId is int configuredCompanyId
                && configuredCompanyId != resolvedPayloadCompanyId)
            {
                return AppHubTenantResolution.Mismatch(
                    resolvedPayloadCompanyId,
                    configuredCompanyId);
            }

            return AppHubTenantResolution.Resolved(resolvedPayloadCompanyId);
        }

        if (sourceOptions.DedicatedSingleTenant
            && sourceOptions.CompanyId is int dedicatedCompanyId
            && dedicatedCompanyId > 0)
        {
            return AppHubTenantResolution.Resolved(dedicatedCompanyId);
        }

        return AppHubTenantResolution.Unresolved;
    }

    private static int? TryReadCompanyId(
        JsonElement? payload,
        out bool propertyPresent)
    {
        propertyPresent = false;
        if (payload is not JsonElement objectPayload
            || objectPayload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in objectPayload.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    AppConst.AppHub.UserState.CompanyId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            propertyPresent = true;
            return ReadPositiveInt32(property.Value);
        }

        return null;
    }

    private static int? ReadPositiveInt32(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var numericValue))
        {
            return numericValue > 0 ? numericValue : null;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var stringValue))
        {
            return stringValue > 0 ? stringValue : null;
        }

        return null;
    }
}

public sealed record AppHubTenantResolution(
    int? CompanyId,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsResolved => CompanyId is > 0;

    public static AppHubTenantResolution Resolved(int companyId) =>
        new(companyId, null, null);

    public static AppHubTenantResolution Mismatch(
        int payloadCompanyId,
        int configuredCompanyId) =>
        new(
            null,
            AppConst.Parsing.TenantMismatch,
            AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_TENANT_MISMATCH,
                payloadCompanyId,
                configuredCompanyId));

    public static AppHubTenantResolution Unresolved { get; } = new(
        null,
        AppConst.Parsing.TenantUnresolved,
        AppConst.Messages.MSG_APPHUB_TENANT_UNRESOLVED);
}
