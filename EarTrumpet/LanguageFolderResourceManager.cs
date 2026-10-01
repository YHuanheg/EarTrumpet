using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace EarTrumpet
{
    /// <summary>
    /// Loads localized resources out of a single "Language" folder instead of the
    /// one-folder-per-culture layout the runtime insists on.
    ///
    /// Why this exists: the CLR only probes satellites in "&lt;app dir&gt;\&lt;culture&gt;\", and it does
    /// that on a code path that does NOT raise AppDomain.AssemblyResolve - measured, not
    /// assumed: an AssemblyResolve handler that returns the relocated assembly is simply
    /// ignored and the UI falls back to English. Taking over ResourceManager is therefore the
    /// only way to move the folders, and this class is that takeover. It is the one place
    /// where the app leaves the beaten path.
    ///
    /// Two things to know before touching this:
    ///  * "Language" must keep being the folder name that relocate-satellites.ps1 creates.
    ///  * Resources.Designer.cs has to keep constructing this type. It is a generated file: if
    ///    Visual Studio regenerates it (which it does when a .resx is edited) the app silently
    ///    reverts to English. make-package.ps1 runs verify-localization.ps1 to turn that into a
    ///    build failure instead of a quiet regression.
    /// </summary>
    public class LanguageFolderResourceManager : ResourceManager
    {
        /// <summary>Relative to the application directory. See relocate-satellites.ps1.</summary>
        public const string LanguageFolderName = "Language";

        // InternalGetResourceSet is called on every lookup, and overriding it skips the base
        // class's own cache, so hold on to what we load.
        private readonly Dictionary<string, ResourceSet> _loaded = new Dictionary<string, ResourceSet>();
        private readonly object _lock = new object();

        public LanguageFolderResourceManager(string baseName, Assembly assembly) : base(baseName, assembly)
        {
            var directory = (assembly != null && !string.IsNullOrEmpty(assembly.Location))
                ? Path.GetDirectoryName(assembly.Location)
                : AppDomain.CurrentDomain.BaseDirectory;

            LanguageRoot = Path.Combine(directory, LanguageFolderName);
        }

        /// <summary>The folder satellites are expected in. Handy for diagnostics.</summary>
        public string LanguageRoot { get; }

        protected override ResourceSet InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)
        {
            var relocated = TryLoadFromLanguageFolder(culture);
            if (relocated != null)
            {
                return relocated;
            }

            // Not relocated (or no satellite for this culture): fall through to the normal
            // rules, which walk parent cultures and end at the neutral resources. That also
            // means a build that still has the conventional layout keeps working.
            return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
        }

        private ResourceSet TryLoadFromLanguageFolder(CultureInfo culture)
        {
            if (culture == null || string.IsNullOrEmpty(culture.Name))
            {
                return null;
            }

            lock (_lock)
            {
                if (_loaded.TryGetValue(culture.Name, out var cached))
                {
                    return cached;
                }
            }

            try
            {
                var directory = ResolveCultureDirectory(culture.Name);
                var satellitePath = Path.Combine(directory, MainAssembly.GetName().Name + ".resources.dll");
                if (!File.Exists(satellitePath))
                {
                    return null;
                }

                var satellite = Assembly.LoadFrom(satellitePath);

                // Do NOT build the resource name from culture.Name. The compiler names it after
                // the source folder ("EarTrumpet.Properties.Resources.bs-latn-ba.resources")
                // while CultureInfo.Name is the canonical form ("bs-Latn-BA"), and manifest
                // lookups are case sensitive - that mismatch silently fell back to English for
                // bs-latn-ba. Ask the satellite what it actually holds.
                var resourceName = FindResourceName(satellite);
                if (resourceName == null)
                {
                    Trace.WriteLine($"LanguageFolderResourceManager: {satellitePath} has no {BaseName} resources");
                    return null;
                }

                var stream = satellite.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return null;
                }

                var set = new ResourceSet(stream);
                lock (_lock)
                {
                    _loaded[culture.Name] = set;
                }
                return set;
            }
            catch (Exception ex)
            {
                // Never take the app down over a localization problem - fall back to English.
                Trace.WriteLine($"LanguageFolderResourceManager could not load '{culture.Name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Culture folder names come from the .resx file names and need not match the casing
        /// CultureInfo reports. NTFS would resolve it anyway; doing it explicitly also covers
        /// case-sensitive locations such as a network share.
        /// </summary>
        private string ResolveCultureDirectory(string cultureName)
        {
            var exact = Path.Combine(LanguageRoot, cultureName);
            if (Directory.Exists(exact))
            {
                return exact;
            }

            try
            {
                foreach (var directory in Directory.GetDirectories(LanguageRoot))
                {
                    if (string.Equals(Path.GetFileName(directory), cultureName, StringComparison.OrdinalIgnoreCase))
                    {
                        return directory;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"LanguageFolderResourceManager could not list {LanguageRoot}: {ex.Message}");
            }

            return exact;
        }

        private string FindResourceName(Assembly satellite)
        {
            var names = satellite.GetManifestResourceNames();

            foreach (var name in names)
            {
                if (name.StartsWith(BaseName + ".", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            // A satellite carries exactly one resource by construction; take it rather than
            // giving up on a naming oddity.
            return (names.Length == 1) ? names[0] : null;
        }
    }
}
