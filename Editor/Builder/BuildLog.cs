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
