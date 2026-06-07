#if UNITY_EDITOR
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Pre-authors Timeline assets + PlayableDirector bindings for world cinematic triggers.</summary>
    public static class WorldCinematicTimelineAuthor
    {
        public const string TimelineFolder = "Assets/EnvironmentKit/Cinematics/Timelines";

        static readonly (string id, float duration, WorldSurfaceBiomeId biome)[] Definitions =
        {
            ("intro_guide", 7f, WorldSurfaceBiomeId.PlayKarst),
            ("boss_portal", 8f, WorldSurfaceBiomeId.BossThreshold),
            ("biome_foothill", 6f, WorldSurfaceBiomeId.FoothillGreen),
            ("biome_peak", 6f, WorldSurfaceBiomeId.PeakStone),
            ("biome_annex", 6f, WorldSurfaceBiomeId.AnnexLabyrinth),
        };

        [MenuItem("Window/Environment Kit/World/Build Cinematic Timelines")]
        public static void BuildFromMenu() => BindAllTriggersInScene();

        public static void BindAllTriggersInScene()
        {
            EnsureFolder(TimelineFolder);
            var built = 0;

            foreach (var def in Definitions)
            {
                var timeline = EnsureTimelineAsset(def.id, def.duration);
                if (timeline == null)
                    continue;
                built++;
            }

            var triggers = Object.FindObjectsByType<WorldCinematicTrigger>(FindObjectsInactive.Include);
            foreach (var trigger in triggers)
            {
                if (trigger == null)
                    continue;

                var id = string.IsNullOrEmpty(trigger.cinematicId)
                    ? trigger.gameObject.name
                    : trigger.cinematicId;
                var timeline = LoadTimeline(id);
                if (timeline == null)
                    continue;

                BindDirector(trigger.gameObject, timeline);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Cinematic] Built {built} timeline(s) and bound {triggers.Length} trigger(s).");
        }

        static TimelineAsset EnsureTimelineAsset(string cinematicId, float duration)
        {
            var path = $"{TimelineFolder}/Cinematic_{cinematicId}.playable";
            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (existing != null)
                return existing;

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = $"Cinematic_{cinematicId}";

            var track = timeline.CreateTrack<PlayableTrack>(null, "WorldCinematic");
            var clip = track.CreateClip<WorldCinematicPlayableAsset>();
            clip.displayName = cinematicId;
            clip.duration = duration;
            clip.start = 0;

            if (clip.asset is WorldCinematicPlayableAsset playable)
            {
                var so = new SerializedObject(playable);
                so.FindProperty("cinematicId").stringValue = cinematicId;
                so.FindProperty("durationSeconds").floatValue = duration;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.CreateAsset(timeline, path);
            return timeline;
        }

        static TimelineAsset LoadTimeline(string cinematicId)
        {
            var path = $"{TimelineFolder}/Cinematic_{cinematicId}.playable";
            return AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
        }

        static void BindDirector(GameObject host, TimelineAsset timeline)
        {
            var director = host.GetComponent<PlayableDirector>();
            if (director == null)
                director = host.AddComponent<PlayableDirector>();

            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;
            EditorUtility.SetDirty(host);
        }

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            var parts = assetPath.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
