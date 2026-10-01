using EarTrumpet.Diagnosis;
using System;
using System.Collections.Generic;
using Windows.Management.Core;
using Windows.Storage;

namespace EarTrumpet.DataModel.Storage.Internal
{
    class WindowsStorageSettingsBag : ISettingsBag
    {
        private static readonly ApplicationData _appDataManager = ApplicationDataManager.CreateForPackageFamily(App.PackageName);

        public string Namespace => "";
        public event EventHandler<string> SettingChanged;

        public bool HasKey(string key)
        {
            var ret = false;
            try
            {
                ret = _appDataManager.LocalSettings.Values.ContainsKey(key);
            }
            catch (Exception ex)
            {
                ErrorReporter.LogWarning(ex);
            }
            return ret;
        }

        public T Get<T>(string key, T defaultValue)
        {
            if (!HasKey(key))
            {
                return defaultValue;
            }

            if (defaultValue is bool || defaultValue is string)
            {
                return ReadSetting<T>(key, defaultValue);
            }

            var data = ReadSetting<string>(key, null);
            if (string.IsNullOrWhiteSpace(data))
            {
                return defaultValue;
            }

            return Serializer.FromString<T>(data);
        }

        public void Set<T>(string key, T value)
        {
            if (value is bool || value is string)
            {
                WriteSetting<T>(key, value);
            }
            else
            {
                WriteSetting(key, Serializer.ToString(key, value));
            }

            SettingChanged?.Invoke(this, key);
        }

        public string GetRaw(string key)
        {
            // ReadSetting<string> hands back whatever the value holds, whatever its type, or null.
            return HasKey(key) ? ReadSetting<string>(key, null) : null;
        }

        public void Remove(string key)
        {
            try
            {
                // Not an error if it was never there.
                _appDataManager.LocalSettings.Values.Remove(key);
                SettingChanged?.Invoke(this, key);
            }
            catch (Exception ex)
            {
                // Removing a setting is best effort - never take the app down over it.
                ErrorReporter.LogWarning(ex);
            }
        }

        public IEnumerable<string> GetKeys()
        {
            var ret = new List<string>();
            try
            {
                foreach (var key in _appDataManager.LocalSettings.Values.Keys)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        ret.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                // Reading the key list is best effort - a failure here must not take the app down.
                ErrorReporter.LogWarning(ex);
            }
            return ret;
        }

        static T ReadSetting<T>(string key, T defaultValue)
        {
            T ret = defaultValue;
            try
            {
                ret = (T)_appDataManager.LocalSettings.Values[key];
            }
            catch (Exception ex)
            {
                ErrorReporter.LogWarning(ex);
            }
            return ret;
        }

        static void WriteSetting<T>(string key, T value)
        {
            try
            {
                _appDataManager.LocalSettings.Values[key] = value;
            }
            catch (Exception ex)
            {
                ErrorReporter.LogWarning(ex);
            }
        }
    }
}
