using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Exports structured agent prompts for Cursor SDK / meat loop workflows.</summary>
    public static class CaveBuildQualityAgentBridge
    {
        const string MeatLoopPromptPath = "Assets/EnvironmentKit/Generated/CaveBuildMeatLoopAgentPrompt.md";
        const string StructuredPromptPath = "Assets/EnvironmentKit/Generated/CaveBuildAgentPrompt.md";

        public static void WriteMeatLoopPrompt(CaveBuildQualityReport report, int pass)
        {
            WriteStructuredPrompt(report, includeLiveSection: false, pass: pass, path: MeatLoopPromptPath);
        }

        public static void WriteStructuredPrompt(
            CaveBuildQualityReport report,
            bool includeLiveSection,
            int pass = -1,
            string path = null)
        {
            if (report == null)
                return;

            EnsureGeneratedFolder();
            path ??= StructuredPromptPath;

            var sb = new StringBuilder();
            sb.AppendLine("# Cave build grading — Cursor agent instructions");
            sb.AppendLine();
            sb.AppendLine("> **Auto-invoke** runs `export-rung-prompt.ts` then `grade-and-fix.ts` — per-rung prompt in ");
            sb.AppendLine("> `CaveBuildActiveRungPrompt.md` / `CaveBuildAgentPrompt.md` with live Generated JSON.");
            sb.AppendLine("> (2026 research: arXiv/ACM, Unity 6, HN). Agent must: Situation → Research → Plan → Execute.");
            sb.AppendLine();
            if (pass >= 0)
                sb.AppendLine($"Meat loop pass: **{pass}**");
            sb.AppendLine($"Score: **{report.OverallScore}** ({report.LetterGrade}) | Acceptable: **{report.BuildAcceptable}** | Dud: **{report.IsDud}**");
            sb.AppendLine($"Recommended action: **{report.RecommendedAction}** | Mode: **{report.GradingMode}** | Version: **{report.GradingVersion}**");
            sb.AppendLine();
            sb.AppendLine("## Read first");
            sb.AppendLine($"- `{report.ExportPath}`");
            sb.AppendLine($"- `{CaveBuildResearchExporter.ResearchPath}` (prestige lab papers — primary)");
            sb.AppendLine($"- `{CaveBuildAgentContextExporter.LadderContextPath}` (activeRung, failingRungs)");
            sb.AppendLine($"- `{CaveBuildQualitySystem.ManifestPath}`");
            sb.AppendLine("- Package: `Packages/com.cursor.environment-authoring-kit/`");
            sb.AppendLine();
            sb.AppendLine("## Web research (required before code edits)");
            sb.AppendLine(CaveBuildResearchSources.WorkflowHint);
            sb.AppendLine();
            sb.AppendLine("Key URLs (also in CaveBuildLadderContext.json):");
            foreach (var url in CaveBuildResearchSources.All)
                sb.AppendLine($"- {url}");
            sb.AppendLine();

            if (report.DudReasons.Count > 0)
            {
                sb.AppendLine("## Dud reasons (hard fail)");
                foreach (var r in report.DudReasons)
                    sb.AppendLine($"- {r}");
                sb.AppendLine();
            }

            sb.AppendLine("## Stage rubric (fix in priority order)");
            sb.AppendLine("| Stage | Score | Weight | Critical | Issues |");
            sb.AppendLine("|-------|------:|-------:|:--------:|--------|");
            foreach (var stage in report.Stages)
            {
                if (stage == null)
                    continue;
                var crit = CaveBuildQualityRubric.IsCritical(stage.StageId) ? "yes" : "";
                var issueSummary = stage.Issues.Count > 0 ? stage.Issues[0] : "—";
                sb.AppendLine($"| {stage.StageId} | {stage.Score} | {stage.Weight} | {crit} | {issueSummary} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Failing stages (detail)");
            foreach (var stage in CaveBuildQualityRubric.GetFailingStages(report))
            {
                sb.AppendLine($"### {stage.StageId} — {stage.StageName} ({stage.Score}/100)");
                foreach (var issue in stage.Issues)
                    sb.AppendLine($"- {issue}");
                foreach (var fix in stage.Fixes)
                    sb.AppendLine($"- **Fix:** {fix}");
            }

            sb.AppendLine();
            sb.AppendLine("## Policy");
            sb.AppendLine("- **No onion:** no AdventureShell, PathCeiling stacks, BlockRingMid rings, legacy spline tubes.");
            sb.AppendLine("- **Full build:** one `RouteTerrainFloor` + one `RouteTerrainCeiling` under CaveGeometry.");
            sb.AppendLine("- **Layout prototype:** `LayoutWalkFloor` only — no block tunnel, no RouteTerrainCeiling.");
            sb.AppendLine("- Do not store API keys in the repo.");
            sb.AppendLine();

            if (includeLiveSection)
            {
                sb.AppendLine("## Play mode (live fix)");
                sb.AppendLine($"- Read `{CaveLiveCodegenRequest.ExportPath}`");
                sb.AppendLine("- Patch gameplay under `Assets/Scripts/` (movement, combat, spawn) when issues are runtime-only.");
                sb.AppendLine();
            }

            sb.AppendLine("## SDK");
            sb.AppendLine("```bash");
            sb.AppendLine("cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader");
            sb.AppendLine("npm install");
            sb.AppendLine("export CURSOR_API_KEY=cursor_...");
            sb.AppendLine("npm run doctor");
            sb.AppendLine("npx tsx grade-and-fix.ts --auto --stream");
            sb.AppendLine("```");

            File.WriteAllText(path, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
        }

        static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Generated"))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
        }
    }
}
