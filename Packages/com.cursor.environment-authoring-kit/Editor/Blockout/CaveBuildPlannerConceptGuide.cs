#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Planner concept PNG is the primary layout authority (75% weight); markers + specs are 25%.
    /// Mirrors <c>planner_concept_render.py</c> iso projection for pixel sampling.
    /// </summary>
    public static class CaveBuildPlannerConceptGuide
    {
        public const float DefaultConceptWeight = 0.75f;
        public const float MarkerPlanWeight = 0.25f;
        public const float PassSimilarityPercent = 89f;
        public const int MaxFidelityRetries = 3;

        const int ImageWidth = 1536;
        const int ImageHeight = 1024;
        const float TileStepFloating = 1.55f;
        const float IsoScale = 38f;

        public enum PlannerZone
        {
            Unknown = 0,
            Void = 1,
            PlayPlatform = 2,
            PlayPlateau = 3,
            Island = 4,
            Trail = 5,
            HopPad = 6,
            Prop = 7,
            Npc = 8,
            Enemy = 9,
            Spawn = 10,
            Water = 11,
        }

        static Texture2D _texture;
        static Texture2D _densityTexture;
        static string _conceptRel;
        static string _densityRel;
        static bool _active;
        static float _conceptWeight = DefaultConceptWeight;
        static CaveBuildPlannerLayoutBridge.TechnicalSpecs _specs;
        static CaveBuildPlannerLayoutBridge.LayoutPlan _plan;

        public static bool IsActive => _active && _texture != null;
        public static float ActiveConceptWeight => _conceptWeight;
        public static string LoadedConceptRel => _conceptRel;
        public static string LoadedConceptDensityRel => _densityRel;

        public static void ClearSession()
        {
            if (_texture != null)
                UnityEngine.Object.DestroyImmediate(_texture);
            if (_densityTexture != null)
                UnityEngine.Object.DestroyImmediate(_densityTexture);
            _texture = null;
            _densityTexture = null;
            _conceptRel = null;
            _densityRel = null;
            _active = false;
            _conceptWeight = DefaultConceptWeight;
            _specs = null;
            _plan = null;
            CaveBuildPlannerApprovedAssets.ClearSession();
        }

        public static bool TryBind(WorldGenerationRequest request)
        {
            ClearSession();

            if (!CaveBuildSessionConfig.HasFinalizedActive ||
                request == null ||
                !CaveBuildSessionConfig.IsSessionRequest(request))
                return false;

            if (!CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out _) ||
                brief?.layoutPlan == null)
                return false;

            if (!CaveBuildPlannerLayoutBridge.TryResolveConceptImagePath(out var abs, out var rel))
            {
                CaveBuildEditorLog.LogSurface(
                    "[PlannerConcept] No concept PNG on disk — marker plan only (25% effective). " +
                    "Finalize planner session to generate concept.png.",
                    forceUnityConsole: true);
                return false;
            }

            _texture = LoadPng(abs);
            if (_texture == null)
                return false;

            _conceptRel = rel;
            if (CaveBuildPlannerLayoutBridge.TryResolveConceptDensityImagePath(out var densityAbs, out var densityRel))
            {
                _densityTexture = LoadPng(densityAbs);
                _densityRel = densityRel;
            }

            _plan = brief.layoutPlan;
            _specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(_plan);
            _active = true;
            _conceptWeight = DefaultConceptWeight;
            CaveBuildPlannerApprovedAssets.EnsureLoaded();

            var densityNote = _densityTexture != null
                ? $" + density `{_densityRel}` (trail/prop overlay)"
                : string.Empty;
            CaveBuildEditorLog.LogSurface(
                $"[PlannerConcept] Bound concept image — {rel} ({_texture.width}×{_texture.height}) " +
                $"at {_conceptWeight:P0} weight (marker plan {MarkerPlanWeight:P0}).{densityNote}",
                forceUnityConsole: true);
            return true;
        }

        public static void BoostWeightForRetry(int attemptIndex)
        {
            _conceptWeight = Mathf.Clamp(DefaultConceptWeight + attemptIndex * 0.05f, 0.75f, 0.90f);
            CaveBuildEditorLog.LogSurface(
                $"[PlannerConcept] Retry boost — concept weight now {_conceptWeight:P0}.",
                forceUnityConsole: true);
        }

        public static float BlendHeightNorm(float markerNorm, float conceptNorm) =>
            Mathf.Clamp01(markerNorm * MarkerPlanWeight + conceptNorm * _conceptWeight);

        public static bool TrySampleExpectedZone(
            Vector3 world,
            Terrain main,
            SceneGroundInfo ground,
            out PlannerZone zone,
            out float conceptConfidence)
        {
            zone = PlannerZone.Unknown;
            conceptConfidence = 0f;

            var markerZone = SampleMarkerZone(world, main, ground);
            if (!IsActive)
            {
                zone = markerZone;
                conceptConfidence = 0.25f;
                return markerZone != PlannerZone.Unknown;
            }

            var conceptZone = SampleConceptPixel(world, main, ground, out conceptConfidence);
            if (_densityTexture != null &&
                SampleDensityPixel(world, main, ground, out var densityZone, out var densityConf) &&
                densityZone is PlannerZone.Trail or PlannerZone.Prop &&
                densityConf >= 0.72f)
            {
                zone = densityZone;
                conceptConfidence = densityConf;
                return true;
            }

            if (conceptZone == PlannerZone.Unknown && markerZone == PlannerZone.Unknown)
                return false;

            if (conceptZone == PlannerZone.Unknown)
            {
                zone = markerZone;
                return true;
            }

            if (markerZone == PlannerZone.Unknown || conceptConfidence >= 0.55f)
            {
                zone = conceptZone;
                return true;
            }

            zone = conceptZone;
            return true;
        }

        public static float ConceptTargetHeightNorm(
            Vector3 world,
            Terrain terrain,
            Terrain main,
            SceneGroundInfo ground,
            float markerNorm) =>
            ConceptTargetHeightNormInternal(world, terrain, main, ground, markerNorm, bulk: false);

        /// <summary>Heightmap bulk path — grid + concept PNG only (no per-pixel marker hit tests).</summary>
        public static float ConceptTargetHeightNormBulk(
            Vector3 world,
            Terrain terrain,
            Terrain main,
            SceneGroundInfo ground,
            float markerNorm) =>
            ConceptTargetHeightNormInternal(world, terrain, main, ground, markerNorm, bulk: true);

        static float ConceptTargetHeightNormInternal(
            Vector3 world,
            Terrain terrain,
            Terrain main,
            SceneGroundInfo ground,
            float markerNorm,
            bool bulk)
        {
            PlannerZone zone;
            if (bulk)
            {
                var conceptZone = IsActive
                    ? SampleConceptPixel(world, main, ground, out var conf)
                    : PlannerZone.Unknown;
                var gridZone = ZoneFromPlayGridOffset(world, main, ground);
                if (conceptZone == PlannerZone.Unknown && gridZone == PlannerZone.Unknown)
                    return markerNorm;
                if (conceptZone == PlannerZone.Unknown)
                    zone = gridZone;
                else if (gridZone == PlannerZone.Unknown)
                    zone = conceptZone;
                else
                    zone = conceptZone;
            }
            else if (!TrySampleExpectedZone(world, main, ground, out zone, out _))
            {
                return markerNorm;
            }

            var hubY = ground != null && ground.HasAnchor
                ? SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y
                : main.transform.position.y;
            var sizeY = terrain?.terrainData?.size.y ?? 600f;
            var originY = terrain != null ? terrain.transform.position.y : hubY;

            float worldY = zone switch
            {
                PlannerZone.Island => hubY + (_specs?.plateauHeightM ?? 35f),
                PlannerZone.PlayPlateau => hubY + (_specs?.plateauHeightM ?? 35f),
                PlannerZone.PlayPlatform => hubY + (_specs?.platformHeightM ?? 12f),
                PlannerZone.HopPad => hubY + (_specs?.platformHeightM ?? 12f) + 0.55f,
                PlannerZone.Trail => hubY + (_specs?.platformHeightM ?? 12f) + 0.35f,
                PlannerZone.Void => hubY - 8f,
                _ => hubY + (_specs?.platformHeightM ?? 12f),
            };

            var conceptNorm = Mathf.Clamp01((worldY - originY) / Mathf.Max(sizeY, 0.01f));
            return BlendHeightNorm(markerNorm, conceptNorm);
        }

        static PlannerZone ZoneFromPlayGridOffset(Vector3 world, Terrain main, SceneGroundInfo ground)
        {
            if (!TryPlayGridOffset(world, main, ground, out var off))
                return PlannerZone.Void;

            if (off == new Vector2Int(0, 2) || off == new Vector2Int(2, 0) ||
                off == new Vector2Int(0, -2) || off == new Vector2Int(-2, 0))
                return PlannerZone.Island;

            if (!SurfaceTerrainTileExpansion.IsNineTileGameplayOffset(off))
                return PlannerZone.Void;

            var row = 1 - off.y;
            var col = off.x + 1;
            return CaveBuildPlannerLayoutBridge.IsCornerCell(row, col)
                ? PlannerZone.PlayPlateau
                : PlannerZone.PlayPlatform;
        }

        static PlannerZone SampleMarkerZone(Vector3 world, Terrain main, SceneGroundInfo ground)
        {
            if (_plan?.markers == null || main?.terrainData == null)
                return PlannerZone.Unknown;

            var hubY = ground != null && ground.HasAnchor
                ? SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y
                : main.transform.position.y;
            var tileSize = main.terrainData.size;
            var specs = _specs ?? new CaveBuildPlannerLayoutBridge.TechnicalSpecs();

            const float hitRadius = 2.2f;
            foreach (var m in _plan.markers)
            {
                if (m == null)
                    continue;

                Vector3 pos;
                if (string.Equals(m.zone, "island", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CaveBuildPlannerLayoutAuthor.TryResolveIslandMarkerWorld(
                            main, m, specs, hubY, tileSize, out pos))
                        continue;
                }
                else if (!CaveBuildPlannerLayoutAuthor.TryResolvePlayMarkerWorld(
                             main, ground, m, specs, hubY, tileSize, out pos))
                    continue;

                if (Vector3.Distance(new Vector3(world.x, 0f, world.z), new Vector3(pos.x, 0f, pos.z)) > hitRadius)
                    continue;

                var kind = m.kind ?? string.Empty;
                if (string.Equals(kind, "spawn", StringComparison.OrdinalIgnoreCase))
                    return PlannerZone.Spawn;
                if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
                    return PlannerZone.Npc;
                if (string.Equals(kind, "enemy", StringComparison.OrdinalIgnoreCase))
                    return PlannerZone.Enemy;
                if (CaveBuildPlannerLayoutBridge.IsPlatformMarker(m))
                    return PlannerZone.HopPad;
                if (CaveBuildPlannerLayoutBridge.IsHubDressingMarker(m) ||
                    CaveBuildPlannerLayoutBridge.IsIslandJunctionMarker(m))
                    return PlannerZone.Prop;
                return PlannerZone.Prop;
            }

            if (!TryPlayGridOffset(world, main, ground, out var off))
                return PlannerZone.Void;

            if (off == new Vector2Int(0, 2) || off == new Vector2Int(2, 0) ||
                off == new Vector2Int(0, -2) || off == new Vector2Int(-2, 0))
                return PlannerZone.Island;

            if (!SurfaceTerrainTileExpansion.IsNineTileGameplayOffset(off))
                return PlannerZone.Void;

            var row = 1 - off.y;
            var col = off.x + 1;
            return CaveBuildPlannerLayoutBridge.IsCornerCell(row, col)
                ? PlannerZone.PlayPlateau
                : PlannerZone.PlayPlatform;
        }

        static PlannerZone SampleConceptPixel(
            Vector3 world,
            Terrain main,
            SceneGroundInfo ground,
            out float confidence)
        {
            confidence = 0f;
            if (_texture == null || main?.terrainData == null)
                return PlannerZone.Unknown;

            if (!TryWorldToConceptPixel(world, main, ground, out var px, out var py))
                return PlannerZone.Unknown;

            var c = _texture.GetPixelBilinear(px, py);
            return ClassifyConceptColor(c, out confidence);
        }

        static bool SampleDensityPixel(
            Vector3 world,
            Terrain main,
            SceneGroundInfo ground,
            out PlannerZone zone,
            out float confidence)
        {
            zone = PlannerZone.Unknown;
            confidence = 0f;
            if (_densityTexture == null || main?.terrainData == null)
                return false;

            if (!TryWorldToConceptPixel(world, main, ground, out var px, out var py))
                return false;

            var c = _densityTexture.GetPixelBilinear(px, py);
            zone = ClassifyConceptColor(c, out confidence);
            return zone != PlannerZone.Unknown;
        }

        static PlannerZone ClassifyConceptColor(Color c, out float confidence)
        {
            confidence = 0.5f;
            var r = (int)(c.r * 255f);
            var g = (int)(c.g * 255f);
            var b = (int)(c.b * 255f);

            if (IsNear(r, g, b, 90, 220, 130, 55))
            {
                confidence = 0.92f;
                return PlannerZone.Spawn;
            }

            if (IsNear(r, g, b, 184, 148, 88, 45) || IsNear(r, g, b, 210, 165, 90, 50))
            {
                confidence = 0.88f;
                return PlannerZone.Trail;
            }

            if (IsNear(r, g, b, 148, 142, 132, 40) || IsNear(r, g, b, 110, 105, 98, 40))
            {
                confidence = 0.85f;
                return PlannerZone.HopPad;
            }

            if (IsNear(r, g, b, 100, 170, 255, 50))
            {
                confidence = 0.9f;
                return PlannerZone.Npc;
            }

            if (IsNear(r, g, b, 230, 90, 90, 55))
            {
                confidence = 0.9f;
                return PlannerZone.Enemy;
            }

            if (IsNear(r, g, b, 210, 180, 90, 50))
            {
                confidence = 0.82f;
                return PlannerZone.Prop;
            }

            if (IsNear(r, g, b, 118, 168, 95, 45) || IsNear(r, g, b, 28, 70, 42, 40))
            {
                confidence = 0.8f;
                return PlannerZone.PlayPlateau;
            }

            if (IsNear(r, g, b, 96, 145, 78, 45) || IsNear(r, g, b, 62, 98, 52, 40))
            {
                confidence = 0.78f;
                return PlannerZone.PlayPlatform;
            }

            if (IsNear(r, g, b, 58, 118, 175, 45))
            {
                confidence = 0.75f;
                return PlannerZone.Water;
            }

            if (c.grayscale < 0.22f)
            {
                confidence = 0.7f;
                return PlannerZone.Void;
            }

            return PlannerZone.Unknown;
        }

        static bool IsNear(int r, int g, int b, int tr, int tg, int tb, int tol) =>
            Math.Abs(r - tr) <= tol && Math.Abs(g - tg) <= tol && Math.Abs(b - tb) <= tol;

        static bool TryWorldToConceptPixel(
            Vector3 world,
            Terrain main,
            SceneGroundInfo ground,
            out float u,
            out float v)
        {
            u = 0f;
            v = 0f;
            if (!TryPlayGridOffset(world, main, ground, out var off))
                return false;

            var tileSize = main.terrainData.size;
            var center = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main);
            var localX = (world.x - center.x) / Mathf.Max(tileSize.x, 1f);
            var localZ = (world.z - center.z) / Mathf.Max(tileSize.z, 1f);

            var gx = off.x * TileStepFloating + localX * 0.35f;
            var gy = off.y * TileStepFloating + localZ * 0.35f;

            var sceneRect = new Rect(252f, 36f, ImageWidth - 268f - 252f, ImageHeight - 28f - 36f);
            var ox = sceneRect.x + sceneRect.width * 0.5f;
            var oy = sceneRect.y + sceneRect.height * 0.58f;

            var sx = ox + (gx - gy) * IsoScale * 0.92f;
            var sy = oy + (gx + gy) * IsoScale * 0.46f;

            u = sx / ImageWidth;
            v = 1f - sy / ImageHeight;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        static bool TryPlayGridOffset(
            Vector3 world,
            Terrain main,
            SceneGroundInfo ground,
            out Vector2Int off)
        {
            off = Vector2Int.zero;
            if (main?.terrainData == null)
                return false;

            var hub = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main);
            var tileSize = main.terrainData.size;
            var dx = world.x - hub.x;
            var dz = world.z - hub.z;
            var tx = Mathf.RoundToInt(dx / tileSize.x);
            var tz = Mathf.RoundToInt(dz / tileSize.z);
            off = new Vector2Int(tx, tz);
            return true;
        }

        static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return tex.LoadImage(bytes) ? tex : null;
        }
    }
}
#endif
