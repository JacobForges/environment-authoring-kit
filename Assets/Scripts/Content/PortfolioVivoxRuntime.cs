using System;
using System.IO;
using UnityEngine;

namespace Hub
{
    /// <summary>Detect whether Vivox dashboard credentials are present locally (no Vivox API calls).</summary>
    public static class PortfolioVivoxRuntime
    {
        const string SettingsRelative = "ProjectSettings/Packages/com.unity.services.vivox/Settings.json";

        public static bool HasProjectCredentials()
        {
            var path = ResolveSettingsPath();
            if (!File.Exists(path))
                return false;

            try
            {
                var text = File.ReadAllText(path);
                return TryGetSettingValue(text, "server", out var server)
                       && !string.IsNullOrWhiteSpace(server)
                       && TryGetSettingValue(text, "domain", out var domain)
                       && !string.IsNullOrWhiteSpace(domain)
                       && TryGetSettingValue(text, "tokenIssuer", out var issuer)
                       && !string.IsNullOrWhiteSpace(issuer);
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetSettingValue(string settingsJson, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(settingsJson))
                return false;

            var keyNeedle = "\"key\": \"" + key + "\"";
            var keyIndex = settingsJson.IndexOf(keyNeedle, StringComparison.Ordinal);
            if (keyIndex < 0)
                return false;

            const string valueNeedle = "\"value\": \"";
            var valueIndex = settingsJson.IndexOf(valueNeedle, keyIndex, StringComparison.Ordinal);
            if (valueIndex < 0)
                return false;

            valueIndex += valueNeedle.Length;
            var endIndex = settingsJson.IndexOf("\"", valueIndex, StringComparison.Ordinal);
            if (endIndex < 0)
                return false;

            var escaped = settingsJson.Substring(valueIndex, endIndex - valueIndex);
            var unescaped = escaped.Replace("\\\"", "\"", StringComparison.Ordinal);
            const string mValueNeedle = "\"m_Value\":\"";
            var mvIndex = unescaped.IndexOf(mValueNeedle, StringComparison.Ordinal);
            if (mvIndex < 0)
                return false;

            mvIndex += mValueNeedle.Length;
            var mvEnd = unescaped.IndexOf("\"", mvIndex, StringComparison.Ordinal);
            if (mvEnd < 0)
                return false;

            value = unescaped.Substring(mvIndex, mvEnd - mvIndex);
            return true;
        }

        public static string ResolveSettingsPath() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", SettingsRelative));
    }
}
