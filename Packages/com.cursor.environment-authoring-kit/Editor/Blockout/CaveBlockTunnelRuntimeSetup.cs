using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBlockTunnelRuntimeSetup
    {
        [MenuItem("Window/Environment Kit/Restore Cave Block Visibility (Active Scene)")]
        public static void RestoreVisibilityFromMenu()
        {
            var caveRoot = GameObject.Find("Grid")?.transform.Find("LavaTubeCaveSystem");
            caveRoot ??= GameObject.Find("LavaTubeCaveSystem")?.transform;
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog("Cave Blocks", "LavaTubeCaveSystem not found.", "OK");
                return;
            }

            foreach (var renderer in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null && renderer.gameObject.name.StartsWith("CaveBlock_"))
                    renderer.enabled = true;
            }

            var culler = caveRoot.GetComponent<CaveBlockTunnelCuller>();
            culler?.RestoreAllBlocks();
            SceneView.RepaintAll();
            Debug.Log("[CaveBuild] Restored all cave block renderers for editor view.");
        }

        [MenuItem("Window/Environment Kit/Add Cave Block Culling (Active Scene)")]
        public static void EnsureFromMenu()
        {
            var caveRoot = GameObject.Find("Grid")?.transform.Find("LavaTubeCaveSystem");
            caveRoot ??= GameObject.Find("LavaTubeCaveSystem")?.transform;
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Cave Block Culling",
                    "LavaTubeCaveSystem not found. Build the cave first.",
                    "OK");
                return;
            }

            EnsureOnCaveRoot(caveRoot);
            var culler = caveRoot.GetComponent<CaveBlockTunnelCuller>();
            if (culler != null)
                culler.RebuildBlockCache();

            EditorUtility.DisplayDialog(
                "Cave Block Culling",
                "CaveBlockTunnelCuller added/updated on LavaTubeCaveSystem.\n" +
                "Adjust Render Radius on that component in Play mode.",
                "OK");
        }

        public static void EnsureOnCaveRoot(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var tunnel = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            if (tunnel == null)
                return;

            var culler = caveRoot.GetComponent<CaveBlockTunnelCuller>();
            if (culler == null)
                culler = CaveEditorUndo.GetOrAddComponent<CaveBlockTunnelCuller>(caveRoot.gameObject);

            CaveEditorUndo.RecordObject(culler, "Cave Block Culler");
            culler.distanceCullingEnabled = false;
            // XR-friendly defaults when play-mode culling is enabled (editor grading uses visibility budget).
            culler.renderRadius = 32f;
            culler.collisionRadius = 24f;
            culler.refreshInterval = 0.35f;

            // FDG HDPCG 2026 — do not mass-enable shell blocks before grading (7M+ tris / block_tunnel fail).
            CavePerformanceBudget.DisableHighPolySplineDescendants(caveRoot);
            CavePerformanceBudget.DisableSplineSubtreeRenderers(caveRoot);
            CavePerformanceBudget.ApplyBlockRendererPolicyOnly(caveRoot);
            CavePerformanceBudget.ApplyMinableVisibilityBudget(caveRoot);
            CavePerformanceBudget.EnsureGradingTriangleBudget(caveRoot);
            CaveInvisibleColliderUtility.StripForAdventure(caveRoot);

            var bootstrap = caveRoot.GetComponent<CaveTunnelVisibilityBootstrap>();
            if (bootstrap == null)
                bootstrap = CaveEditorUndo.GetOrAddComponent<CaveTunnelVisibilityBootstrap>(caveRoot.gameObject);

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null && meta.adventureHybrid)
                bootstrap.enableInteriorTubeMeshes = false;

            bootstrap.disableDistanceCullingOnLoad = true;
        }
    }
}
