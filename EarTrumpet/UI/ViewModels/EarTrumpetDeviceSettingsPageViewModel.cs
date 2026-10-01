using EarTrumpet.DataModel;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;

namespace EarTrumpet.UI.ViewModels
{
    public class EarTrumpetDeviceSettingsPageViewModel : SettingsPageViewModel
    {
        private readonly BluetoothVolumeMemory _bluetoothVolumeMemory;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        /// <summary>
        /// Devices we hold a remembered volume for, connected or not. Rebuilt on every refresh
        /// rather than patched, so the collection can never drift from what is stored.
        /// </summary>
        public ObservableCollection<RememberedVolumeDeviceViewModel> RememberedDevices { get; }
            = new ObservableCollection<RememberedVolumeDeviceViewModel>();

        public bool RememberBluetoothVolume
        {
            get => _bluetoothVolumeMemory.IsEnabled;
            set
            {
                _bluetoothVolumeMemory.IsEnabled = value;
                RaisePropertyChanged(nameof(RememberBluetoothVolume));
            }
        }

        public bool HasNoRememberedDevices => RememberedDevices.Count == 0;

        public string RememberedDevicesHeader => string.Format(
            CultureInfo.CurrentCulture,
            Properties.Resources.SettingsRememberedDevicesHeader,
            RememberedDevices.Count);

        public EarTrumpetDeviceSettingsPageViewModel(BluetoothVolumeMemory bluetoothVolumeMemory) : base(null)
        {
            _bluetoothVolumeMemory = bluetoothVolumeMemory;
            Title = Properties.Resources.DeviceSettingsPageText;
            Glyph = "\xE702";

            _bluetoothVolumeMemory.RecordsChanged += OnRecordsChanged;
            Refresh();
        }

        public override void NavigatedTo()
        {
            // Devices may have come and gone while this page was in the background.
            Refresh();
        }

        public void Refresh()
        {
            try
            {
                var devices = _bluetoothVolumeMemory.GetRememberedDevices();

                RememberedDevices.Clear();
                foreach (var device in devices)
                {
                    RememberedDevices.Add(new RememberedVolumeDeviceViewModel(device, Forget));
                }

                RaisePropertyChanged(nameof(HasNoRememberedDevices));
                RaisePropertyChanged(nameof(RememberedDevicesHeader));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"DeviceSettingsPage Refresh Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Drops the remembered volume for one device. Only the record goes away - the device
        /// itself is untouched, and the volume is learned again the next time it changes.
        /// </summary>
        public void Forget(string storageKey)
        {
            _bluetoothVolumeMemory.Forget(storageKey);
        }

        private void OnRecordsChanged(object sender, EventArgs e)
        {
            // The device collection is fed by the audio manager, so this can arrive on a
            // background thread. Even on the UI thread we hand the work to the dispatcher:
            // a record can be dropped from inside its own row's click handler, and rebuilding
            // the bound collection while that click is still being dispatched would be
            // mutating the items the visual tree is walking.
            try
            {
                if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                {
                    return;
                }

                _dispatcher.BeginInvoke((Action)Refresh);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"DeviceSettingsPage OnRecordsChanged Failed: {ex.Message}");
            }
        }
    }
}
