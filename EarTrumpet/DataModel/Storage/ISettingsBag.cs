using System;
using System.Collections.Generic;

namespace EarTrumpet.DataModel.Storage
{
    public interface ISettingsBag
    {
        string Namespace { get; }
        bool HasKey(string key);
        T Get<T>(string key, T defaultValue);
        void Set<T>(string key, T value);

        /// <summary>
        /// The value exactly as it is stored, or null when the key is absent. Both back ends use
        /// the same encoding, which makes this the right way to copy settings between them.
        ///
        /// Prefer this over Get&lt;string&gt;(key, null): a null default fails the "is this a
        /// string setting" test inside Get, which then tries to XML-decode the value instead of
        /// handing it back.
        /// </summary>
        string GetRaw(string key);

        /// <summary>
        /// Drops a key. Removing a key that is not there is not an error - callers use this to
        /// say "make sure nothing is stored", not "delete this exact thing".
        /// </summary>
        void Remove(string key);

        /// <summary>
        /// Every key currently stored in this bag, with the bag's own namespace prefix removed.
        /// Features that keep one entry per device/app need this: without it there is no way to
        /// discover what was stored earlier, only to read a key you already know about.
        /// </summary>
        IEnumerable<string> GetKeys();

        event EventHandler<string> SettingChanged;
    }
}
