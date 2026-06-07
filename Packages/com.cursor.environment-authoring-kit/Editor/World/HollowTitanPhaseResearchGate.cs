#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Mandatory research + action plan before each Hollow Titan meat phase (0–11).
    /// Writes gate JSON + Generated prompts agents can open.
    /// </summary>
    public static class HollowTitanPhaseResearchGate
    {
        public const string GateRel =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanPhaseResearchGate.json";

        public const string ActionPlanRel = HollowTitanPhasePromptBridge.ResearchActionPlanPath;

        [Serializable]
        public class GateFile
        {
            public bool passed;
            public string completedUtc;
            public int phaseIndex;
            public string phaseId;
            public string phaseLabel;
            public int buildSeed;
            public string masterConceptImageRel;
            public string phaseConceptImageRel;
            public string activePhasePromptRel;
            public string researchAgentPromptRel;
            public string message;
        }

        public static bool EnsureBeforeMeatPhase(int phaseIndex, int seed, out string message) =>
            EnsureBeforeMeatPhase(phaseIndex, seed, out message, out _);

        public static bool EnsureBeforeMeatPhase(
            int phaseIndex,
            int seed,
            out string message,
            out bool awaitingPromptTsx)
        {
            message = string.Empty;
            awaitingPromptTsx = false;
            phaseIndex = Mathf.Clamp(phaseIndex, 0, HollowTitanConceptCatalog.PhaseCount - 1);

            if (IsRecentlyPassed(phaseIndex, seed))
            {
                message = $"Hollow Titan research gate OK (cached): phase {phaseIndex}.";
                return true;
            }

            var phaseId = HollowTitanConceptCatalog.GetPhaseId(phaseIndex);
            var phaseLabel = HollowTitanConceptCatalog.GetPhaseLabel(phaseIndex);

            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Research gate: phase {phaseIndex} ({phaseId})…",
                forceUnityConsole: false);

            if (!RefreshPromptsForGate(phaseIndex, seed, out message, out awaitingPromptTsx))
                return false;

            if (awaitingPromptTsx)
                return true;

            WriteGate(
                true,
                phaseIndex,
                phaseId,
                phaseLabel,
                seed,
                message);
            return true;
        }

        public static void EnsureBeforeMeatPhaseAsync(int phaseIndex, int seed, Action onReady)
        {
            if (onReady == null)
                return;

            if (EnsureBeforeMeatPhase(phaseIndex, seed, out var message, out var awaiting) && !awaiting)
            {
                onReady();
                return;
            }

            if (!awaiting)
            {
                onReady();
                return;
            }

            RefreshPromptsForGateAsync(phaseIndex, seed, ok =>
            {
                if (ok)
                {
                    var phaseId = HollowTitanConceptCatalog.GetPhaseId(phaseIndex);
                    var phaseLabel = HollowTitanConceptCatalog.GetPhaseLabel(phaseIndex);
                    WriteGate(true, phaseIndex, phaseId, phaseLabel, seed, message);
                }

                onReady();
            });
        }

        static bool RefreshPromptsForGate(
            int phaseIndex,
            int seed,
            out string message,
            out bool awaitingPromptTsx)
        {
            awaitingPromptTsx = false;
            message = string.Empty;

            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx)
            {
                awaitingPromptTsx = RefreshPromptsForGateAsync(phaseIndex, seed, null);
                if (awaitingPromptTsx)
                {
                    message =
                        $"Hollow Titan research gate awaiting prompt tsx for phase {phaseIndex} ({HollowTitanConceptCatalog.GetPhaseId(phaseIndex)}).";
                    CaveBuildEditorLog.LogSurface(message, forceUnityConsole: false);
                    return true;
                }
            }

            return RunPromptExportsSync(phaseIndex, seed, out message);
        }

        static bool RefreshPromptsForGateAsync(int phaseIndex, int seed, Action<bool> onComplete)
        {
            var pending = 0;
            var ok = true;

            void Done(bool stepOk, string stepMsg)
            {
                if (!stepOk)
                    ok = false;
                pending--;
                if (pending <= 0)
                    onComplete?.Invoke(ok || HasMinimumCachedPrompts());
            }

            void Launch(string scriptName, string phaseArg)
            {
                pending++;
                TryBeginOrRun(scriptName, phaseArg, phaseIndex, seed, Done);
            }

            Launch("export-hollow-titan-research-brief.ts", $"--phase={phaseIndex}");
            Launch("generate-hollow-titan-phase-prompt-manifest.ts", null);
            Launch("generate-hollow-titan-research-prompt.ts", $"--phase={phaseIndex}");
            Launch("generate-hollow-titan-active-phase-prompt.ts", $"--phase={phaseIndex}");
            Launch("generate-hollow-titan-research-action-plan.ts", $"--phase={phaseIndex}");

            if (pending == 0)
                onComplete?.Invoke(true);

            return pending > 0;
        }

        static void TryBeginOrRun(
            string scriptName,
            string phaseArg,
            int phaseIndex,
            int seed,
            Action<bool, string> onComplete)
        {
            var envs = new[]
            {
                $"HOLLOW_TITAN_ACTIVE_PHASE={phaseIndex}",
                $"HOLLOW_TITAN_BUILD_SEED={seed}",
            };

            if (CaveBuildPhasePromptBridge.RequiresNonBlockingTsx &&
                HollowTitanPhasePromptBridge.TryBeginRunTsx(
                    scriptName,
                    phaseArg,
                    60_000,
                    onComplete,
                    envs))
                return;

            var syncOk = RunSingleExport(scriptName, phaseArg, phaseIndex, seed, out var syncMsg);
            onComplete(syncOk, syncMsg);
        }

        static bool RunPromptExportsSync(int phaseIndex, int seed, out string message)
        {
            var ok = true;
            var parts = new System.Collections.Generic.List<string>();

            if (!HollowTitanPhasePromptBridge.ExportResearchExecutionBrief(phaseIndex, out var briefMsg))
            {
                if (!HasCachedExecutionBrief())
                    ok = false;
                parts.Add(briefMsg);
            }

            if (!HollowTitanPhasePromptBridge.ExportPhasePromptManifest(out var manifestMsg))
            {
                ok = false;
                parts.Add(manifestMsg);
            }

            if (!HollowTitanPhasePromptBridge.ExportResearchAgentPrompt(phaseIndex, out var researchMsg))
            {
                if (!HasCachedResearchPrompts())
                    ok = false;
                parts.Add(researchMsg);
            }

            if (!HollowTitanPhasePromptBridge.ExportActivePhasePrompt(phaseIndex, seed, out var activeMsg))
            {
                if (!HasCachedActivePrompt())
                    ok = false;
                parts.Add(activeMsg);
            }

            if (!HollowTitanPhasePromptBridge.ExportResearchActionPlan(phaseIndex, seed, out var planMsg))
            {
                if (!HasCachedActionPlan())
                    ok = false;
                parts.Add(planMsg);
            }

            message = ok
                ? $"Phase {phaseIndex}: research prompts exported."
                : string.Join(" | ", parts);
            return ok || HasMinimumCachedPrompts();
        }

        static bool RunSingleExport(
            string scriptName,
            string phaseArg,
            int phaseIndex,
            int seed,
            out string message)
        {
            switch (scriptName)
            {
                case "export-hollow-titan-research-brief.ts":
                    return HollowTitanPhasePromptBridge.ExportResearchExecutionBrief(phaseIndex, out message);
                case "generate-hollow-titan-phase-prompt-manifest.ts":
                    return HollowTitanPhasePromptBridge.ExportPhasePromptManifest(out message);
                case "generate-hollow-titan-research-prompt.ts":
                    return HollowTitanPhasePromptBridge.ExportResearchAgentPrompt(phaseIndex, out message);
                case "generate-hollow-titan-active-phase-prompt.ts":
                    return HollowTitanPhasePromptBridge.ExportActivePhasePrompt(phaseIndex, seed, out message);
                case "generate-hollow-titan-research-action-plan.ts":
                    return HollowTitanPhasePromptBridge.ExportResearchActionPlan(phaseIndex, seed, out message);
                default:
                    message = $"Unknown script {scriptName}";
                    return false;
            }
        }

        static bool HasCachedResearchPrompts()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, HollowTitanPhasePromptBridge.ResearchAgentPromptPath));
        }

        static bool HasCachedActivePrompt()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, HollowTitanPhasePromptBridge.ActivePhasePromptPath));
        }

        static bool HasCachedActionPlan()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, HollowTitanPhasePromptBridge.ResearchActionPlanPath));
        }

        static bool HasCachedExecutionBrief()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            return File.Exists(Path.Combine(hub, HollowTitanPhasePromptBridge.ResearchExecutionBriefJsonPath));
        }

        static bool HasMinimumCachedPrompts() =>
            HasCachedActivePrompt() && HasCachedResearchPrompts() && HasCachedExecutionBrief();

        static bool IsRecentlyPassed(int phaseIndex, int seed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GateRel);
            if (!File.Exists(path))
                return false;

            try
            {
                var gate = JsonUtility.FromJson<GateFile>(File.ReadAllText(path));
                return gate.passed &&
                       gate.buildSeed == seed &&
                       gate.phaseIndex == phaseIndex;
            }
            catch
            {
                return false;
            }
        }

        static void WriteGate(
            bool passed,
            int phaseIndex,
            string phaseId,
            string phaseLabel,
            int seed,
            string message)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GateRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var gate = new GateFile
            {
                passed = passed,
                completedUtc = DateTime.UtcNow.ToString("o"),
                phaseIndex = phaseIndex,
                phaseId = phaseId,
                phaseLabel = phaseLabel,
                buildSeed = seed,
                masterConceptImageRel = HollowTitanConceptCatalog.MasterConceptImageRel,
                phaseConceptImageRel = HollowTitanConceptCatalog.GetPhaseConceptImageRel(phaseIndex),
                activePhasePromptRel = HollowTitanPhasePromptBridge.ActivePhasePromptPath,
                researchAgentPromptRel = HollowTitanPhasePromptBridge.ResearchAgentPromptPath,
                message = message,
            };

            File.WriteAllText(path, JsonUtility.ToJson(gate, true));
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Research gate → {GateRel} (phase {phaseIndex}, seed {seed})",
                forceUnityConsole: false);
        }
    }
}
#endif
