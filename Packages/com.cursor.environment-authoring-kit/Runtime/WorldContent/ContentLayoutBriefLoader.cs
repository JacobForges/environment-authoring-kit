using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    public static class ContentLayoutBriefLoader
    {
        public const string BriefRelativePath = "Assets/EnvironmentKit/Generated/CaveBuildContentLayoutBrief.json";

        public static bool TryLoad(out ContentLayoutBriefFile file)
        {
            file = null;
            var path = Path.Combine(Application.dataPath, "EnvironmentKit/Generated/CaveBuildContentLayoutBrief.json");
            if (!File.Exists(path))
                return false;

            var raw = File.ReadAllText(path);
            var json = SanitizeForJsonUtility(raw);
            file = JsonUtility.FromJson<ContentLayoutBriefFile>(json);
            if (file?.contentPlan == null)
                return false;

            NormalizeDialog(file);
            return true;
        }

        static string SanitizeForJsonUtility(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;
            var s = json;
            s = Regex.Replace(s, "\"nextId\"\\s*:\\s*null", "\"nextId\": \"\"");
            s = Regex.Replace(s, "\"shop\"\\s*:\\s*null", "\"shop\": {\"currency\":\"gold\",\"items\":[]}");
            return s;
        }

        static void NormalizeDialog(ContentLayoutBriefFile file)
        {
            var npcs = file.contentPlan?.npcs;
            if (npcs == null)
                return;
            foreach (var n in npcs)
            {
                if (n?.dialog == null)
                    continue;
                if (n.dialog.shop != null && (n.dialog.shop.items == null || n.dialog.shop.items.Length == 0)
                    && string.IsNullOrEmpty(n.dialog.shop.currency))
                    n.dialog.shop = null;
            }
        }

        public static ContentNpcDef FindNpc(ContentLayoutBriefFile file, string id)
        {
            if (file?.contentPlan?.npcs == null || string.IsNullOrEmpty(id))
                return null;
            foreach (var n in file.contentPlan.npcs)
                if (n.id == id)
                    return n;
            return null;
        }
    }
}
