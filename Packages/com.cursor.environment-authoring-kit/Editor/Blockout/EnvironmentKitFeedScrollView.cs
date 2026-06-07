#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Scrollable feed that can pin to the bottom so the newest lines stay visible.</summary>
    static class EnvironmentKitFeedScrollView
    {
        static GUIStyle _feedStyle;

        static GUIStyle FeedStyle
        {
            get
            {
                if (_feedStyle != null)
                    return _feedStyle;

                _feedStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    richText = false,
                };
                _feedStyle.padding = new RectOffset(6, 6, 4, 4);
                return _feedStyle;
            }
        }

        /// <param name="stickToBottom">When true, scroll position jumps to newest line (bottom).</param>
        public static void Draw(ref Vector2 scroll, string text, float viewHeight, bool stickToBottom)
        {
            if (string.IsNullOrEmpty(text))
                text = "(waiting for activity)";

            var viewWidth = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 36f);
            var contentHeight = FeedStyle.CalcHeight(new GUIContent(text), viewWidth);
            contentHeight = Mathf.Max(contentHeight, viewHeight + 1f);

            scroll = EditorGUILayout.BeginScrollView(
                scroll,
                false,
                true,
                GUILayout.Height(viewHeight),
                GUILayout.ExpandWidth(true));

            EditorGUILayout.SelectableLabel(
                text,
                FeedStyle,
                GUILayout.MinHeight(contentHeight),
                GUILayout.ExpandWidth(true));

            EditorGUILayout.EndScrollView();

            if (stickToBottom)
                scroll.y = Mathf.Max(0f, contentHeight - viewHeight);
        }
    }
}
#endif
