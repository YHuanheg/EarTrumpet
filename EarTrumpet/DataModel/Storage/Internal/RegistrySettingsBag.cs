using Microsoft.Win32;
using System;
using System.Collections.Generic;

namespace EarTrumpet.DataModel.Storage.Internal
{
    class RegistrySettingsBag : ISettingsBag
    {
        private static readonly string s_earTrumpetKey = @"Software\EarTrumpet";

        public string Namespace => "";

        public event EventHandler<string> SettingChanged;

        public bool HasKey(string key)
        {
            using (var regKey = Registry.CurrentUser.CreateSubKey(s_earTrumpetKey, true))
            {
                return regKey.GetValue(key) != null;
            }
        }

        public T Get<T>(string key, T defaultValue)
        {
            if (defaultValue is string)
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
            if (value is string)
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
            return ReadSetting<string>(key, null);
        }

        public void Remove(string key)
        {
            try
            {
                using (var regKey = Registry.CurrentUser.CreateSubKey(s_earTrumpetKey, true))
                {
                    // Not an error if it was never there.
                    regKey.DeleteValue(key, false);
                }
                SettingChanged?.Invoke(this, key);
            }
            catch (Exception)
            {
                // Removing a setting is best effort - never take the app down over it.
            }
        }

        public IEnumerable<string> GetKeys()
        {
            var ret = new List<string>();
            try
            {
                using (var regKey = Registry.CurrentUser.CreateSubKey(s_earTrumpetKey, true))
                {
                    foreach (var name in regKey.GetValueNames())
                    {
                        // An empty name is the registry's per-key default value, not one of ours.
                        if (!string.IsNullOrEmpty(name))
                        {
                            ret.Add(name);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Reading the key list is best effort - a failure here must not take the app down.
            }
            return ret;
        }

        static T ReadSetting<T>(string key, T defaultValue)
        {
            using (var regKey = Registry.CurrentUser.CreateSubKey(s_earTrumpetKey, true))
            {
                T ret = defaultValue;
                try
                {
                    ret = (T)regKey.GetValue(key);
                }
                catch (Exception)
                {
                    ret = defaultValue;
                }
                return ret;
            }
        }

        static void WriteSetting<T>(string key, T value)
        {
            using (var regKey = Registry.CurrentUser.CreateSubKey(s_earTrumpetKey, true))
            {
                regKey.SetValue(key, value);
            }
        }
    }
}
