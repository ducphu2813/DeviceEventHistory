using System.Text.RegularExpressions;
using DeviceEventHistory.Domain.Common;
using DeviceEventHistory.Infrastructure.AppHub.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Configuration;

public sealed class AppHubOptionsValidator(
    IOptions<WorkerOptions> workerOptions,
    IOptions<RfidRawLogOptions> rawLogOptions) : IValidateOptions<AppHubOptions>
{
    private static readonly Regex HubNameRegex = new(
        "^[A-Za-z][A-Za-z0-9_.-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ValidateOptionsResult Validate(string? name, AppHubOptions options)
    {
        if (!workerOptions.Value.Enabled || !options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var sources = options.Sources ?? [];
        if (sources.Count == 0)
        {
            failures.Add(AppConst.Messages.MSG_APPHUB_SOURCES_REQUIRED);
        }

        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawSource in rawLogOptions.Value.Sources ?? [])
        {
            var rawSourceId = rawSource.SourceId.Trim();
            if (rawSourceId.Length > 0)
            {
                sourceIds.Add(rawSourceId);
            }
        }

        for (var index = 0; index < sources.Count; index++)
        {
            ValidateSource(sources[index], index, sourceIds, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSource(
        AppHubSourceOptions source,
        int index,
        ISet<string> sourceIds,
        ICollection<string> failures)
    {
        var prefix = $"Sources[{index}]";
        var sourceId = source.SourceId.Trim();

        if (sourceId.Length == 0)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_SOURCE_ID_REQUIRED,
                prefix));
        }
        else if (!sourceIds.Add(sourceId))
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_SOURCE_ID_DUPLICATED,
                prefix,
                sourceId));
        }

        ValidateEndpoint(source.Endpoint, prefix, failures);
        ValidateHubName(source.HubName, prefix, failures);

        if (source.CompanyId is <= 0)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_COMPANY_ID_POSITIVE,
                prefix));
        }

        if (source.DedicatedSingleTenant && source.CompanyId is not > 0)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_DEDICATED_COMPANY_REQUIRED,
                prefix));
        }

        if (source.ChannelCapacity <= 0)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_CHANNEL_CAPACITY_POSITIVE,
                prefix));
        }

        if (source.EnqueueTimeout <= TimeSpan.Zero)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_ENQUEUE_TIMEOUT_POSITIVE,
                prefix));
        }

        if (source.ReconnectMinDelay <= TimeSpan.Zero)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_RECONNECT_MIN_DELAY_POSITIVE,
                prefix));
        }

        if (source.ReconnectMaxDelay <= TimeSpan.Zero)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_RECONNECT_MAX_DELAY_POSITIVE,
                prefix));
        }

        if (source.ReconnectMinDelay > source.ReconnectMaxDelay)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_RECONNECT_DELAY_RANGE_INVALID,
                prefix));
        }

        ValidateCallbacks(source.EnabledEvents, prefix, failures);
        ValidateCredentialConfiguration(source, prefix, failures);
    }

    private static void ValidateEndpoint(
        string endpoint,
        string prefix,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_ENDPOINT_REQUIRED,
                prefix));
            return;
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_ENDPOINT_INVALID,
                prefix));
        }
    }

    private static void ValidateHubName(
        string hubName,
        string prefix,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(hubName))
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_HUB_NAME_REQUIRED,
                prefix));
        }
        else if (!HubNameRegex.IsMatch(hubName.Trim()))
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_HUB_NAME_INVALID,
                prefix));
        }
    }

    private static void ValidateCallbacks(
        IEnumerable<string> callbacks,
        string prefix,
        ICollection<string> failures)
    {
        var callbackSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var callback in callbacks ?? [])
        {
            var value = callback.Trim();
            if (!AppConst.AppHub.Callbacks.Registered.Contains(value))
            {
                failures.Add(AppConst.Messages.Format(
                    AppConst.Messages.MSG_APPHUB_EVENT_UNSUPPORTED,
                    prefix,
                    callback));
            }
            else if (!callbackSet.Add(value))
            {
                failures.Add(AppConst.Messages.Format(
                    AppConst.Messages.MSG_APPHUB_EVENT_DUPLICATED,
                    prefix,
                    value));
            }
        }

        if (callbackSet.Count == 0)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_ENABLED_EVENTS_REQUIRED,
                prefix));
        }
    }

    private static void ValidateCredentialConfiguration(
        AppHubSourceOptions source,
        string prefix,
        ICollection<string> failures)
    {
        var hasAccessToken = !string.IsNullOrWhiteSpace(source.AccessToken)
            || !string.IsNullOrWhiteSpace(source.AccessTokenEnvironmentVariable);
        var hasJwtToken = !string.IsNullOrWhiteSpace(source.TokenJwt)
            || !string.IsNullOrWhiteSpace(source.TokenJwtEnvironmentVariable);
        if (!hasAccessToken && !hasJwtToken)
        {
            failures.Add(AppConst.Messages.Format(
                AppConst.Messages.MSG_APPHUB_CREDENTIAL_ENVIRONMENT_VARIABLE_REQUIRED,
                prefix));
        }
    }
}
