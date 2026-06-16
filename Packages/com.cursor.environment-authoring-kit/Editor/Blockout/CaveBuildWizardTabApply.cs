#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Applies each wizard tab in Unity immediately after brief approval, saves the scene, then
    /// allows the next tab (manual or full pipeline). One tab at a time.
    /// </summary>
    [InitializeOnLoad]
    public static class CaveBuildWizardTabApply
    {
        public const string ApplyRelPath = "Assets/EnvironmentKit/Generated/CaveBuildWizardTabApply.json";

        static double _lastPollAt;
        static bool _terrainStartAttempted;
        static string _activeRequestId;

        static CaveBuildWizardTabApply()
        {
            EditorApplication.update += Tick;
        }

        public static bool IsActive
        {
            get
            {
                if (!TryLoad(out var state) || state.current == null)
                    return false;
                return IsBusy(state.current);
            }
        }

        static bool IsBusy(TabApplyItem item)
        {
            if (item == null)
                return false;
            if (string.Equals(item.status, "pending", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.status, "running", StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(item.phase, "waiting_terrain", StringComparison.OrdinalIgnoreCase);
        }

        static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastPollAt < 0.5)
                return;

            _lastPollAt = now;

            if (!TryLoad(out var state))
                return;

            if (state.current == null)
            {
                PromoteQueue(ref state);
                if (state.current == null)
                    return;
            }

            var item = state.current;
            if (string.Equals(item.status, "done", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.status, "error", StringComparison.OrdinalIgnoreCase))
            {
                PromoteQueue(ref state);
                return;
            }

            if (!string.Equals(item.requestId, _activeRequestId, StringComparison.Ordinal))
            {
                _activeRequestId = item.requestId;
                _terrainStartAttempted = false;
            }

            try
            {
                Advance(ref state, item);
            }
            catch (Exception ex)
            {
                Fail(ref state, item, ex.Message);
                Debug.LogException(ex);
            }
        }

        static void Advance(ref TabApplyState state, TabApplyItem item)
        {
            item.status = "running";

            if (string.Equals(item.tabId, "terrain", StringComparison.OrdinalIgnoreCase))
            {
                AdvanceTerrain(ref state, item);
                return;
            }

            if (!TryApplyTab(item, out var message))
                Debug.LogWarning("[WizardTabApply] " + item.tabId + " skipped: " + message);
            else
                Debug.Log("[WizardTabApply] " + message);

            SaveDirtyScenes();
            FinishCurrent(ref state, item);
        }

        static void AdvanceTerrain(ref TabApplyState state, TabApplyItem item)
        {
            var phase = item.phase ?? "terrain_build";

            if (string.Equals(phase, "terrain_build", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "pending", StringComparison.OrdinalIgnoreCase))
            {
                item.phase = "terrain_build";
                Write(state);

                if (CaveBuildHubSessionReconcile.IsPacedWorkActive())
                    return;

                if (!_terrainStartAttempted)
                {
                    _terrainStartAttempted = true;
                    if (!LavaTubeCaveBuilder.TryStartFromWizardPipelineApply(out var msg))
                    {
                        Fail(ref state, item, msg);
                        return;
                    }

                    item.phase = "waiting_terrain";
                    Write(state);
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        "wizard",
                        "Tab apply — terrain build running…");
                    return;
                }

                item.phase = "waiting_terrain";
                Write(state);
                return;
            }

            if (string.Equals(phase, "waiting_terrain", StringComparison.OrdinalIgnoreCase))
            {
                if (CaveBuildHubSessionReconcile.IsPacedWorkActive())
                    return;

                SaveDirtyScenes();
                CaveBuildRunStatusPublisher.PulseSubOperation("wizard", "Tab apply — terrain saved.");
                FinishCurrent(ref state, item);
            }
        }

        static bool TryApplySurfaceContent(out string message)
        {
            message = null;
            var hubType = System.Type.GetType("MainSceneContentLayoutSetup, Assembly-CSharp-Editor");
            if (hubType != null)
            {
                var method = hubType.GetMethod(
                    "ApplySilentForWizard",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    var args = new object[] { null };
                    var ok = (bool)method.Invoke(null, args);
                    message = args[0] as string;
                    return ok;
                }
            }

            return MainSceneContentLayoutAuthor.TryApplyMainSceneSilent(out message);
        }

        static bool TryApplyTab(TabApplyItem item, out string message)
        {
            message = null;
            switch (item.tabId)
            {
                case "surface-content":
                    return TryApplySurfaceContent(out message);
                case "caves":
                    return WizardBriefAuthors.TryApplyBriefSilent(
                        "CaveBuildCaveBrief.json",
                        "CaveLayout_Authored",
                        out message);
                case "mazes":
                    return WizardBriefAuthors.TryApplyBriefSilent(
                        "CaveBuildMazeBrief.json",
                        "MazeLayout_Authored",
                        out message);
                case "interior-content":
                    return WizardBriefAuthors.TryApplyBriefSilent(
                        "CaveBuildInteriorContentBrief.json",
                        "InteriorContent_Authored",
                        out message);
                case "atmosphere":
                    return WizardBriefAuthors.TryApplyBriefSilent(
                        "CaveBuildAtmosphereBrief.json",
                        "Atmosphere_Authored",
                        out message);
                default:
                    message = "No Unity apply for tab " + item.tabId;
                    return false;
            }
        }

        static void FinishCurrent(ref TabApplyState state, TabApplyItem item)
        {
            item.status = "done";
            item.completedUtc = UtcNow();
            item.error = null;
            state.completed = Append(state.completed, item);
            state.current = null;
            _terrainStartAttempted = false;
            Write(state);
            PromoteQueue(ref state);
            Debug.Log("[WizardTabApply] Tab " + item.tabId + " applied and scene saved.");
        }

        static void PromoteQueue(ref TabApplyState state)
        {
            if (state.current != null || state.queue == null || state.queue.Length == 0)
                return;

            state.current = state.queue[0];
            state.queue = ShiftQueue(state.queue);
            Write(state);
        }

        static void Fail(ref TabApplyState state, TabApplyItem item, string message)
        {
            item.status = "error";
            item.error = message;
            item.updatedUtc = UtcNow();
            state.current = item;
            _terrainStartAttempted = false;
            Write(state);
            Debug.LogWarning("[WizardTabApply] " + message);
        }

        static void SaveDirtyScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty || !scene.IsValid())
                    continue;

                if (!EditorSceneManager.SaveScene(scene))
                    Debug.LogWarning("[WizardTabApply] Could not save scene: " + scene.path);
            }

            AssetDatabase.SaveAssets();
        }

        static string FullPath()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return Path.Combine(hub, ApplyRelPath);
        }

        static bool TryLoad(out TabApplyState state)
        {
            state = null;
            var path = FullPath();
            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                state = JsonUtility.FromJson<TabApplyState>(json);
                return state != null;
            }
            catch (IOException)
            {
                return false;
            }
        }

        static void Write(TabApplyState state)
        {
            state.updatedUtc = UtcNow();
            var path = FullPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(state, true) + "\n");
        }

        static TabApplyItem[] Append(TabApplyItem[] completed, TabApplyItem item)
        {
            var n = completed?.Length ?? 0;
            var arr = new TabApplyItem[n + 1];
            if (completed != null)
                Array.Copy(completed, arr, n);
            arr[n] = item;
            return arr;
        }

        static TabApplyItem[] ShiftQueue(TabApplyItem[] queue)
        {
            if (queue == null || queue.Length <= 1)
                return Array.Empty<TabApplyItem>();
            var arr = new TabApplyItem[queue.Length - 1];
            Array.Copy(queue, 1, arr, 0, arr.Length);
            return arr;
        }

        static string UtcNow() => DateTime.UtcNow.ToString("o");

        [Serializable]
        sealed class TabApplyState
        {
            public int version = 2;
            public TabApplyItem current;
            public TabApplyItem[] queue;
            public TabApplyItem[] completed;
            public string updatedUtc;
        }

        [Serializable]
        sealed class TabApplyItem
        {
            public string tabId;
            public string applyStep;
            public string status;
            public string phase;
            public string requestId;
            public string requestedUtc;
            public string updatedUtc;
            public string error;
            public string completedUtc;
        }
    }
}
#endif
