using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Simple fullscreen subtitle during world cinematics.</summary>
    public sealed class WorldCinematicSubtitleOverlay : MonoBehaviour
    {
        static WorldCinematicSubtitleOverlay _instance;
        static string _line;

        public static void Show(string line)
        {
            Ensure();
            _line = line ?? string.Empty;
        }

        public static void Hide() => _line = null;

        static void Ensure()
        {
            if (_instance != null)
                return;

            var go = new GameObject("WorldCinematicSubtitles");
            _instance = go.AddComponent<WorldCinematicSubtitleOverlay>();
            DontDestroyOnLoad(go);
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(_line))
                return;

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            style.normal.textColor = Color.white;
            var w = Screen.width * 0.7f;
            var h = 80f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.82f, w, h), _line, style);
        }
    }
}
