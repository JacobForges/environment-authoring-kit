#if UNITY_EDITOR
using System;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Optional enhancement phases (speed, quality, creative) — not extra queued macro steps.</summary>
    public static class CaveBuildEnhancementCatalog
    {
        public enum Hook
        {
            Preflight,
            OnRequestPrepared,
            AfterResearch,
            AfterCaveShell,
            AfterDemStamp,
            AfterTerrainPhases,
            BeforeWorldPolish,
            OnFinalize,
            PostPipeline,
        }

        public enum Category
        {
            Speed,
            Quality,
            Creative,
        }

        [Serializable]
        public struct PhaseDef
        {
            public string id;
            public string title;
            public Category category;
            public Hook hook;
            public bool reliableFullWorldDefault;
            public int costWeight;
        }

        public const int PhaseCount = 48;

        public static readonly PhaseDef[] All =
        {
            // Speed (1–15)
            P("speed_incremental_off_first", "Incremental ladder off for first full pass", Category.Speed, Hook.Preflight, true, 1),
            P("speed_auto_invoke_off", "Auto-invoke Cursor off during full run", Category.Speed, Hook.Preflight, true, 1),
            P("speed_paced_phased_build", "Phased 120-step queue (not monolithic)", Category.Speed, Hook.Preflight, true, 1),
            P("speed_defer_refresh_geo", "Defer asset refresh during cave geo", Category.Speed, Hook.AfterCaveShell, true, 1),
            P("speed_skip_manifest_rebuild", "Skip duplicate JSON manifest rebuild", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_research_cache_local", "Reuse local ResearchCache + images", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_florida_dem_before_fbm", "Florida DEM stamp before FBM sculpt", Category.Speed, Hook.AfterDemStamp, true, 2),
            P("speed_lazy_validate_materials", "Lazy material upgrade (not on validate)", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_block_ring_batches", "Paced block-ring batches (not one frame)", Category.Speed, Hook.AfterCaveShell, true, 2),
            P("speed_tsx_stream_logs", "Stream tsx stdout to Pipeline Console", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_single_research_prompt", "One consolidated research agent prompt", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_no_navmesh_in_validate", "No implicit NavMesh during validate", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_suppress_mid_autonomous", "Suppress autonomous loop mid-pipeline", Category.Speed, Hook.Preflight, true, 1),
            P("speed_sequential_image_pull_log", "Sequential research image pull + progress", Category.Speed, Hook.AfterResearch, true, 1),
            P("speed_queue_live_status", "Live status + Build X/120 progress labels", Category.Speed, Hook.Preflight, true, 1),

            // Quality (16–30)
            P("quality_dem_supersample_128", "DEM elevation grid supersample → 128", Category.Quality, Hook.AfterDemStamp, true, 2),
            P("quality_dem_supersample_256", "DEM supersample → 256 (heavy)", Category.Quality, Hook.AfterDemStamp, false, 3),
            P("quality_mouth_lidar_carve", "Mouth carve aligned to LiDAR georef", Category.Quality, Hook.AfterCaveShell, true, 2),
            P("quality_navmesh_after_shell", "Surface NavMesh after shell (not validate)", Category.Quality, Hook.AfterTerrainPhases, true, 2),
            P("quality_playability_before_world", "Playability pass before world polish", Category.Quality, Hook.BeforeWorldPolish, true, 2),
            P("quality_grade_after_geo", "Quality grade snapshot after geo", Category.Quality, Hook.AfterCaveShell, true, 2),
            P("quality_completion_contract", "Completion contract JSON at pipeline end", Category.Quality, Hook.PostPipeline, true, 1),
            P("quality_ladder_completion_export", "Ladder completion per seed", Category.Quality, Hook.PostPipeline, true, 1),
            P("quality_research_execution_brief", "Terrain + cave execution briefs", Category.Quality, Hook.AfterResearch, true, 1),
            P("quality_surface_deferral_handoff", "FullWorld LiDAR after cave shell", Category.Quality, Hook.AfterCaveShell, true, 2),
            P("quality_prop_region_lock", "Terrain prop placement lock export", Category.Quality, Hook.AfterTerrainPhases, true, 1),
            P("quality_heightmap_live_commits", "Terrain sculpt live row commits", Category.Quality, Hook.AfterTerrainPhases, true, 2),
            P("quality_route_probe_surface_first", "Surface route bot before cave fixes", Category.Quality, Hook.BeforeWorldPolish, true, 2),
            P("quality_commercial_manifest", "Commercial production manifest gate", Category.Quality, Hook.OnFinalize, true, 2),
            P("quality_preflight_checklist", "Full-run preflight checklist MD", Category.Quality, Hook.Preflight, true, 1),

            // Creative (31–45)
            P("creative_maze_flavor_seed", "Maze flavor rolled from seed (5 layouts)", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_visual_style_roll", "Per-build visual style palette", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_entrance_key_light", "Entrance dramatic key + rim", Category.Creative, Hook.AfterCaveShell, true, 2),
            P("creative_biolum_accent", "Sparse biolum accent point lights", Category.Creative, Hook.AfterCaveShell, true, 2),
            P("creative_karst_prop_boost", "Extra karst/forest prop scatter", Category.Creative, Hook.AfterTerrainPhases, true, 2),
            P("creative_chamber_scale_jitter", "Chamber scale variance from seed", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_fog_mood_style", "Fog density tied to visual style", Category.Creative, Hook.BeforeWorldPolish, true, 1),
            P("creative_trail_moss_tint", "Trail sector moss color shift", Category.Creative, Hook.AfterTerrainPhases, true, 1),
            P("creative_finish_beacon", "Finish goal beacon emissive pulse", Category.Creative, Hook.AfterCaveShell, true, 2),
            P("creative_combat_sector_mix", "Mob spawns varied by path sector", Category.Creative, Hook.AfterCaveShell, true, 1),
            P("creative_opening_sector_bias", "Preferred cave opening sector roll", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_path_yaw_variance", "Extra spline yaw variance", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_prop_emphasis_karst", "Prop emphasis: karst + canopy", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_torch_warmth", "Route torch warmth from style", Category.Creative, Hook.AfterCaveShell, true, 1),
            P("creative_post_color_mood", "Post polish color mood pass", Category.Creative, Hook.OnFinalize, true, 2),
            P("creative_tomb_raider_labyrinth", "Tomb Raider walkway → labyrinth → cavern", Category.Creative, Hook.OnRequestPrepared, true, 2),
            P("creative_asymmetric_surface_tiles", "Asymmetric multi-tile surface (non-square)", Category.Creative, Hook.OnRequestPrepared, true, 1),
            P("creative_mountain_trail_ridges", "Twin peaks + flank ascent/descent trails", Category.Creative, Hook.AfterTerrainPhases, true, 2),
        };

        static PhaseDef P(
            string id,
            string title,
            Category cat,
            Hook hook,
            bool reliableDefault,
            int cost) =>
            new()
            {
                id = id,
                title = title,
                category = cat,
                hook = hook,
                reliableFullWorldDefault = reliableDefault,
                costWeight = cost,
            };

        public static PhaseDef? Find(string id)
        {
            foreach (var p in All)
            {
                if (p.id == id)
                    return p;
            }

            return null;
        }
    }
}
#endif
