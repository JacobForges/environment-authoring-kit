#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBuildQualityMenu
    {
        [MenuItem("Window/Environment Kit/Cave Build/Regrade Active Scene (No Rebuild)")]
        public static void GradeActiveScene() => CaveBuildGraderWindow.Open();

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Regrade With Dialog", false, 1)]
        public static void GradeActiveSceneQuick()
        {
            var cave = GameObject.Find("LavaTubeCaveSystem");
            if (cave == null)
            {
                EditorUtility.DisplayDialog("Cave Quality", "LavaTubeCaveSystem not found. Build the cave first.", "OK");
                return;
            }

            var ground = SceneGroundResolver.Resolve();
            var request = new Generation.WorldGenerationRequest
            {
                Seed = 0,
                UseSplineMesh = true,
                UseBlockTunnel = true,
                UseTerrainCarve = true
            };
            var quality = CaveBuildQualityGrader.GradeFullBuild(cave.transform, ground, request, null);
            var target = CaveBuildQualityRubric.MeetsShipTarget(quality)
                ? "SHIP TARGET MET (commercial production)"
                : $"Target: {CaveBuildQualityRubric.TargetGrade} ({CaveBuildQualityRubric.ShipScore}+), Beta playtest: {CaveBuildQualityRubric.BetaScore}+";

            EditorUtility.DisplayDialog(
                "Cave Quality",
                $"Overall: {quality.LetterGrade} ({quality.OverallScore}/100)\n{target}\n\n" +
                $"Exported: {quality.ExportPath}\n\n" +
                "Paste that JSON into Cursor Agent to diagnose failed stages.",
                "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Open Quality Report (JSON)")]
        public static void OpenQualityReport()
        {
            const string path = "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                EditorUtility.DisplayDialog(
                    "Cave Quality",
                    "No report yet. Run Build Complete Cave Level first.",
                    "OK");
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
#endif
