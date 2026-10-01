using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace EarTrumpet.DataModel.Storage
{
    public class StorageFactory
    {
        /// <summary>
        /// Dropping this file next to EarTrumpet.exe switches the app to portable settings
        /// (a settings.json in the same folder). It ships inside the portable zip.
        /// </summary>
        private const string PortableMarkerFileName = "portable.txt";

        private const string PortableSettingsFileName = "settings.json";

        private static ISettingsBag s_globalSettings;

        /// <summary>True when settings live next to the application instead of in the registry.</summary>
        public static bool IsPortable { get; private set; }

        /// <summary>Where portable mode keeps its settings; null when not in portable mode.</summary>
        public static string PortableSettingsPath { get; private set; }

        static StorageFactory()
        {
            s_globalSettings = CreateGlobalSettings();
        }

        public static ISettingsBag GetSettings(string nameSpace = null)
        {
            return (nameSpace == null) ? s_globalSettings :
                new Internal.NamespacedSettingsBag(nameSpace, s_globalSettings);
        }

        private static ISettingsBag CreateGlobalSettings()
        {
            // Portable settings only make sense for an unpackaged build: a packaged app installs
            // into a read-only folder and already gets its own store from Windows.
            if (!App.HasIdentity)
            {
                var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var settingsPath = Path.Combine(directory, PortableSettingsFileName);

                // The marker is the normal trigger. An existing settings.json counts too, so a
                // folder that was copied somewhere else keeps working after the marker is lost.
                if (File.Exists(Path.Combine(directory, PortableMarkerFileName)) || File.Exists(settingsPath))
                {
                    var bag = new Internal.JsonFileSettingsBag(settingsPath);
                    Internal.PortableSettings.TakeOverFromRegistry(settingsPath, bag);

                    IsPortable = true;
                    PortableSettingsPath = settingsPath;
                    Trace.WriteLine($"StorageFactory: portable settings in {settingsPath}");
                    return bag;
                }
            }

            IsPortable = false;
            PortableSettingsPath = null;
            return App.HasIdentity ?
                (ISettingsBag)new Internal.WindowsStorageSettingsBag() :
                new Internal.RegistrySettingsBag();
        }
    }
}
