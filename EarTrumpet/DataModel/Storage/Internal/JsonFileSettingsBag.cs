using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace EarTrumpet.DataModel.Storage.Internal
{
    /// <summary>
    /// Keeps every setting in one JSON file next to the application, so the configuration can
    /// travel with it (portable mode). The registry cannot be copied between machines, which is
    /// the whole reason this exists.
    ///
    /// Values are stored as the exact strings the registry backend would hold - plain strings
    /// as-is, everything else through Serializer - so both backends behave identically and
    /// moving an existing install over here is a straight copy rather than a conversion.
    ///
    /// The file is self describing: {"version": 1, "values": { ... }}. A bare top level object
    /// is also accepted, which keeps older/hand-written files readable.
    /// </summary>
    class JsonFileSettingsBag : ISettingsBag
    {
        private const int CurrentFormatVersion = 1;
        private const string VersionName = "version";
        private const string ValuesName = "values";

        private readonly string _path;
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string Namespace => "";

        /// <summary>Where this bag keeps its data. Handy for diagnostics and tests.</summary>
        public string Path => _path;

        public event EventHandler<string> SettingChanged;

        public JsonFileSettingsBag(string path)
        {
            _path = path;
            Load();
        }

        public bool HasKey(string key)
        {
            lock (_lock)
            {
                return _values.ContainsKey(key);
            }
        }

        public T Get<T>(string key, T defaultValue)
        {
            lock (_lock)
            {
                if (!_values.TryGetValue(key, out var data))
                {
                    return defaultValue;
                }

                try
                {
                    if (defaultValue is string)
                    {
                        return (T)(object)data;
                    }

                    if (string.IsNullOrWhiteSpace(data))
                    {
                        return defaultValue;
                    }

                    return Serializer.FromString<T>(data);
                }
                catch (Exception ex)
                {
                    // A value we cannot decode must not take the app down; hand back the default
                    // and leave the (possibly corrupted) data in place for a human to look at.
                    Trace.WriteLine($"JsonFileSettingsBag Get '{key}' failed: {ex.Message}");
                    return defaultValue;
                }
            }
        }

        public void Set<T>(string key, T value)
        {
            var data = value as string ?? Serializer.ToString(key, value);

            if (data == null)
            {
                Remove(key);
                return;
            }

            lock (_lock)
            {
                _values[key] = data;
                Save();
            }

            SettingChanged?.Invoke(this, key);
        }

        public string GetRaw(string key)
        {
            lock (_lock)
            {
                return _values.TryGetValue(key, out var data) ? data : null;
            }
        }

        public void Remove(string key)
        {
            lock (_lock)
            {
                if (_values.Remove(key))
                {
                    Save();
                }
            }

            SettingChanged?.Invoke(this, key);
        }

        public IEnumerable<string> GetKeys()
        {
            lock (_lock)
            {
                return _values.Keys.ToArray();
            }
        }

        /// <summary>
        /// Bulk load, used once when portable mode takes over an existing install. Existing
        /// values win, so this can never clobber something already in the file.
        /// </summary>
        public void ImportAll(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            lock (_lock)
            {
                foreach (var pair in pairs)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null && !_values.ContainsKey(pair.Key))
                    {
                        _values[pair.Key] = pair.Value;
                    }
                }

                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var text = File.ReadAllText(_path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var root = JObject.Parse(text);
                var versionToken = root[VersionName];
                var valuesToken = root[ValuesName] as JObject;

                // Versioned shape needs both parts to be what we expect; anything else is
                // treated as a plain key/value document.
                var isVersioned = versionToken != null
                    && versionToken.Type == JTokenType.Integer
                    && valuesToken != null;

                var values = isVersioned ? valuesToken : root;

                if (isVersioned)
                {
                    var version = versionToken.Value<int>();
                    if (version > CurrentFormatVersion)
                    {
                        Trace.WriteLine($"JsonFileSettingsBag: {_path} was written by a newer version ({version}); reading what we understand");
                    }
                }

                foreach (var property in values.Properties())
                {
                    if (property.Value.Type == JTokenType.String)
                    {
                        _values[property.Name] = (string)property.Value;
                    }
                }

                Trace.WriteLine($"JsonFileSettingsBag loaded {_values.Count} settings from {_path}");
            }
            catch (Exception ex)
            {
                // Starting with empty settings beats refusing to start. The bad file is left
                // alone so it can be inspected or hand-repaired.
                Trace.WriteLine($"JsonFileSettingsBag could not read {_path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Write to a sibling temp file and swap it in. A crash or a power cut then leaves
        /// either the old file or the new one, never a half-written one - which is the main
        /// thing file storage has to get right that the registry gives you for free.
        /// </summary>
        private void Save()
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var payload = new JObject
                {
                    [VersionName] = CurrentFormatVersion,
                    [ValuesName] = JObject.FromObject(_values),
                };

                var temp = _path + ".tmp";
                File.WriteAllText(temp, payload.ToString(Formatting.Indented), new UTF8Encoding(false));

                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, null);
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception ex)
            {
                // Best effort: a failed save must not crash the app, and the in-memory copy
                // still serves the rest of this session.
                Trace.WriteLine($"JsonFileSettingsBag could not write {_path}: {ex.Message}");
            }
        }
    }
}
