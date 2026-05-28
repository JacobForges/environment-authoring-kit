using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.XR
{
    public static class VitureIntegration
    {
        public static bool IsSdkPresent()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                a.GetName().Name.IndexOf("Viture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                a.GetName().Name.IndexOf("VITURE", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string GetStatusMessage()
        {
            if (IsSdkPresent())
                return "Viture XR SDK detected. Use Neckband/Glasses SDK settings for 6DoF.";

            return "Viture SDK not installed. Glasses work via Android XR OpenXR display path.";
        }

        public static void TryApplyVitureSettings()
        {
            if (!IsSdkPresent())
                return;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name.IndexOf("Viture", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var settingsType = assembly.GetTypes().FirstOrDefault(t => t.Name.Contains("Settings"));
                if (settingsType == null)
                    continue;

                Debug.Log("[Environment Kit] Viture SDK present — configure device settings in Viture developer panel.");
                break;
            }
        }
    }
}
