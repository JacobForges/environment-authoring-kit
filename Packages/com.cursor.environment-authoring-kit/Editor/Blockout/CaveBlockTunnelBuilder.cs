using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Minecraft-style block shells along the spline with jittered transforms for organic cave walls/floor/ceiling.
    /// </summary>
    public static class CaveBlockTunnelBuilder
    {
        public const string BlockTunnelRootName = "BlockTunnel";

        public struct Settings
        {
            public float BlockSize;
            public float RingSpacing;
            public float InteriorHollow;
            public int AngularSteps;
            public int FloorLayers;
            public int CeilingLayers;
            public int WallThickness;
            public float MorphPosition;
            public float MorphRotation;
            public float MorphScaleMin;
            public float MorphScaleMax;
            public bool OuterWallMinable;
            /// <summary>Grid maze compact route: four cardinal walls per ring (NVIDIA 3D-GENERALIST 2026 layout band).</summary>
            public bool CompactCardinalShell;

            public static Settings Default => new()
            {
                BlockSize = 1.15f,
                RingSpacing = 2.8f,
                InteriorHollow = 0.44f,
                AngularSteps = 12,
                FloorLayers = 1,
                CeilingLayers = 1,
                WallThickness = 1,
                MorphPosition = 0.14f,
                MorphRotation = 9f,
                MorphScaleMin = 0.88f,
                MorphScaleMax = 1.14f,
                OuterWallMinable = false
            };
        }

        public static int Build(
            Transform cavesRoot,
            CaveSplinePath spline,
            Material rockMaterial,
            int seed,
            Settings settings = default,
            string sectionName = "Main")
        {
            if (cavesRoot == null || spline == null || spline.KnotCount < 2 || rockMaterial == null)
                return 0;

            if (settings.BlockSize <= 0f)
                settings = Settings.Default;

            var root = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, BlockTunnelRootName);
            if (string.IsNullOrEmpty(sectionName))
                sectionName = "Main";

            var section = root.Find(sectionName);
            if (section == null)
            {
                var sectionGo = new GameObject(sectionName);
                CaveEditorUndo.RegisterCreated(sectionGo, "Block Tunnel Section");
                sectionGo.transform.SetParent(root, false);
                section = sectionGo.transform;
            }

            ClearChildren(section);

            var placedTotal = 0;
            var rng = new System.Random(seed);
            var ringCount = Mathf.Max(2, Mathf.CeilToInt(spline.TotalLength / settings.RingSpacing));

            for (var ring = 0; ring < ringCount; ring++)
            {
                var dist = ringCount <= 1 ? 0f : (ring / (float)(ringCount - 1)) * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var ringRoot = new GameObject($"BlockRing_{ring:D3}");
                CaveEditorUndo.RegisterCreated(ringRoot, "Block Ring");
                ringRoot.transform.SetParent(section, false);
                ringRoot.transform.localPosition = Vector3.zero;
                ringRoot.transform.localRotation = Quaternion.identity;

                placedTotal += FillRing(ringRoot.transform, sample, rockMaterial, rng, settings);
            }

            if (placedTotal > 0)
                CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(cavesRoot);

            return placedTotal;
        }

        static int FillRing(
            Transform ringRoot,
            CaveSplineSample sample,
            Material rockMaterial,
            System.Random rng,
            Settings settings)
        {
            var placed = 0;
            var block = settings.BlockSize;
            var rx = sample.RadiusX;
            var ry = sample.RadiusY;
            var hollowX = rx * settings.InteriorHollow;
            var hollowY = ry * settings.InteriorHollow;
            var floorBase = sample.Position - sample.Up * (ry * 0.82f);

            for (var layer = 0; layer < settings.FloorLayers; layer++)
            {
                for (var a = 0; a < settings.AngularSteps; a++)
                {
                    var angle = a / (float)settings.AngularSteps * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);
                    var norm = Mathf.Sqrt((cos * cos) / (rx * rx) + (sin * sin) / (ry * ry));
                    if (norm < 1f / Mathf.Max(rx, ry))
                        continue;

                    var offset = sample.Right * (cos * rx * 0.92f) + sample.Up * (sin * ry * 0.35f);
                    var pos = floorBase + offset + sample.Up * (layer * block * 0.95f);
                    if (IsInsideHollow(offset, sample, hollowX, hollowY))
                        continue;

                    placed += PlaceBlock(ringRoot, pos, sample, rockMaterial, rng, settings, minable: false);
                }
            }

            for (var layer = 0; layer < settings.CeilingLayers; layer++)
            {
                for (var a = 0; a < settings.AngularSteps; a++)
                {
                    var angle = a / (float)settings.AngularSteps * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);
                    if (sin < 0.15f)
                        continue;

                    var offset = sample.Right * (cos * rx * 0.95f) + sample.Up * (sin * ry * 0.95f);
                    var pos = sample.Position + offset - sample.Up * (layer * block * 0.9f);
                    if (IsInsideHollow(offset, sample, hollowX, hollowY))
                        continue;

                    placed += PlaceBlock(ringRoot, pos, sample, rockMaterial, rng, settings, minable: false);
                }
            }

            for (var wall = 0; wall < settings.WallThickness; wall++)
            {
                var wallRx = rx * (0.72f + wall * 0.12f);
                var wallRy = ry * (0.72f + wall * 0.12f);

                for (var a = 0; a < settings.AngularSteps; a++)
                {
                    var angle = a / (float)settings.AngularSteps * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);
                    if (Mathf.Abs(sin) < 0.35f)
                        continue;

                    var offset = sample.Right * (cos * wallRx) + sample.Up * (sin * wallRy);
                    if (IsInsideHollow(offset, sample, hollowX, hollowY))
                        continue;

                    var heightSteps = Mathf.Max(2, Mathf.RoundToInt(ry * 1.1f / block));
                    for (var h = 0; h < heightSteps; h++)
                    {
                        var pos = sample.Position + offset - sample.Up * (ry * 0.75f) + sample.Up * (h * block);
                        var minable = settings.OuterWallMinable && wall == settings.WallThickness - 1 && h > 0;
                        placed += PlaceBlock(ringRoot, pos, sample, rockMaterial, rng, settings, minable);
                    }
                }
            }

            return placed;
        }

        static bool IsInsideHollow(Vector3 offset, CaveSplineSample sample, float hollowX, float hollowY)
        {
            if (hollowX < 0.01f || hollowY < 0.01f)
                return false;

            var alongRight = Vector3.Dot(offset, sample.Right);
            var alongUp = Vector3.Dot(offset, sample.Up);
            var nx = alongRight / hollowX;
            var ny = alongUp / hollowY;
            return nx * nx + ny * ny < 1f;
        }

        static int PlaceBlock(
            Transform parent,
            Vector3 localPos,
            CaveSplineSample sample,
            Material rockMaterial,
            System.Random rng,
            Settings settings,
            bool minable)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Cave Block");
            go.name = minable ? "CaveBlock_Minable" : "CaveBlock_Shell";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(sample.Tangent, sample.Up);

            ApplyMorph(go.transform, settings, rng);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = rockMaterial;

            if (go.GetComponent<CaveTunnelBlock>() == null)
                go.AddComponent<CaveTunnelBlock>();

            if (minable)
            {
                go.tag = CaveTags.Minable;
                if (go.GetComponent<MinableRock>() == null)
                {
                    var rock = go.AddComponent<MinableRock>();
                    rock.hitPoints = 4;
                }
            }

            go.isStatic = true;
            return 1;
        }

        static void ApplyMorph(Transform t, Settings settings, System.Random rng)
        {
            var mp = settings.MorphPosition;
            t.localPosition += new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * mp,
                (float)(rng.NextDouble() * 2 - 1) * mp,
                (float)(rng.NextDouble() * 2 - 1) * mp);

            t.localRotation *= Quaternion.Euler(
                (float)(rng.NextDouble() * 2 - 1) * settings.MorphRotation,
                (float)(rng.NextDouble() * 2 - 1) * settings.MorphRotation * 1.2f,
                (float)(rng.NextDouble() * 2 - 1) * settings.MorphRotation);

            var s = settings.BlockSize * Mathf.Lerp(
                settings.MorphScaleMin,
                settings.MorphScaleMax,
                (float)rng.NextDouble());
            t.localScale = Vector3.one * s;
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
