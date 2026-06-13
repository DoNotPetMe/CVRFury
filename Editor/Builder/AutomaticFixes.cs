using System.Collections.Generic;

namespace CVRFury.Builder
{
    /// <summary>
    /// Post-bake hygiene pass over the avatar's Advanced Avatar Settings. It removes clearly-broken
    /// entries (no machine name) and warns about duplicate synced parameter names, which would
    /// otherwise silently collide in-game. It never renames entries: a machine name is also the
    /// animator parameter name, so renaming here would break the link the builders just created.
    /// </summary>
    internal static class AutomaticFixes
    {
        public static void Run(BuildContext ctx)
        {
            var list = ctx.Avatar?.SettingsList;
            if (list == null) return;

            var seen = new HashSet<string>();
            var removed = 0;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];
                var machine = Reflect.GetField(entry, CckNames.Entry_MachineName) as string;

                if (string.IsNullOrEmpty(machine))
                {
                    list.RemoveAt(i);
                    removed++;
                    continue;
                }
            }

            // Forward pass for duplicate detection (keeps message order stable).
            foreach (var entry in list)
            {
                var machine = Reflect.GetField(entry, CckNames.Entry_MachineName) as string;
                if (!string.IsNullOrEmpty(machine) && !seen.Add(machine))
                    ctx.Log.Warning($"Duplicate synced parameter '{machine}' in Advanced Avatar Settings. " +
                                    "Two controls share a name and will fight in-game — give one a unique " +
                                    "parameter name.");
            }

            if (removed > 0)
                ctx.Log.Info($"Automatic fixes removed {removed} Advanced Avatar Setting(s) with no parameter name.");
        }
    }
}
