using System;
using System.Collections.Generic;
using System.Linq;

namespace EarTrumpet.DataModel.Storage.Internal
{
    class NamespacedSettingsBag : ISettingsBag
    {
        public string Namespace { get; }

        public event EventHandler<string> SettingChanged;

        private readonly ISettingsBag _globalBag;

        public NamespacedSettingsBag(string nameSpace, ISettingsBag bag)
        {
            Namespace = nameSpace + ".";
            _globalBag = bag;
        }


        public T Get<T>(string key, T defaultValue)
        {
            return _globalBag.Get($"{Namespace}{key}", defaultValue);
        }

        public bool HasKey(string key)
        {
            return _globalBag.HasKey($"{Namespace}{key}");
        }

        public void Set<T>(string key, T value)
        {
            _globalBag.Set($"{Namespace}{key}", value);
            SettingChanged?.Invoke(this, key);
        }

        public string GetRaw(string key)
        {
            return _globalBag.GetRaw($"{Namespace}{key}");
        }

        public void Remove(string key)
        {
            _globalBag.Remove($"{Namespace}{key}");
            SettingChanged?.Invoke(this, key);
        }

        public IEnumerable<string> GetKeys()
        {
            // The shared bag holds every namespace side by side, so filter ours out and hand
            // back the caller-visible keys (without the prefix).
            return _globalBag.GetKeys()
                .Where(key => key.StartsWith(Namespace, StringComparison.Ordinal))
                .Select(key => key.Substring(Namespace.Length))
                .ToArray();
        }
    }
}