using System.Collections.Generic;

namespace CVRFury.Builder
{
    /// <summary>
    /// Allocates unique, sanitised synced-parameter (machine) names for a build. Pure logic with no
    /// Unity dependencies, so it is unit-testable on its own.
    /// </summary>
    internal sealed class ParamNameAllocator
    {
        private readonly HashSet<string> _used = new HashSet<string>();
        private int _counter;

        public string Allocate(string desired)
        {
            var baseName = Sanitize(desired);
            if (string.IsNullOrEmpty(baseName))
                baseName = $"CVRFury_{_counter++}";

            var name = baseName;
            var i = 1;
            while (!_used.Add(name))
                name = $"{baseName}_{i++}";
            return name;
        }

        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            return new string(chars);
        }
    }
}
