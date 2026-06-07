using System;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Reads CaveBuildResearchExecutionBrief.json for automated fixes that must cite pulled cache data.</summary>
    public static class CaveBuildResearchExecutionBrief
    {
        [Serializable]
        class BriefFile
        {
            public HillshadeEntry[] hillshades;
            public EntryRef[] entries;
        }

        [Serializable]
        class HillshadeEntry
        {
            public string county;
            public string relativePath;
        }

        [Serializable]
        class EntryRef
        {
            public string id;
            public string contentPath;
        }

        public static void TryLogGroundPlacementRefs()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildResearchCacheBridge.ExecutionBriefPath);
            if (!File.Exists(path))
            {
                Debug.LogWarning(
                    "[CaveBuild] No execution brief on disk — run research pull or npm run export-research-execution-brief.");
                return;
            }

            try
            {
                var brief = JsonUtility.FromJson<BriefFile>(File.ReadAllText(path));
                if (brief?.hillshades != null && brief.hillshades.Length > 0)
                {
                    foreach (var h in brief.hillshades)
                    {
                        if (h == null || string.IsNullOrWhiteSpace(h.relativePath))
                            continue;
                        var rel = h.relativePath.StartsWith("Assets/", StringComparison.Ordinal)
                            ? h.relativePath
                            : $"Assets/EnvironmentKit/ResearchCache/{h.relativePath}";
                        Debug.Log($"[CaveBuild] ground_placement — use hillshade {h.county}: {rel}");
                    }
                }

                if (brief?.entries != null)
                {
                    var n = Mathf.Min(3, brief.entries.Length);
                    for (var i = 0; i < n; i++)
                    {
                        var e = brief.entries[i];
                        if (e == null || string.IsNullOrWhiteSpace(e.contentPath))
                            continue;
                        Debug.Log(
                            $"[CaveBuild] ground_placement — research entry {e.id}: {e.contentPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CaveBuild] Failed to parse execution brief: " + ex.Message);
            }
        }
    }
}
