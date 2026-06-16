#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Applies wizard tab brief JSON files to the active scene (markers / logs).</summary>
    public static class WizardBriefAuthors
    {
        const string Gen = "Assets/EnvironmentKit/Generated";

        [MenuItem("Window/Environment Kit/Apply Wizard Briefs/Caves")]
        public static void ApplyCaves() => ApplyBrief("CaveBuildCaveBrief.json", "CaveLayout_Authored");

        [MenuItem("Window/Environment Kit/Apply Wizard Briefs/Mazes")]
        public static void ApplyMazes() => ApplyBrief("CaveBuildMazeBrief.json", "MazeLayout_Authored");

        [MenuItem("Window/Environment Kit/Apply Wizard Briefs/Interior Content")]
        public static void ApplyInterior() => ApplyBrief("CaveBuildInteriorContentBrief.json", "InteriorContent_Authored");

        [MenuItem("Window/Environment Kit/Apply Wizard Briefs/Atmosphere")]
        public static void ApplyAtmosphere() => ApplyBrief("CaveBuildAtmosphereBrief.json", "Atmosphere_Authored");

        public static bool TryApplyBriefSilent(string fileName, string rootName, out string message)
        {
            message = null;
            var path = Path.Combine(Gen, fileName);
            if (!File.Exists(path))
            {
                message = $"Brief not found: {path}";
                return false;
            }

            var json = File.ReadAllText(path);
            var root = GameObject.Find(rootName);
            if (root == null)
                root = new GameObject(rootName);
            var holder = root.GetComponent<WizardBriefSceneHolder>();
            if (holder == null)
                holder = root.AddComponent<WizardBriefSceneHolder>();
            holder.briefJson = json;
            holder.briefFile = fileName;
            EditorUtility.SetDirty(root);
            message = $"Applied {fileName} → {rootName}";
            Debug.Log("[WizardBrief] " + message);
            return true;
        }

        static void ApplyBrief(string fileName, string rootName)
        {
            if (!TryApplyBriefSilent(fileName, rootName, out var message))
            {
                EditorUtility.DisplayDialog("Wizard brief", message, "OK");
                return;
            }

            EditorUtility.DisplayDialog("Wizard brief", message, "OK");
        }
    }

    public sealed class WizardBriefSceneHolder : MonoBehaviour
    {
        [TextArea(4, 12)] public string briefJson;
        public string briefFile;
    }
}
#endif
