using Microsoft.Win32;
using System;
using System.IO;

namespace EarTrumpet.Interop.Helpers
{
    /// <summary>
    /// The "run at startup" mechanism used when EarTrumpet has no MSIX identity: a value
    /// under HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// Kept separate from <see cref="StartupHelper"/> so the registry key can be pointed at
    /// a scratch location and exercised without touching the real startup list.
    /// </summary>
    class RunKeyStartupRegistration
    {
        public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultValueName = "EarTrumpet";

        private readonly string _keyPath;
        private readonly string _valueName;

        public RunKeyStartupRegistration(string keyPath, string valueName)
        {
            _keyPath = keyPath;
            _valueName = valueName;
        }

        public static RunKeyStartupRegistration CreateDefault()
        {
            return new RunKeyStartupRegistration(DefaultKeyPath, DefaultValueName);
        }

        /// <summary>
        /// True when the value exists and launches the given executable. A value that points
        /// somewhere else (for example a stale path from an older install) counts as not
        /// registered, so that turning the setting on repairs it.
        /// </summary>
        public bool IsRegisteredFor(string executablePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(_keyPath, false))
            {
                return SameExecutable(key?.GetValue(_valueName) as string, executablePath);
            }
        }

        public void Register(string executablePath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(_keyPath, true))
            {
                // Quoted so that paths with spaces are launched as a single command line.
                key.SetValue(_valueName, $"\"{executablePath}\"");
            }
        }

        public void Unregister()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(_keyPath, true))
            {
                if (key?.GetValue(_valueName) != null)
                {
                    key.DeleteValue(_valueName, false);
                }
            }
        }

        /// <summary>
        /// Compares a Run value ("C:\path\app.exe" or unquoted) with an executable path,
        /// ignoring quoting, whitespace and surrounding case.
        /// </summary>
        public static bool SameExecutable(string commandLineValue, string executablePath)
        {
            if (string.IsNullOrWhiteSpace(commandLineValue) || string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            var value = commandLineValue.Trim().Trim('"').Trim();
            var expected = executablePath.Trim().Trim('"').Trim();

            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Be forgiving about the extension and about 8.3/long path differences, both of
            // which are common when another tool (installer, Scoop) wrote the value.
            try
            {
                return string.Equals(
                    Path.GetFullPath(value),
                    Path.GetFullPath(expected),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Full path of the running executable, with a fallback to the managed entry
        /// assembly when the process image cannot be queried.
        /// </summary>
        public static string GetExecutablePath()
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var fileName = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        return fileName;
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                return System.Reflection.Assembly.GetEntryAssembly()?.Location;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override string ToString()
        {
            return $"{_keyPath}\\{_valueName}";
        }
    }
}
