using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Post-geometry pass: seal ring gaps and report enclosure coverage.</summary>
    public static class CaveSeamlessQualityPass
    {
        const float JunctionSpacing = 2.35f;
        const float FloorModuleSpan = 5f;

        public static CaveSeamlessQualityReport Run(
            Transform caveRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplinePath spline)
        {
            var report = new CaveSeamlessQualityReport();
            if (caveRoot == null || catalog == null || spline == null)
                return report;

            report.JunctionPatches = SealJunctions(caveRoot, catalog, rng, spline);
            report.TunnelRingCount = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Tunnel_Ring_") || t.name.StartsWith("Tunnel_Entrance_Bridge"))
                    report.TunnelRingCount++;
            }

            CountEnclosure(caveRoot, report);
            return report;
        }

        static int SealJunctions(
            Transform caveRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplinePath spline)
        {
            var tunnelRoot = caveRoot.Find("Tunnels");
            if (tunnelRoot == null)
                return 0;

            var dims = new CaveSeamlessTunnelBuilder.TunnelDimensions
            {
                Width = CavePathFactory.MinInterior + 0.5f,
                Height = CavePathFactory.MinInterior,
                NavClearance = 2.2f
            };

            var patches = 0;
            for (var dist = JunctionSpacing * 0.5f; dist < spline.TotalLength; dist += JunctionSpacing)
            {
                var sample = spline.SampleAtDistance(dist);
                var patchScale = new Vector3(dims.Width / FloorModuleSpan * 0.55f, 1f, 0.45f);
                if (CavePrefabScatter.PlaceModule(tunnelRoot, catalog.Pick(catalog.Floors, rng),
                        sample.Position + sample.Up * 0.02f, Quaternion.LookRotation(sample.Tangent, sample.Up),
                        patchScale, "Junction_Floor", false))
                    patches++;

                if (CavePrefabScatter.PlaceModule(tunnelRoot, catalog.Pick(catalog.Ceilings, rng),
                        sample.Position + sample.Up * dims.Height,
                        Quaternion.LookRotation(sample.Tangent, sample.Up),
                        Vector3.Scale(patchScale, new Vector3(1.02f, 1f, 1.02f)), "Junction_Ceiling", false))
                    patches++;
            }

            return patches;
        }

        static void CountEnclosure(Transform caveRoot, CaveSeamlessQualityReport report)
        {
            var floor = 0;
            var ceiling = 0;
            var wall = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n.Contains("Floor") || n.Contains("SM_Floor"))
                    floor++;
                if (n.Contains("Ceiling") || n.Contains("SM_Ceiling") || n.Contains("Cupola"))
                    ceiling++;
                if (n.Contains("Wall") || n.Contains("SM_Wall"))
                    wall++;
            }

            report.FloorPieces = floor;
            report.CeilingPieces = ceiling;
            report.WallPieces = wall;
            report.EnclosureScore = Mathf.Clamp01((floor + ceiling + wall) / 120f);
        }

        public static string FormatReport(CaveSeamlessQualityReport report)
        {
            var sb = new StringBuilder();
            sb.Append($"rings≈{report.TunnelRingCount}, floors={report.FloorPieces}, ");
            sb.Append($"ceilings={report.CeilingPieces}, walls={report.WallPieces}, ");
            sb.Append($"junction patches={report.JunctionPatches}, enclosure={report.EnclosureScore:P0}.");
            return sb.ToString();
        }
    }

    public sealed class CaveSeamlessQualityReport
    {
        public int TunnelRingCount;
        public int FloorPieces;
        public int CeilingPieces;
        public int WallPieces;
        public int JunctionPatches;
        public float EnclosureScore;
    }
}
