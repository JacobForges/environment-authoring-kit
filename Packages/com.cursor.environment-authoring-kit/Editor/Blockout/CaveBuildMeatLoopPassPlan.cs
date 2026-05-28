using System;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Per cave meat-loop pass: underground enrichment + cave rubric stages only (no surface terrain work).</summary>
    public static class CaveBuildMeatLoopPassPlan
    {
        public enum EnrichmentSlot
        {
            Props = 0,
            MaterialsLighting = 1,
            AtmosphereFog = 2,
            MobsCombat = 3,
            VisualPolish = 4,
            DecalsDetails = 5,
            AudioAmbience = 6,
            PerformanceTrim = 7,
        }

        public enum WorldScope
        {
            CaveOnly = 0,
        }

        public enum SurfaceTask
        {
            None = 0,
        }

        public readonly struct PassMission
        {
            public readonly int Pass;
            public readonly string Title;
            public readonly string ResearchFocus;
            public readonly EnrichmentSlot Enrichment;
            public readonly WorldScope Scope;
            public readonly SurfaceTask Surface;
            public readonly string[] GradeStageIds;
            public readonly int[] GradeWeights;

            public PassMission(
                int pass,
                string title,
                string researchFocus,
                EnrichmentSlot enrichment,
                WorldScope scope,
                SurfaceTask surface,
                string[] gradeStageIds,
                int[] gradeWeights = null)
            {
                Pass = pass;
                Title = title;
                ResearchFocus = researchFocus;
                Enrichment = enrichment;
                Scope = scope;
                Surface = surface;
                GradeStageIds = gradeStageIds ?? Array.Empty<string>();
                GradeWeights = gradeWeights;
            }
        }

        static readonly PassMission[] Missions =
        {
            new(0, "Shell + geometry", "visual_shell, geometry_integrity",
                EnrichmentSlot.VisualPolish, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "visual_shell", "geometry_integrity", "enclosure_policy" }, new[] { 30, 24, 14 }),
            new(1, "Mouth seal + ground anchor", "cave_mouth_seal, underground placement",
                EnrichmentSlot.Props, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "cave_mouth_seal", "ground", "packaging_readiness" }, new[] { 22, 10, 12 }),
            new(2, "Walkways + moving platforms", "walk colliders, route probe",
                EnrichmentSlot.MaterialsLighting, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "walkways", "player_floor", "spawn_reachability", "layout_integrity" },
                new[] { 18, 20, 14, 12 }),
            new(3, "Block tunnel + layout", "block tunnel, layout integrity",
                EnrichmentSlot.DecalsDetails, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "block_tunnel", "layout_integrity", "path" }, new[] { 20, 16, 14 }),
            new(4, "Organic mesh polish", "adventure shell, interior ribs",
                EnrichmentSlot.Props, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "organic_mesh", "interior_ribs" }, new[] { 18, 12 }),
            new(5, "Lighting pass", "URP cave lighting",
                EnrichmentSlot.MaterialsLighting, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "lighting", "materials" }, new[] { 24, 14 }),
            new(6, "Fog layout (cave only)", "cave mouth mist — surface stays clear",
                EnrichmentSlot.AtmosphereFog, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "atmosphere", "water" }, new[] { 18, 12 }),
            new(7, "Enemies + mobs", "mob spawn coverage",
                EnrichmentSlot.MobsCombat, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "mob_spawns", "playability" }, new[] { 22, 14 }),
            new(8, "Cave NavMesh", "underground NavMesh walkable",
                EnrichmentSlot.VisualPolish, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "navmesh", "portal" }, new[] { 20, 16 }),
            new(9, "Materials refresh", "URP materials",
                EnrichmentSlot.MaterialsLighting, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "materials", "visual_shell" }, new[] { 18, 12 }),
            new(10, "Performance trim", "XR triangle budget",
                EnrichmentSlot.PerformanceTrim, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "performance", "export_artifacts" }, new[] { 24, 10 }),
            new(11, "Enclosure + ribs", "occlusion shell",
                EnrichmentSlot.DecalsDetails, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "enclosure", "interior_ribs" }, new[] { 16, 14 }),
            new(12, "Interior detail pass", "ribs, decals",
                EnrichmentSlot.Props, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "interior_ribs", "organic_mesh" }, new[] { 14, 12 }),
            new(13, "Audio ambience", "cave ambient beds",
                EnrichmentSlot.AudioAmbience, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "atmosphere" }, new[] { 14 }),
            new(14, "Packaging gate", "underground ship readiness",
                EnrichmentSlot.VisualPolish, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "packaging_readiness", "cave_mouth_seal", "mode_consistency" },
                new[] { 26, 20, 14 }),
            new(15, "Final polish", "commercial production checklist",
                EnrichmentSlot.VisualPolish, WorldScope.CaveOnly, SurfaceTask.None,
                new[] { "visual_shell", "geometry_integrity", "performance" }, new[] { 22, 18, 14 }),
        };

        public static PassMission GetMission(int pass)
        {
            if (pass < 0)
                return Missions[0];
            return Missions[pass % Missions.Length];
        }

        public static EnrichmentSlot EnrichmentForPass(int pass) => GetMission(pass).Enrichment;

        public static bool RunsSurfaceWork(int pass) => false;
    }
}
