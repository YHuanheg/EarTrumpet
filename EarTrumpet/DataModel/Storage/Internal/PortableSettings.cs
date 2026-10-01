using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace EarTrumpet.DataModel.Storage.Internal
{
    /// <summary>
    /// One-time handover from the machine-wide settings to the portable ones.
    ///
    /// Only runs when portable mode starts with no settings file of its own. The registry
    /// values are deliberately left where they are: deleting settings.json puts the user back
    /// on them, which is the documented way to undo portable mode.
    /// </summary>
    internal static class PortableSettings
    {
        public static void TakeOverFromRegistry(string settingsPath, JsonFileSettingsBag destination)
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    // The file is the user's configuration now; never merge over it.
                    return;
                }

                var registry = new RegistrySettingsBag();
                var keys = registry.GetKeys().ToArray();
                if (keys.Length == 0)
                {
                    return;
                }

                var pairs = new List<KeyValuePair<string, string>>(keys.Length);
                foreach (var key in keys)
                {
                    // The raw stored form: identical in both back ends, so the copy cannot lose
                    // or reinterpret anything.
                    var raw = registry.GetRaw(key);
                    if (raw != null)
                    {
                        pairs.Add(new KeyValuePair<string, string>(key, raw));
                    }
                }

                destination.ImportAll(pairs);
                Trace.WriteLine($"PortableSettings: copied {pairs.Count} settings from the registry to {settingsPath}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"PortableSettings TakeOverFromRegistry failed: {ex.Message}");
            }
        }
    }
}
