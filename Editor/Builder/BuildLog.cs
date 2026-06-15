using System.Collections.Generic;
using UnityEngine;

namespace CVRFury.Builder
{
    /// <summary>Collects per-build messages so they can be echoed to the console and shown in
    /// the CVRFury build window.</summary>
    internal sealed class BuildLog
    {
        public enum Level { Info, Warning, Error }

        public readonly struct Entry
        {
            public readonly Level Level;
            public readonly string Message;
            public Entry(Level level, string message) { Level = level; Message = message; }
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public bool HasErrors { get; private set; }

        public void Info(string m) => Add(Level.Info, m);
        public void Warning(string m) => Add(Level.Warning, m);

        public void Error(string m)
        {
            HasErrors = true;
            Add(Level.Error, m);
        }

        /// <summary>Records a message WITHOUT echoing it to the console. Used when mirroring text
        /// that has already been surfaced elsewhere (e.g. the CVRFury window's own log area) into the
        /// persistent "Show Last Build Log" view, so we don't double-spam the console.</summary>
        public void Record(Level level, string message)
        {
            if (level == Level.Error) HasErrors = true;
            Entries.Add(new Entry(level, message));
        }

        private void Add(Level level, string message)
        {
            Entries.Add(new Entry(level, message));
            var line = $"[CVRFury] {message}";
            switch (level)
            {
                case Level.Warning: Debug.LogWarning(line); break;
                case Level.Error: Debug.LogError(line); break;
                default:
                    if (CVRFurySettings.VerboseLogging) Debug.Log(line);
                    break;
            }
        }
    }
}
