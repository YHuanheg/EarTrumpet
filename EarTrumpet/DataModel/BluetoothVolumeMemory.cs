using EarTrumpet.DataModel.Audio;
using EarTrumpet.DataModel.Storage;
using EarTrumpet.DataModel.WindowsAudio;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Timers;
using System.Windows.Threading;

namespace EarTrumpet.DataModel
{
    /// <summary>
    /// Remembers the volume of Bluetooth audio endpoints and puts it back when the same
    /// endpoint comes back. Bluetooth headsets often come back at whatever level the
    /// Bluetooth stack decided on (usually far too loud), because the endpoint is torn down
    /// on disconnect and re-created on reconnect, so the level the user set is lost.
    /// </summary>
    public class BluetoothVolumeMemory
    {
        private const string SettingsNamespace = "BluetoothVolumeMemory";

        /// <summary>
        /// Suffix of the companion entry holding the device's name and last-recorded time.
        /// It is a separate entry so the volume entry stays a plain int that is read on every
        /// reconnect and must never fail to parse. The '!' cannot occur inside a device key
        /// (GetStorageKey rewrites it), so the two kinds can never be confused.
        /// </summary>
        private const string InfoSuffix = "!info";

        /// <summary>
        /// Format generation of a stored device record. Reading never depends on it
        /// (XmlSerializer ignores unknown and missing members), but writing it means a future
        /// change can tell "recorded before field X existed" from "X is legitimately empty",
        /// which is otherwise indistinguishable.
        /// </summary>
        private const int CurrentInfoVersion = 1;

        // Never remember a volume below this. Some Bluetooth stacks drive the endpoint to 0
        // while it disconnects, and restoring that would hand the user a silent headset with
        // no visible cause. A value of 0 is therefore treated as "not a user choice".
        private const int MinRememberedVolume = 1;
        private const int MaxRememberedVolume = 100;

        // Dragging a slider raises one notification per step; coalesce them into one write.
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        private readonly IAudioDeviceManager _deviceManager;
        private readonly AppSettings _settings;
        private readonly ISettingsBag _settingsBag = StorageFactory.GetSettings(SettingsNamespace);
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Timer _saveTimer = new Timer(SaveDelay.TotalMilliseconds) { AutoReset = false };
        private readonly Dictionary<string, IAudioDevice> _attachedDevices = new Dictionary<string, IAudioDevice>();
        private readonly Dictionary<string, int> _pendingVolumes = new Dictionary<string, int>();

        /// <summary>
        /// Raised when the stored records change, or when a device connects/disconnects (which
        /// changes how the settings page describes it). May come from a non-UI thread, because
        /// the device collection is fed by the audio manager, so UI handlers must marshal.
        /// </summary>
        public event EventHandler RecordsChanged;

        /// <summary>
        /// Raised after a remembered volume was put back on a device that just reconnected, so
        /// the shell can tell the user what happened. Not raised for devices that were already
        /// connected when the app started: nothing reconnected there, and an overlay popping up
        /// on every launch would be noise.
        /// </summary>
        public event EventHandler<VolumeRestoredEventArgs> VolumeRestored;

        public BluetoothVolumeMemory(IAudioDeviceManager deviceManager, AppSettings settings)
        {
            _deviceManager = deviceManager;
            _settings = settings;

            _saveTimer.Elapsed += OnSaveTimerElapsed;
            _deviceManager.Devices.CollectionChanged += OnDevicesChanged;

            PruneUnusableRecords();

            foreach (var device in _deviceManager.Devices.ToArray())
            {
                Attach(device);
                // Not announced: these were already connected before we started.
                RestoreIfRemembered(device, announce: false);
            }
        }

        /// <summary>
        /// What we keep next to the volume, so the settings page can still describe a device
        /// that is not connected at the moment. Public because the settings store serializes it
        /// by type; all strings so it round-trips through both back ends unchanged.
        /// </summary>
        public class StoredDeviceInfo
        {
            /// <summary>See CurrentInfoVersion.</summary>
            public int Version { get; set; }

            public string Name { get; set; }
            public string Enumerator { get; set; }

            /// <summary>ISO 8601 round-trip format, invariant culture.</summary>
            public string LastRecordedUtc { get; set; }
        }

        /// <summary>
        /// While this is off the feature does nothing at all: no volume is remembered and no
        /// volume is restored. That matters, because remembering while off would overwrite a
        /// good value with whatever the device happens to be at.
        /// </summary>
        public bool IsEnabled
        {
            get => _settings.RememberBluetoothVolume;
            set
            {
                if (_settings.RememberBluetoothVolume == value)
                {
                    return;
                }

                _settings.RememberBluetoothVolume = value;
                Trace.WriteLine($"BluetoothVolumeMemory IsEnabled = {value}");

                if (!value)
                {
                    // Drop anything queued so a disabled feature never writes.
                    _saveTimer.Stop();
                    _pendingVolumes.Clear();
                }
            }
        }

        /// <summary>
        /// Bluetooth audio endpoints are enumerated by the Bluetooth stack: "BTHENUM" for
        /// A2DP, "BTHHFENUM" for hands-free and "BTHLEENUM" for LE Audio.
        /// </summary>
        public static bool IsBluetoothDevice(IAudioDevice device)
        {
            var enumeratorName = (device as IAudioDeviceWindowsAudio)?.EnumeratorName;
            return !string.IsNullOrEmpty(enumeratorName) &&
                enumeratorName.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Keys a remembered volume by the endpoint GUID rather than by the whole endpoint id:
        /// the "{0.0.0.00000000}." prefix identifies the audio adapter container, and using
        /// only the trailing GUID keeps the memory valid if that container is re-created.
        /// </summary>
        internal static string GetStorageKey(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return string.Empty;
            }

            var lastDot = deviceId.LastIndexOf('.');
            var uniquePart = (lastDot >= 0) ? deviceId.Substring(lastDot + 1) : deviceId;

            // The key ends up in the shared settings store (registry or package settings),
            // so keep it to characters both of them accept.
            var chars = uniquePart.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '{' && c != '}' && c != '-' && c != '_')
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Every device we have a remembered volume for, newest-connection first and then by
        /// name. Includes devices that are powered off right now - that is the point: the list
        /// has to describe what we would restore even when nothing is connected.
        /// </summary>
        public IReadOnlyList<RememberedDevice> GetRememberedDevices()
        {
            var ret = new List<RememberedDevice>();

            try
            {
                var liveDevices = _deviceManager.Devices.ToArray();

                foreach (var key in _settingsBag.GetKeys())
                {
                    if (key.EndsWith(InfoSuffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var volume = _settingsBag.Get(key, 0);
                    if (volume < MinRememberedVolume || volume > MaxRememberedVolume)
                    {
                        // Never a user choice, so do not present it as a remembered volume.
                        continue;
                    }

                    var info = ReadInfo(key);
                    var live = FindLiveDevice(liveDevices, key);

                    ret.Add(new RememberedDevice(
                        key,
                        volume,
                        FirstNonEmpty(info?.Name, live?.DisplayName),
                        FirstNonEmpty(info?.Enumerator, (live as IAudioDeviceWindowsAudio)?.EnumeratorName),
                        ParseTimestamp(info?.LastRecordedUtc),
                        live != null,
                        live?.Id,
                        (live != null) ? ToPercent(live.Volume) : volume));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory GetRememberedDevices Failed: {ex.Message}");
            }

            return ret
                .OrderByDescending(device => device.IsConnected)
                .ThenBy(device => device.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        internal static string GetInfoKey(string storageKey)
        {
            return storageKey + InfoSuffix;
        }

        /// <summary>
        /// Drops everything we remember about one device. The device itself is untouched - only
        /// the record that would restore its volume on the next reconnect. The next time the
        /// volume changes it will be learned again, which is the point: this exists for records
        /// the user no longer wants, not for switching the feature off.
        /// </summary>
        public void Forget(string storageKey)
        {
            if (string.IsNullOrEmpty(storageKey))
            {
                return;
            }

            try
            {
                _settingsBag.Remove(storageKey);
                _settingsBag.Remove(GetInfoKey(storageKey));

                // A write for this device may still be queued behind the debounce timer; if it
                // fires it would put back exactly what we just deleted.
                _pendingVolumes.Remove(storageKey);

                Trace.WriteLine($"BluetoothVolumeMemory Forget {storageKey}");
                RaiseRecordsChanged();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory Forget Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Drops records that could never be restored anyway: a volume outside the usable range,
        /// or a leftover info entry whose volume is gone. These are entries the reader already
        /// skips, so removing them changes nothing except the amount of junk we keep.
        ///
        /// Devices that are merely not connected are NEVER touched. "Not here right now" and
        /// "gone for good" look identical from here, and the whole point of this feature is to
        /// have something to restore when an absent device comes back - so guessing would break
        /// the feature to save a few hundred bytes.
        ///
        /// Takes the bag rather than using the field so it can be exercised against a throwaway
        /// store. Returns how many records went away.
        /// </summary>
        internal static int PruneUnusableRecords(ISettingsBag settingsBag)
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in settingsBag.GetKeys())
            {
                owners.Add(key.EndsWith(InfoSuffix, StringComparison.Ordinal)
                    ? key.Substring(0, key.Length - InfoSuffix.Length)
                    : key);
            }

            var removed = 0;
            foreach (var owner in owners)
            {
                var volume = settingsBag.Get(owner, 0);
                if (volume >= MinRememberedVolume && volume <= MaxRememberedVolume)
                {
                    continue;
                }

                settingsBag.Remove(owner);
                settingsBag.Remove(GetInfoKey(owner));
                removed++;
            }

            return removed;
        }

        private void PruneUnusableRecords()
        {
            try
            {
                var removed = PruneUnusableRecords(_settingsBag);
                if (removed > 0)
                {
                    Trace.WriteLine($"BluetoothVolumeMemory pruned {removed} unusable record(s)");
                }
            }
            catch (Exception ex)
            {
                // Housekeeping must never stop the app from starting.
                Trace.WriteLine($"BluetoothVolumeMemory PruneUnusableRecords Failed: {ex.Message}");
            }
        }

        private void OnDevicesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (IAudioDevice device in e.NewItems)
                        {
                            Attach(device);
                            // This is the reconnect: the endpoint came back, put the volume back.
                            RestoreIfRemembered(device, announce: true);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (IAudioDevice device in e.OldItems)
                        {
                            // The headset just powered off; the level the user left it at is
                            // still queued, so get it written before the endpoint id is gone.
                            FlushPending(device.Id);
                            Detach(device);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // Not raised by the device manager today, but keep the subscriptions honest.
                    ReattachAll();
                    break;

                default:
                    break;
            }

            // Connected/disconnected changes how the settings page describes each device.
            RaiseRecordsChanged();
        }

        private void Attach(IAudioDevice device)
        {
            if (device == null || string.IsNullOrEmpty(device.Id) || !IsBluetoothDevice(device))
            {
                return;
            }

            if (!_attachedDevices.ContainsKey(device.Id))
            {
                _attachedDevices[device.Id] = device;
                device.PropertyChanged += OnDevicePropertyChanged;
                Trace.WriteLine($"BluetoothVolumeMemory Attach {device.Id}");
            }
        }

        private void Detach(IAudioDevice device)
        {
            if (device == null || !_attachedDevices.ContainsKey(device.Id))
            {
                return;
            }

            device.PropertyChanged -= OnDevicePropertyChanged;
            _attachedDevices.Remove(device.Id);
            Trace.WriteLine($"BluetoothVolumeMemory Detach {device.Id}");
        }

        private void ReattachAll()
        {
            foreach (var device in _attachedDevices.Values.ToArray())
            {
                device.PropertyChanged -= OnDevicePropertyChanged;
            }
            _attachedDevices.Clear();

            foreach (var device in _deviceManager.Devices.ToArray())
            {
                Attach(device);
            }
        }

        private void OnDevicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!IsEnabled || e.PropertyName != nameof(IAudioDevice.Volume))
            {
                return;
            }

            QueueSave(sender as IAudioDevice);
        }

        private void QueueSave(IAudioDevice device)
        {
            if (device == null || !IsBluetoothDevice(device))
            {
                return;
            }

            var key = GetStorageKey(device.Id);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            var volume = ToPercent(device.Volume);
            if (volume < MinRememberedVolume || volume > MaxRememberedVolume)
            {
                // A transient 0 (the endpoint going away) must not overwrite a good value.
                return;
            }

            _pendingVolumes[key] = volume;

            // Restart the window so a slider drag produces a single write.
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                {
                    return;
                }
                _dispatcher.BeginInvoke((Action)FlushPendingVolumes);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory OnSaveTimerElapsed Failed: {ex.Message}");
            }
        }

        private void FlushPendingVolumes()
        {
            foreach (var pair in _pendingVolumes.ToArray())
            {
                Save(pair.Key, pair.Value);
            }
            _pendingVolumes.Clear();
        }

        private void FlushPending(string deviceId)
        {
            var key = GetStorageKey(deviceId);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (_pendingVolumes.TryGetValue(key, out var volume))
            {
                Save(key, volume);
                _pendingVolumes.Remove(key);
            }
        }

        private void Save(string key, int volume)
        {
            try
            {
                _settingsBag.Set(key, volume);
                WriteInfo(key, FindLiveDevice(key));
                Trace.WriteLine($"BluetoothVolumeMemory Save {key} {volume}");
                RaiseRecordsChanged();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory Save Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Puts the remembered volume back on a device. <paramref name="announce"/> says whether
        /// this is a reconnect worth telling the user about (it is false for the devices already
        /// present when the app started).
        /// </summary>
        private void RestoreIfRemembered(IAudioDevice device, bool announce)
        {
            if (!IsEnabled || device == null || !IsBluetoothDevice(device))
            {
                return;
            }

            var key = GetStorageKey(device.Id);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                if (!_settingsBag.HasKey(key))
                {
                    return;
                }

                var remembered = _settingsBag.Get(key, 0);
                if (remembered < MinRememberedVolume || remembered > MaxRememberedVolume)
                {
                    return;
                }

                // Builds before the settings page existed stored only the volume. Capture the
                // name now, while the endpoint is alive, so the list can still describe this
                // device after it powers off again.
                WriteInfo(key, device);

                var current = ToPercent(device.Volume);
                if (current == remembered)
                {
                    // Already where the user left it - but the device did come back, and the
                    // overlay is how the user learns which level it is at, so still announce it.
                    RaiseVolumeRestored(device, current, announce);
                    return;
                }

                Trace.WriteLine($"BluetoothVolumeMemory Restore {device.Id} {current}% -> {remembered}%");
                device.Volume = remembered / 100f;

                // The endpoint may snap to a level it supports instead of the requested one,
                // in which case what we just observed is the truth - remember that instead.
                var applied = ToPercent(device.Volume);
                if (applied != remembered && applied >= MinRememberedVolume && applied <= MaxRememberedVolume)
                {
                    _pendingVolumes[key] = applied;
                    _saveTimer.Stop();
                    _saveTimer.Start();
                }

                // Announce the level the device actually ended up at, not the one we asked for.
                RaiseVolumeRestored(device, applied, announce);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory Restore Failed: {ex.Message}");
            }
        }

        private void RaiseVolumeRestored(IAudioDevice device, int volume, bool announce)
        {
            if (!announce)
            {
                return;
            }

            try
            {
                // Handlers touch UI, and this can be raised from the audio manager's thread, so
                // they are expected to marshal - same contract as RecordsChanged.
                VolumeRestored?.Invoke(this, new VolumeRestoredEventArgs(device, volume, device.IsMuted));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory VolumeRestored handler threw: {ex.Message}");
            }
        }

        private StoredDeviceInfo ReadInfo(string storageKey)
        {
            try
            {
                var infoKey = GetInfoKey(storageKey);
                if (!_settingsBag.HasKey(infoKey))
                {
                    return null;
                }

                return _settingsBag.Get<StoredDeviceInfo>(infoKey, null);
            }
            catch (Exception ex)
            {
                // A record we cannot read must not hide the device: fall back to whatever the
                // live endpoint can tell us.
                Trace.WriteLine($"BluetoothVolumeMemory ReadInfo Failed: {ex.Message}");
                return null;
            }
        }

        private void WriteInfo(string storageKey, IAudioDevice device)
        {
            if (device == null)
            {
                return;
            }

            try
            {
                _settingsBag.Set(GetInfoKey(storageKey), new StoredDeviceInfo
                {
                    Version = CurrentInfoVersion,
                    Name = device.DisplayName,
                    Enumerator = (device as IAudioDeviceWindowsAudio)?.EnumeratorName,
                    LastRecordedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory WriteInfo Failed: {ex.Message}");
            }
        }

        private IAudioDevice FindLiveDevice(string storageKey)
        {
            return FindLiveDevice(_deviceManager.Devices.ToArray(), storageKey);
        }

        private static IAudioDevice FindLiveDevice(IAudioDevice[] devices, string storageKey)
        {
            foreach (var device in devices)
            {
                if (device != null && GetStorageKey(device.Id) == storageKey)
                {
                    return device;
                }
            }
            return null;
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first : second;
        }

        private static DateTime? ParseTimestamp(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private void RaiseRecordsChanged()
        {
            try
            {
                RecordsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BluetoothVolumeMemory RecordsChanged handler threw: {ex.Message}");
            }
        }

        private static int ToPercent(float volume)
        {
            var bounded = Math.Min(1f, Math.Max(0f, volume));
            return (int)Math.Round(bounded * 100f);
        }
    }
}
