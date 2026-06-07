#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Editor-time playtest bot wiring (route bot on cave root — not misplaced CavePlaytestBotController-only).
    /// </summary>
    public static class CaveBuildPlaytestBotPlacement
    {
        public static void EnsureOnCaveRoot(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            if (caveRoot.GetComponent<CavePlaytestRouteBot>() == null)
                caveRoot.gameObject.AddComponent<CavePlaytestRouteBot>();

            CaveCombatGameTypes.EnsureCombatPlaytestBot(caveRoot, beginProbe: false);

            var legacyOnRoot = caveRoot.GetComponent<CavePlaytestBotController>();
            if (legacyOnRoot != null)
            {
                Object.DestroyImmediate(legacyOnRoot);
                Debug.Log(
                    "[CaveBuild] Removed CavePlaytestBotController from cave root — bot lives on CavePlaytestRouteBot avatar.");
            }

            EditorUtility.SetDirty(caveRoot.gameObject);
        }
    }
}
#endif
