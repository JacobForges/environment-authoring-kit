#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Append-only heal checkpoint database under Generated/HealCheckpointDb.
    /// Ledger is append-only (WORM); index.json tracks last good head for rollback.
    /// </summary>
    public static class CaveBuildHealCheckpointStore
    {
        public const string DbRootRel = "Assets/EnvironmentKit/Generated/HealCheckpointDb";
        const string LedgerFileName = "ledger.jsonl";
        const string IndexFileName = "index.json";

        static readonly string[] ArtifactRelPaths =
        {
            "Assets/EnvironmentKit/Generated/SurfaceTerrainGridManifest.json",
            "Assets/EnvironmentKit/Generated/SurfaceTerrainBuildLadderReport.json",
            "Assets/EnvironmentKit/Generated/SurfaceTerrainQualityReport.json",
            "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json",
            "Assets/EnvironmentKit/Generated/WorldLayoutAudit.json",
            "Assets/EnvironmentKit/Generated/ActiveGenerationStyle.json",
            "Assets/EnvironmentKit/Generated/CaveBuildPreBuildLadderReport.json",
        };

        [Serializable]
        public class IndexDto
        {
            public string schemaVersion = "1";
            public string lastGoodCheckpointId = "";
            public string headCheckpointId = "";
            public int totalCheckpoints;
            public int totalGoodPromotions;
            public string lastRestoreUtc = "";
            public string lastGoodMilestone = "";
            public int lastGoodScore;
            public string baselineCheckpointId = "";
        }

        [Serializable]
        class LedgerEventDto
        {
            public string eventType;
            public string utc;
            public string checkpointId;
            public string milestone;
            public int seed;
            public int score;
            public string letterGrade;
            public bool buildAcceptable;
            public string note;
            public string aiProvider;
        }

        [Serializable]
        class TerrainSnapshotMeta
        {
            public string name;
            public float posX;
            public float posY;
            public float posZ;
            public float sizeX;
            public float sizeY;
            public float sizeZ;
            public int resolution;
            public string heightFile;
        }

        [Serializable]
        class CheckpointManifest
        {
            public string checkpointId;
            public string milestone;
            public int seed;
            public int score;
            public string letterGrade;
            public bool buildAcceptable;
            public string capturedUtc;
            public string aiProvider;
            public TerrainSnapshotMeta[] terrains;
            public string caveRootName;
            public float cavePosX;
            public float cavePosY;
            public float cavePosZ;
        }

        public static string DbRootAbs => Path.GetFullPath(DbRootRel);

        public static IndexDto LoadIndex()
        {
            EnsureDbRoot();
            var path = Path.Combine(DbRootAbs, IndexFileName);
            if (!File.Exists(path))
                return new IndexDto();

            try
            {
                return JsonUtility.FromJson<IndexDto>(File.ReadAllText(path)) ?? new IndexDto();
            }
            catch
            {
                return new IndexDto();
            }
        }

        static void SaveIndex(IndexDto index)
        {
            EnsureDbRoot();
            var path = Path.Combine(DbRootAbs, IndexFileName);
            File.WriteAllText(path, JsonUtility.ToJson(index, true), Encoding.UTF8);
            AssetDatabase.Refresh();
        }

        static void AppendLedger(LedgerEventDto evt)
        {
            EnsureDbRoot();
            var line = JsonUtility.ToJson(evt);
            File.AppendAllText(Path.Combine(DbRootAbs, LedgerFileName), line + "\n", Encoding.UTF8);
        }

        static void EnsureDbRoot()
        {
            if (!Directory.Exists(DbRootAbs))
                Directory.CreateDirectory(DbRootAbs);
        }

        public static string Capture(
            string milestone,
            int seed,
            int score,
            string letterGrade,
            bool buildAcceptable,
            Terrain mainTerrain,
            Transform caveRoot,
            string note = null)
        {
            if (string.IsNullOrEmpty(milestone))
                milestone = "unknown";

            EnsureDbRoot();
            var id = $"cp_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}".Substring(0, 40);
            var cpDir = Path.Combine(DbRootAbs, id);
            Directory.CreateDirectory(cpDir);
            Directory.CreateDirectory(Path.Combine(cpDir, "artifacts"));
            Directory.CreateDirectory(Path.Combine(cpDir, "terrains"));

            var provider = CaveBuildCursorSettings.ResolveActiveProvider().ToString();
            var terrainMetas = CaptureTerrains(mainTerrain, cpDir);
            CaptureArtifacts(cpDir);
            CaptureCaveRoot(caveRoot, out var caveName, out var cavePos);

            var manifest = new CheckpointManifest
            {
                checkpointId = id,
                milestone = milestone,
                seed = seed,
                score = score,
                letterGrade = letterGrade ?? "",
                buildAcceptable = buildAcceptable,
                capturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                aiProvider = provider,
                terrains = terrainMetas.ToArray(),
                caveRootName = caveName ?? "",
                cavePosX = cavePos.x,
                cavePosY = cavePos.y,
                cavePosZ = cavePos.z,
            };
            File.WriteAllText(
                Path.Combine(cpDir, "manifest.json"),
                JsonUtility.ToJson(manifest, true),
                Encoding.UTF8);

            var index = LoadIndex();
            index.headCheckpointId = id;
            index.totalCheckpoints++;
            if (string.IsNullOrEmpty(index.baselineCheckpointId))
                index.baselineCheckpointId = id;
            SaveIndex(index);

            AppendLedger(new LedgerEventDto
            {
                eventType = "capture",
                utc = manifest.capturedUtc,
                checkpointId = id,
                milestone = milestone,
                seed = seed,
                score = score,
                letterGrade = letterGrade,
                buildAcceptable = buildAcceptable,
                note = note ?? "",
                aiProvider = provider,
            });

            CaveBuildEditorLog.LogSurface(
                $"[HealCheckpoint] Captured {id} — {milestone} score={score} ({letterGrade}).",
                forceUnityConsole: true);
            return id;
        }

        public static void PromoteGood(string checkpointId, string reason)
        {
            if (string.IsNullOrEmpty(checkpointId))
                return;

            var index = LoadIndex();
            index.lastGoodCheckpointId = checkpointId;
            index.lastGoodMilestone = reason ?? "";
            index.totalGoodPromotions++;
            var cpDir = Path.Combine(DbRootAbs, checkpointId);
            var manifestPath = Path.Combine(cpDir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = JsonUtility.FromJson<CheckpointManifest>(File.ReadAllText(manifestPath));
                    if (manifest != null)
                        index.lastGoodScore = manifest.score;
                }
                catch
                {
                    // ignore parse errors on promote
                }
            }
            SaveIndex(index);

            AppendLedger(new LedgerEventDto
            {
                eventType = "promote_good",
                utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                checkpointId = checkpointId,
                note = reason ?? "",
                aiProvider = CaveBuildCursorSettings.ResolveActiveProvider().ToString(),
            });

            CaveBuildEditorLog.LogSurface(
                $"[HealCheckpoint] Promoted GOOD backup → {checkpointId} ({reason}).",
                forceUnityConsole: true);
        }

        public static bool TryRestoreBestAvailable(Terrain mainTerrain, out string message)
        {
            message = string.Empty;
            var index = LoadIndex();
            string checkpointId = null;

            if (!string.IsNullOrEmpty(index.lastGoodCheckpointId))
                checkpointId = index.lastGoodCheckpointId;
            else if (!string.IsNullOrEmpty(index.baselineCheckpointId))
                checkpointId = index.baselineCheckpointId;
            else if (!string.IsNullOrEmpty(index.headCheckpointId))
                checkpointId = index.headCheckpointId;

            if (string.IsNullOrEmpty(checkpointId))
            {
                message = "No checkpoint to restore (run a build to capture session baseline).";
                return false;
            }

            if (!TryRestoreCheckpoint(checkpointId, mainTerrain, out message))
                return false;

            var prefix = checkpointId == index.lastGoodCheckpointId
                ? "good"
                : checkpointId == index.baselineCheckpointId
                    ? "baseline"
                    : "head";
            message = $"[{prefix}] {message}";

            index.lastRestoreUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveIndex(index);

            AppendLedger(new LedgerEventDto
            {
                eventType = "restore",
                utc = index.lastRestoreUtc,
                checkpointId = checkpointId,
                note = message,
                aiProvider = CaveBuildCursorSettings.ResolveActiveProvider().ToString(),
            });
            return true;
        }

        public static bool TryRestoreLastGood(Terrain mainTerrain, out string message) =>
            TryRestoreBestAvailable(mainTerrain, out message);

        public static bool TryRestoreCheckpoint(string checkpointId, Terrain mainTerrain, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrEmpty(checkpointId))
            {
                message = "Empty checkpoint id.";
                return false;
            }

            var cpDir = Path.Combine(DbRootAbs, checkpointId);
            var manifestPath = Path.Combine(cpDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                message = $"Checkpoint missing: {checkpointId}";
                return false;
            }

            CheckpointManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<CheckpointManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                message = "Manifest parse failed: " + ex.Message;
                return false;
            }

            if (manifest?.terrains == null || manifest.terrains.Length == 0)
            {
                message = "Checkpoint has no terrain snapshots.";
                return false;
            }

            var restored = 0;
            foreach (var meta in manifest.terrains)
            {
                if (meta == null || string.IsNullOrEmpty(meta.name))
                    continue;

                var tile = FindTerrainByName(meta.name);
                if (tile?.terrainData == null)
                    continue;

                var heightPath = Path.Combine(cpDir, meta.heightFile ?? "");
                if (!File.Exists(heightPath))
                    continue;

                var bytes = File.ReadAllBytes(heightPath);
                var expected = meta.resolution * meta.resolution * sizeof(float);
                if (bytes.Length < expected)
                    continue;

                var heights = new float[meta.resolution, meta.resolution];
                Buffer.BlockCopy(bytes, 0, heights, 0, expected);
                tile.transform.position = new Vector3(meta.posX, meta.posY, meta.posZ);
                CaveEditorUndo.RecordObject(tile.terrainData, "Heal checkpoint restore");
                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(tile, 0, 0, heights, requestDelayLod: false);
                tile.Flush();
                restored++;
            }

            RestoreArtifacts(cpDir);

            if (!string.IsNullOrEmpty(manifest.caveRootName))
            {
                var cave = GameObject.Find(manifest.caveRootName);
                if (cave != null)
                {
                    cave.transform.position = new Vector3(manifest.cavePosX, manifest.cavePosY, manifest.cavePosZ);
                }
            }

            if (mainTerrain != null)
            {
                RefreshTerrainConnectivity(mainTerrain);
            }

            message = $"Restored checkpoint {checkpointId} ({manifest.milestone}) — {restored} terrain(s).";
            CaveBuildEditorLog.LogSurface("[HealCheckpoint] " + message, forceUnityConsole: true);
            return restored > 0;
        }

        static List<TerrainSnapshotMeta> CaptureTerrains(Terrain mainTerrain, string cpDir)
        {
            var list = new List<TerrainSnapshotMeta>();
            if (mainTerrain == null)
                return list;

            var tiles = CollectAllTiles(mainTerrain);
            foreach (var tile in tiles)
            {
                if (tile?.terrainData == null)
                    continue;

                var data = tile.terrainData;
                var res = data.heightmapResolution;
                var heights = data.GetHeights(0, 0, res, res);
                var bytes = new byte[res * res * sizeof(float)];
                Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
                var safeName = SanitizeFileName(tile.name);
                var rel = Path.Combine("terrains", safeName + ".height");
                File.WriteAllBytes(Path.Combine(cpDir, rel), bytes);

                var pos = tile.transform.position;
                var size = data.size;
                list.Add(new TerrainSnapshotMeta
                {
                    name = tile.name,
                    posX = pos.x,
                    posY = pos.y,
                    posZ = pos.z,
                    sizeX = size.x,
                    sizeY = size.y,
                    sizeZ = size.z,
                    resolution = res,
                    heightFile = rel.Replace('\\', '/'),
                });
            }

            return list;
        }

        static Terrain[] CollectAllTiles(Terrain mainTerrain)
        {
            var list = new List<Terrain> { mainTerrain };
            list.AddRange(SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain));
            list.AddRange(SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain));
            return list.ToArray();
        }

        static void CaptureArtifacts(string cpDir)
        {
            var artDir = Path.Combine(cpDir, "artifacts");
            foreach (var rel in ArtifactRelPaths)
            {
                var abs = Path.GetFullPath(rel);
                if (!File.Exists(abs))
                    continue;

                var name = Path.GetFileName(rel);
                File.Copy(abs, Path.Combine(artDir, name), overwrite: true);
            }
        }

        static void RestoreArtifacts(string cpDir)
        {
            var artDir = Path.Combine(cpDir, "artifacts");
            if (!Directory.Exists(artDir))
                return;

            foreach (var src in Directory.GetFiles(artDir))
            {
                var name = Path.GetFileName(src);
                var destRel = ArtifactRelPaths.Length > 0
                    ? Array.Find(ArtifactRelPaths, p => p.EndsWith(name, StringComparison.Ordinal))
                    : null;
                if (string.IsNullOrEmpty(destRel))
                    continue;

                File.Copy(src, Path.GetFullPath(destRel), overwrite: true);
            }
        }

        static void CaptureCaveRoot(Transform caveRoot, out string name, out Vector3 pos)
        {
            if (caveRoot == null)
            {
                name = "";
                pos = Vector3.zero;
                return;
            }

            name = caveRoot.name;
            pos = caveRoot.position;
        }

        static Terrain FindTerrainByName(string name)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (t != null && t.name == name)
                    return t;
            }

            return null;
        }

        static void RefreshTerrainConnectivity(Terrain mainTerrain)
        {
            SurfaceTerrainTileExpansion.RefreshMountainTerrainConnectivity(mainTerrain);
        }

        static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
#endif
