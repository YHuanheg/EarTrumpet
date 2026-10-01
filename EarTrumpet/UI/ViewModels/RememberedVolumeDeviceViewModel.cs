using EarTrumpet.DataModel;
using EarTrumpet.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;

namespace EarTrumpet.UI.ViewModels
{
    /// <summary>
    /// One row on the Devices settings page: a device we have a remembered volume for.
    /// Immutable apart from the command - the list is rebuilt on every refresh, so there is
    /// nothing else to notify about.
    /// </summary>
    public class RememberedVolumeDeviceViewModel
    {
        // Separator inside the summary line. A symbol rather than a word, so it needs no
        // translation; the words around it all come from resources.
        private const string SummarySeparator = "  \u00b7  ";

        private readonly RememberedDevice _device;

        /// <summary>Drops the remembered volume for this device. See BluetoothVolumeMemory.Forget.</summary>
        public ICommand ForgetCommand { get; }

        public RememberedVolumeDeviceViewModel(RememberedDevice device, Action<string> forget)
        {
            _device = device;
            ForgetCommand = new RelayCommand(() => forget?.Invoke(_device.Key));
        }

        /// <summary>The storage key, useful in a tooltip and for testing.</summary>
        public string Key => _device.Key;

        public string DisplayName => string.IsNullOrWhiteSpace(_device.DisplayName)
            ? Properties.Resources.SettingsRememberedDeviceUnknownNameText
            : _device.DisplayName;

        public bool IsConnected => _device.IsConnected;

        public string StatusText => IsConnected
            ? Properties.Resources.SettingsRememberedDeviceConnectedText
            : Properties.Resources.SettingsRememberedDeviceDisconnectedText;

        public string KindText => GetKindText(_device.EnumeratorName);

        public string RememberedVolumeText => Format(_device.Volume);

        /// <summary>
        /// "Bluetooth stereo (A2DP)  ·  remembered 45%  ·  now 30%". The current volume is only
        /// added while the device is here and only when it disagrees with the remembered value,
        /// so the two numbers are never shown side by side saying the same thing.
        /// </summary>
        public string SummaryText
        {
            get
            {
                var parts = new List<string>
                {
                    KindText,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Properties.Resources.SettingsRememberedDeviceRememberedVolumeFormat,
                        RememberedVolumeText)
                };

                if (IsConnected && _device.CurrentVolume != _device.Volume)
                {
                    parts.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        Properties.Resources.SettingsRememberedDeviceCurrentVolumeFormat,
                        Format(_device.CurrentVolume)));
                }

                return string.Join(SummarySeparator, parts);
            }
        }

        public bool IsLastRecordedVisible => _device.LastRecordedUtc.HasValue;

        public string LastRecordedText => _device.LastRecordedUtc.HasValue
            ? string.Format(
                CultureInfo.CurrentCulture,
                Properties.Resources.SettingsRememberedDeviceLastRecordedFormat,
                _device.LastRecordedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : string.Empty;

        /// <summary>
        /// The endpoint id is not available while the device is away, so fall back to the
        /// storage key - it is the same GUID, just without the adapter prefix.
        /// </summary>
        public string DetailsText => string.Format(
            CultureInfo.CurrentCulture,
            Properties.Resources.SettingsRememberedDeviceToolTipFormat,
            _device.DeviceId ?? _device.Key,
            _device.EnumeratorName ?? string.Empty);

        private static string Format(int volume) => volume.ToString(CultureInfo.CurrentCulture) + "%";

        /// <summary>
        /// The Bluetooth stack tells us which profile an endpoint belongs to through its
        /// enumerator name: BTHENUM is A2DP, BTHHFENUM is hands-free and BTHLEENUM is LE Audio.
        /// </summary>
        private static string GetKindText(string enumeratorName)
        {
            if (!string.IsNullOrEmpty(enumeratorName))
            {
                if (enumeratorName.StartsWith("BTHHFENUM", StringComparison.OrdinalIgnoreCase))
                {
                    return Properties.Resources.BluetoothDeviceKindHandsFreeText;
                }

                if (enumeratorName.StartsWith("BTHLEENUM", StringComparison.OrdinalIgnoreCase))
                {
                    return Properties.Resources.BluetoothDeviceKindLeAudioText;
                }

                if (enumeratorName.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase))
                {
                    return Properties.Resources.BluetoothDeviceKindStereoText;
                }
            }

            return Properties.Resources.BluetoothDeviceKindGenericText;
        }
    }
}
