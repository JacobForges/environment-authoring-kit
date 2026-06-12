#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Pipeline hook: world build + playthrough → JSON for portfolio multiplayer + proof bundle.
    /// Schema matches Hub.Multiplayer.CaveBuildWorldSessionManifest.
    /// </summary>
    public static class CaveBuildWorldSessionManifestWriter
    {
        public const string RelativePath = "Assets/EnvironmentKit/Generated/CaveBuildWorldSessionManifest.json";

        [Serializable]
        sealed class Manifest
        {
            public int version = 1;
            public string sceneName;
            public int seed;
            public string buildUtc;
            public int qualityScore;
            public bool buildAcceptable;
            public bool playthroughRecorded;
            public string notes;
        }

        public static void Write(
            string sceneName,
            int seed,
            CaveBuildQualityReport quality,
            bool playthroughRecorded,
            string notes = null)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var m = new Manifest
            {
                sceneName = sceneName ?? "MainScene",
                seed = seed,
                buildUtc = DateTime.UtcNow.ToString("o"),
                qualityScore = quality?.OverallScore ?? 0,
                buildAcceptable = quality != null && quality.BuildAcceptable,
                playthroughRecorded = playthroughRecorded,
                notes = notes ?? string.Empty,
            };
            File.WriteAllText(path, JsonUtility.ToJson(m, true) + "\n");
            Debug.Log("[CaveBuild] World session manifest → " + RelativePath);
        }
    }
}
#endif
