#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Loads per-card approved kit prefabs + concept images from finalized planner brief.
    /// </summary>
    public static class CaveBuildPlannerApprovedAssets
    {
        public const string ManifestRelPath =
            "Assets/EnvironmentKit/Generated/CaveBuildPlannerApprovedConceptCards.json";

        [Serializable]
        public sealed class ApprovedCard
        {
            public string id;
            public string cardType;
            public string label;
            public string imageRel;
            public string prefabPath;
            public int instanceCount = 1;
            public int targetInstanceCount;
            public string categoryKey;
            public string markerSlotKey;
            public string slotLabel;
            public bool allowCountAdjust;

            public int EffectiveCount =>
                targetInstanceCount > 0 ? targetInstanceCount : Mathf.Max(1, instanceCount);
        }

        [Serializable]
        sealed class ManifestFile
        {
            public ApprovedCard[] cards;
        }

        static List<ApprovedCard> _cards;
        static bool _loaded;

        public static void ClearSession()
        {
            _cards = null;
            _loaded = false;
        }

        public static bool TryLoadFromBrief(out List<ApprovedCard> cards)
        {
            cards = null;
            if (!CaveBuildPlannerLayoutBridge.TryLoadBriefFile(out var file, out _))
                return false;

            if (file?.approvedConceptCards != null && file.approvedConceptCards.Length > 0)
            {
                cards = file.approvedConceptCards.Where(c => c != null).ToList();
                return cards.Count > 0;
            }

            return false;
        }

        public static bool EnsureLoaded()
        {
            if (_loaded)
                return _cards != null && _cards.Count > 0;

            _loaded = true;
            _cards = null;

            if (!CaveBuildSessionConfig.HasFinalizedActive)
                return false;

            if (!TryLoadFromBrief(out var fromBrief) || fromBrief == null || fromBrief.Count == 0)
                return false;

            _cards = fromBrief;
            ExportManifestSnapshot(_cards);
            LogLoaded();
            return true;
        }

        public static IReadOnlyList<ApprovedCard> Cards => _cards;

        public static GameObject ResolveForLayoutMarker(CaveBuildPlannerLayoutBridge.Marker marker)
        {
            if (marker == null || !EnsureLoaded() || _cards == null)
                return null;

            if (!string.IsNullOrEmpty(marker.plannerCardId))
            {
                foreach (var card in _cards)
                {
                    if (card == null || string.IsNullOrEmpty(card.prefabPath))
                        continue;
                    if (!string.Equals(card.id, marker.plannerCardId, StringComparison.Ordinal))
                        continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                    if (go != null)
                        return go;
                }
            }

            if (!string.IsNullOrEmpty(marker.markerSlotKey))
            {
                foreach (var card in _cards)
                {
                    if (card == null || string.IsNullOrEmpty(card.prefabPath))
                        continue;
                    if (!string.Equals(card.markerSlotKey, marker.markerSlotKey, StringComparison.Ordinal))
                        continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                    if (go != null)
                        return go;
                }
            }

            return ResolvePrefabForMarker(marker.kind, marker.label);
        }

        public static GameObject ResolvePrefabForMarker(string kind, string label)
        {
            if (!EnsureLoaded() || _cards == null)
                return null;

            var stem = ExtractStem(label);
            foreach (var card in _cards)
            {
                if (card == null || string.IsNullOrEmpty(card.prefabPath))
                    continue;
                if (!string.Equals(card.cardType, kind, StringComparison.OrdinalIgnoreCase))
                    continue;

                var cardStem = Path.GetFileNameWithoutExtension(card.prefabPath);
                if (!string.IsNullOrEmpty(stem) &&
                    (label.IndexOf(cardStem, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cardStem.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                    if (go != null)
                        return go;
                }

                if (!string.IsNullOrEmpty(card.label) &&
                    label.IndexOf(card.label, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                    if (go != null)
                        return go;
                }
            }

            return ResolvePrefab(kind, label);
        }

        static string ExtractStem(string label)
        {
            if (string.IsNullOrEmpty(label))
                return string.Empty;
            var open = label.LastIndexOf('(');
            var close = label.LastIndexOf(')');
            if (open >= 0 && close > open)
                return label.Substring(open + 1, close - open - 1).Trim();
            return label.Trim();
        }

        public static GameObject ResolvePrefab(string cardType, string labelHint = null)
        {
            if (!EnsureLoaded() || _cards == null)
                return null;

            var type = cardType ?? string.Empty;
            var hint = labelHint ?? string.Empty;

            foreach (var card in _cards)
            {
                if (card == null || string.IsNullOrEmpty(card.prefabPath))
                    continue;
                if (!string.Equals(card.cardType, type, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(hint) &&
                    !string.IsNullOrEmpty(card.label) &&
                    card.label.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0 &&
                    hint.IndexOf(card.label, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                if (go != null)
                    return go;
            }

            foreach (var card in _cards)
            {
                if (card == null || string.IsNullOrEmpty(card.prefabPath))
                    continue;
                if (!string.Equals(card.cardType, type, StringComparison.OrdinalIgnoreCase))
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                if (go != null)
                    return go;
            }

            return null;
        }

        public static GameObject ResolvePropPrefab(SurfacePropCategory category)
        {
            if (!EnsureLoaded() || _cards == null)
                return null;

            var want = category switch
            {
                SurfacePropCategory.Trees => new[] { "tree", "prop" },
                SurfacePropCategory.Grass => new[] { "grass", "prop" },
                SurfacePropCategory.Rocks => new[] { "rock", "prop" },
                _ => new[] { "prop", "bush" },
            };

            foreach (var token in want)
            {
                foreach (var card in _cards)
                {
                    if (card == null || string.IsNullOrEmpty(card.prefabPath))
                        continue;
                    if (!string.Equals(card.cardType, "prop", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var lab = card.label ?? string.Empty;
                    if (token != "prop" &&
                        lab.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (card.id ?? string.Empty).IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                    if (go != null)
                        return go;
                }
            }

            foreach (var card in _cards)
            {
                if (card == null || string.IsNullOrEmpty(card.prefabPath))
                    continue;
                if (!string.Equals(card.cardType, "prop", StringComparison.OrdinalIgnoreCase))
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(card.prefabPath);
                if (go != null)
                    return go;
            }

            return null;
        }

        static void ExportManifestSnapshot(List<ApprovedCard> cards)
        {
            try
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var path = Path.Combine(hub, ManifestRelPath);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonUtility.ToJson(new ManifestFile { cards = cards.ToArray() }, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlannerApprovedAssets] Could not write manifest snapshot: " + ex.Message);
            }
        }

        static void LogLoaded()
        {
            if (_cards == null || _cards.Count == 0)
                return;

            var withPrefab = _cards.Count(c => !string.IsNullOrEmpty(c?.prefabPath));
            CaveBuildEditorLog.LogSurface(
                $"[PlannerApprovedAssets] Bound {_cards.Count} approved concept card(s) " +
                $"({withPrefab} with kit prefab paths) for generation.",
                forceUnityConsole: true);
        }
    }
}
#endif
