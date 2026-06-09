#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Heavy Environment Kit data (captures, recap assets, live status) prefers a writable external volume;
    /// falls back to Hub/Library/EnvironmentKit on the Mac disk when no external drive is mounted.
    /// </summary>
    public static class EnvironmentKitDataRoot
    {
        public const string PrefDataRoot = "EnvironmentKit_DataRoot";
        public const string ExternalBundleFolderName = "EnvironmentKit-Hub";
        public const string EnvVarName = "ENVIRONMENT_KIT_DATA_ROOT";

        const string InternalRelative = "Library/EnvironmentKit";

        public static string ResolveRoot()
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                var envPath = Path.GetFullPath(fromEnv.Trim());
                EnsureDirectory(envPath);
                return envPath;
            }

            var pref = EditorPrefs.GetString(PrefDataRoot, string.Empty).Trim();
            if (!string.IsNullOrEmpty(pref))
            {
                var prefPath = Path.GetFullPath(pref);
                if (Directory.Exists(prefPath))
                    return prefPath;
            }

            var external = TryResolveExternalRoot();
            if (!string.IsNullOrEmpty(external))
            {
                EditorPrefs.SetString(PrefDataRoot, external);
                return external;
            }

            return ResolveInternalRoot();
        }

        public static string ResolvePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return ResolveRoot();
            return Path.Combine(ResolveRoot(), relativePath.Trim().TrimStart('/', '\\'));
        }

        public static string ResolveInternalRoot()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, InternalRelative);
            EnsureDirectory(path);
            return path;
        }

        public static bool IsUsingExternalVolume()
        {
            var root = ResolveRoot();
            return root.StartsWith("/Volumes/", StringComparison.Ordinal);
        }

        public static string DescribeStorage()
        {
            var root = ResolveRoot();
            var label = IsUsingExternalVolume() ? "external drive" : "Mac disk (Hub/Library)";
            return $"{label}: {root}";
        }

        public static string ResolveProjectGeneratedRoot()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return Path.GetFullPath(Path.Combine(hub, "Assets/EnvironmentKit/Generated"));
        }

        public static string ResolveProjectResearchCacheRoot()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return Path.GetFullPath(Path.Combine(hub, "Assets/EnvironmentKit/ResearchCache"));
        }

        public static string ResolveProjectGeneratedFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return ResolveProjectGeneratedRoot();
            return Path.Combine(ResolveProjectGeneratedRoot(), fileName.Trim().TrimStart('/', '\\'));
        }

        public static bool IsPathOnExternalVolume(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                return false;

            try
            {
                return Path.GetFullPath(absolutePath).StartsWith("/Volumes/", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static long ResolveAvailableFreeBytes(string absolutePath)
        {
            try
            {
                var full = Path.GetFullPath(absolutePath);
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root))
                    root = "/";

                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static bool HasMinimumFreeSpaceForWrites(string absolutePath, long minBytes)
        {
            var free = ResolveAvailableFreeBytes(absolutePath);
            return free <= 0 || free >= minBytes;
        }

        public static void ApplyToProcessEnvironment(System.Diagnostics.ProcessStartInfo psi)
        {
            if (psi?.EnvironmentVariables == null)
                return;
            psi.EnvironmentVariables[EnvVarName] = ResolveRoot();
        }

        static string TryResolveExternalRoot()
        {
            try
            {
                if (!Directory.Exists("/Volumes"))
                    return null;

                foreach (var volume in Directory.GetDirectories("/Volumes"))
                {
                    var name = Path.GetFileName(volume);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (name.Equals("Macintosh HD", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.StartsWith(".", StringComparison.Ordinal))
                        continue;

                    var candidate = Path.Combine(volume, ExternalBundleFolderName);
                    if (!TryEnsureWritable(candidate))
                        continue;
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnvironmentKit] External storage scan failed: {ex.Message}");
            }

            return null;
        }

        static bool TryEnsureWritable(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                var probe = Path.Combine(path, ".write_probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
#endif
