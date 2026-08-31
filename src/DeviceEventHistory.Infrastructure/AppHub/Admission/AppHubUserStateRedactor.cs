using System.Security.Cryptography;
using System.Text;
using DeviceEventHistory.Domain.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeviceEventHistory.Infrastructure.AppHub.Admission;

internal static class AppHubUserStateRedactor
{
    public static string Redact(string argumentsJson, string eventName)
    {
        if (!IsUserStateCallback(eventName))
        {
            return argumentsJson;
        }

        var arguments = JArray.Parse(argumentsJson);
        foreach (var token in arguments.OfType<JObject>())
        {
            RedactObject(token);
        }

        return arguments.ToString(Formatting.None);
    }

    private static void RedactObject(JObject payload)
    {
        var connectionId = GetProperty(payload, AppConst.AppHub.UserState.ConnectionId);
        if (connectionId?.Value is JValue { Value: string connectionValue }
            && !string.IsNullOrWhiteSpace(connectionValue))
        {
            SetProperty(
                payload,
                AppConst.AppHub.UserState.ConnectionIdHash,
                Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(connectionValue)))
                    .ToLowerInvariant());
        }

        RemoveProperty(payload, AppConst.AppHub.UserState.ConnectionId);
        foreach (var sensitiveField in AppConst.AppHub.UserState.SensitiveFields)
        {
            RemoveProperty(payload, sensitiveField);
        }
    }

    private static bool IsUserStateCallback(string eventName) =>
        eventName is AppConst.AppHub.Callbacks.ReceiveDeviceScanConnect
            or AppConst.AppHub.Callbacks.ReceiveDeviceScanDisconnect
            or AppConst.AppHub.Callbacks.ReceiveRequestDeviceScanInfoOnline;

    private static JProperty? GetProperty(JObject payload, string propertyName) =>
        payload.Properties().FirstOrDefault(property =>
            string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static void SetProperty(JObject payload, string propertyName, string value)
    {
        var property = GetProperty(payload, propertyName);
        if (property is null)
        {
            payload.Add(propertyName, value);
        }
        else
        {
            property.Value = value;
        }
    }

    private static void RemoveProperty(JObject payload, string propertyName)
    {
        GetProperty(payload, propertyName)?.Remove();
    }
}
