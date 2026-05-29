#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Single source of truth for the paced FullWorld cave queue (Build X/120).</summary>
    public static class CaveBuildQueuedPipelineSchedule
    {
        public const int Total = 122;

        public const int Validate = 0;
        public const int GeoFirst = 1;
        public const int GeoCount = 15;
        public const int PlayabilityFirst = 16;
        public const int ValidationFirst = 34;
        public const int ValidationCount = 6;
        public const int GroundPolishFirst = 40;
        public const int GroundPolishCount = 10;
        public const int WorldFirst = 50;
        public const int WorldCount = 15;
        public const int Meat = 65;
        public const int PostMeatFirst = 66;
        public const int PostMeatCount = 24;
        public const int ResearchFirst = 90;
        public const int ResearchCount = 12;
        public const int FinalizePolishFirst = 102;
        public const int FinalizePolishCount = 18;
        public const int AaaManifest = 120;
        public const int FinishReport = 121;

        public const int ManifestQueuedStepIndex = AaaManifest;

        public static readonly string[] WorldStageLabels =
        {
            "Organic mesh QA",
            "Scatter natural props along path",
            "Occlusion shell (seal sky gaps)",
            "Cave rock materials",
            "Cinematic lighting (AAA sun + cave key/fill/rim)",
            "FX — motes, gleam, entrance glow, fog mist",
            "Block cull distance + colliders + LOD (XR)",
            "NavMesh (floor walkable only)",
            "Spawn points (surface + cave) & portal",
            "Final material + collider pass",
            "Enclosure + playability validation",
            "Cave burial touch-up under heightmap",
            "Route-adjacent prop scatter pass",
            "XR LOD + shell gap second pass",
            "World grounding lock + mouth depth verify",
        };

        public static readonly string[] GroundPolishLabels =
        {
            "Re-seat cave root under terrain lip",
            "Burial envelope pass 1 (heightmap)",
            "Strip above-ground roof meshes",
            "Burial guardrail + protrusion audit",
            "Depth-only mouth snap (no raise)",
            "Entrance terrain carve refresh",
            "Burial envelope pass 2",
            "Roof auditor second pass",
            "Lock cave world XZ placement",
            "Pre-world playable layout gate",
        };

        public static readonly string[] FinalizePolishLabels =
        {
            "Final heightmap burial pass",
            "Final above-ground roof strip",
            "Surface prop coverage audit",
            "Nine-tile vegetation contract check",
            "Quality regrade + JSON export",
            "Surface NavMesh commit verify",
            "Cave spawn + mouth alignment",
            "Route probe + reachability",
            "Visual shell + collider repair",
            "Commercial pre-manifest spot check",
            "Completion contract + readout MD",
            "Ladder metrics + phase catalog flush",
            "Post-build helper script handoff",
            "Incremental ladder finalize gate",
            "Pre-manifest pacing settle",
            "Burial residual check",
            "Prop plan JSON sync",
            "Manifest handoff prep",
        };

        public static string StepLabel(int step)
        {
            if (step == Validate)
                return "cave validate & prep";
            if (step >= GeoFirst && step < GeoFirst + GeoCount)
                return $"cave geo {step - GeoFirst + 1}/{GeoCount} — {GeoLabel(step)}";
            if (step >= PlayabilityFirst &&
                step < PlayabilityFirst + CaveAdventurePlayabilityPipeline.StepCount)
            {
                var play = step - PlayabilityFirst;
                return $"cave play {play + 1}/{CaveAdventurePlayabilityPipeline.StepCount} — " +
                       CaveAdventurePlayabilityPipeline.StepLabels[play];
            }

            if (step >= ValidationFirst && step < ValidationFirst + ValidationCount)
            {
                var v = step - ValidationFirst;
                return $"cave validation {v + 1}/{ValidationCount} — " +
                       CaveBuildAutomatedValidation.StepLabels[v];
            }

            if (step >= GroundPolishFirst && step < GroundPolishFirst + GroundPolishCount)
            {
                var g = step - GroundPolishFirst;
                return $"cave ground {g + 1}/{GroundPolishCount} — {GroundPolishLabels[g]}";
            }

            if (step >= WorldFirst && step < WorldFirst + WorldCount)
            {
                var w = step - WorldFirst;
                return $"cave world {w + 1}/{WorldCount} — {WorldStageLabels[w]}";
            }

            if (step == Meat)
                return "cave meat loop (queued phases)";
            if (step >= PostMeatFirst && step < PostMeatFirst + PostMeatCount)
                return $"cave post-meat {step - PostMeatFirst + 1}/{PostMeatCount}";
            if (step >= ResearchFirst && step < ResearchFirst + ResearchCount)
                return $"cave research {step - ResearchFirst + 1}/{ResearchCount}";
            if (step >= FinalizePolishFirst && step < FinalizePolishFirst + FinalizePolishCount)
            {
                var f = step - FinalizePolishFirst;
                return $"cave finalize {f + 1}/{FinalizePolishCount} — {FinalizePolishLabels[f]}";
            }

            if (step == AaaManifest)
                return "cave commercial production manifest (100pt)";
            if (step == FinishReport)
                return "cave finalize report";
            return $"cave step {step + 1}/{Total}";
        }

        public static string GeoLabel(int step) => (step - GeoFirst + 1) switch
        {
            1 => "clear & roots",
            2 => "entrance",
            3 => "maze layout",
            4 => "add terrain",
            5 => "walk platforms",
            6 => "enclosure shell (walkway)",
            7 => "labyrinth annex (playable maze)",
            8 => "route-end grand cavern",
            9 => "blocks — prepare",
            10 => "blocks — ring batch",
            11 => "blocks — wall details",
            12 => "features & goal",
            13 => "surface walk-in entrance",
            14 => "spawn & runtime",
            15 => "props, mobs & water",
            _ => "geometry",
        };
    }
}
#endif
