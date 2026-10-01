using System;

namespace EarTrumpet.DataModel
{
    /// <summary>
    /// One device that has a remembered volume, as shown on the Devices settings page.
    /// A powered-off headset has no live audio object, so the descriptive fields come from
    /// what was persisted the last time we saw it; the "current" fields are only meaningful
    /// while the device is connected.
    /// </summary>
    public class RememberedDevice
    {
        /// <summary>The storage key (the endpoint GUID on its own, see GetStorageKey).</summary>
        public string Key { get; }

        /// <summary>The volume that will be put back on reconnect, in percent.</summary>
        public int Volume { get; }

        /// <summary>Device name, or null when we never got to see one.</summary>
        public string DisplayName { get; }

        /// <summary>Enumerator the endpoint came from (BTHENUM / BTHHFENUM / BTHLEENUM).</summary>
        public string EnumeratorName { get; }

        /// <summary>When the volume was last written, in UTC. Null for entries stored by
        /// builds that did not record this yet.</summary>
        public DateTime? LastRecordedUtc { get; }

        public bool IsConnected { get; }

        /// <summary>Full endpoint id - only available while connected.</summary>
        public string DeviceId { get; }

        /// <summary>Volume the device is actually at right now (equals Volume when offline).</summary>
        public int CurrentVolume { get; }

        public RememberedDevice(
            string key,
            int volume,
            string displayName,
            string enumeratorName,
            DateTime? lastRecordedUtc,
            bool isConnected,
            string deviceId,
            int currentVolume)
        {
            Key = key;
            Volume = volume;
            DisplayName = displayName;
            EnumeratorName = enumeratorName;
            LastRecordedUtc = lastRecordedUtc;
            IsConnected = isConnected;
            DeviceId = deviceId;
            CurrentVolume = currentVolume;
        }
    }
}
