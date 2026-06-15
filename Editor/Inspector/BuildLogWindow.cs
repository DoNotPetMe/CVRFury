using UnityEditor;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Shows the messages from the most recent CVRFury bake. Opened automatically when
    /// a build produces warnings or errors, or manually from the CVRFury menu.</summary>
    internal sealed class BuildLogWindow : EditorWindow
    {
        private static BuildLog _last;
        private Vector2 _scroll;

        public static void Publish(BuildLog log)
        {
            _last = log;
            if (log.HasErrors || HasWarnings(log))
                ShowWindow();
        }

        /// <summary>Mirrors a block of text (already shown in the CVRFury window's own log area) into
        /// the persistent "Show Last Build Log" view, so the menu item is never empty after a run.</summary>
        public static void PublishText(string text, bool isError)
        {
            var log = new BuildLog();
            log.Record(isError ? BuildLog.Level.Error : BuildLog.Level.Info,
                       string.IsNullOrEmpty(text) ? "(no output)" : text);
            _last = log;
        }

        [MenuItem("Tools/CVRFury/Show Last Build Log")]
        public static void ShowWindow()
        {
            var w = GetWindow<BuildLogWindow>("CVRFury Build Log");
            w.minSize = new Vector2(420, 240);
            w.Show();
        }

        private static bool HasWarnings(BuildLog log)
        {
            foreach (var e in log.Entries)
                if (e.Level == BuildLog.Level.Warning) return true;
            return false;
        }

        private void OnGUI()
        {
            if (_last == null)
            {
                EditorGUILayout.HelpBox("No CVRFury build has run yet this session.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var e in _last.Entries)
            {
                var type = e.Level switch
                {
                    BuildLog.Level.Error => MessageType.Error,
                    BuildLog.Level.Warning => MessageType.Warning,
                    _ => MessageType.None,
                };
                EditorGUILayout.HelpBox(e.Message, type);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
