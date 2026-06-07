using System.IO;
using System.Text;
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class CaveBuildGradingManifestWriter
    {
        public static void Write()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Generated"))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"version\": \"{CaveBuildQualitySystem.GradingVersion}\",");
            sb.AppendLine($"  \"gradingStandard\": \"{CaveBuildQualityRubric.GradingStandard}\",");
            sb.AppendLine($"  \"targetGrade\": \"{CaveBuildQualityRubric.TargetGrade}\",");
            sb.AppendLine($"  \"betaGrade\": \"{CaveBuildQualityRubric.BetaGrade}\",");
            sb.AppendLine($"  \"targetScore\": {CaveBuildQualityRubric.ShipScore},");
            sb.AppendLine($"  \"betaScore\": {CaveBuildQualityRubric.BetaScore},");
            sb.AppendLine($"  \"reportPath\": \"Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json\",");
            sb.AppendLine("  \"stages\": [");
            var defs = CaveBuildQualityRubric.StageDefinitions;
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{d.Id}\",");
                sb.AppendLine($"      \"name\": \"{d.Name}\",");
                sb.AppendLine($"      \"weight\": {d.Weight},");
                sb.AppendLine($"      \"passScore\": {d.PassScore},");
                sb.AppendLine($"      \"floorScore\": {d.FloorScore},");
                sb.AppendLine($"      \"critical\": {(d.Critical ? "true" : "false")},");
                sb.AppendLine($"      \"waivedLayoutPrototype\": {(d.WaivedLayoutPrototype ? "true" : "false")},");
                sb.AppendLine($"      \"fullBuildOnly\": {(d.FullBuildOnly ? "true" : "false")}");
                sb.Append(i < defs.Length - 1 ? "    }," : "    }");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(CaveBuildQualitySystem.ManifestPath, sb.ToString());
            CaveBuildDeferredAssetRefresh.RequestRefresh();
        }
    }
}
