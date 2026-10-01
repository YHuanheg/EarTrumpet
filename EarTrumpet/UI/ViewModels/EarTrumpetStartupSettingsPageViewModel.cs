using EarTrumpet.Interop.Helpers;
using System.Diagnostics;

namespace EarTrumpet.UI.ViewModels
{
    /// <summary>
    /// "Run at startup" for a packaged (StartupTask) or unpackaged (Run key) EarTrumpet.
    /// The state is owned by Windows, not by our settings store, so it is read back after
    /// every change instead of being cached.
    /// </summary>
    public class EarTrumpetStartupSettingsPageViewModel : SettingsPageViewModel
    {
        private RunAtStartupState _state;

        /// <summary>
        /// Read only when Windows owns the value (the user turned the entry off in Task
        /// Manager, or a policy is in force) - the checkbox is disabled in those cases.
        /// </summary>
        public bool RunAtStartup
        {
            get => _state == RunAtStartupState.Enabled || _state == RunAtStartupState.ForcedByPolicy;
            set
            {
                if (IsRunAtStartupConfigurable)
                {
                    StartupHelper.SetEnabled(value);
                }
                RefreshState();
            }
        }

        public bool IsRunAtStartupConfigurable => StartupHelper.CanUserChange(_state);

        public string RunAtStartupStatusText
        {
            get
            {
                switch (_state)
                {
                    case RunAtStartupState.DisabledByUser:
                        return Properties.Resources.SettingsRunAtStartupDisabledByUserText;
                    case RunAtStartupState.DisabledByPolicy:
                        return Properties.Resources.SettingsRunAtStartupDisabledByPolicyText;
                    case RunAtStartupState.ForcedByPolicy:
                        return Properties.Resources.SettingsRunAtStartupEnabledByPolicyText;
                    case RunAtStartupState.Unavailable:
                        return Properties.Resources.SettingsRunAtStartupUnavailableText;
                    default:
                        return string.Empty;
                }
            }
        }

        public bool HasRunAtStartupStatusText => !string.IsNullOrEmpty(RunAtStartupStatusText);

        public EarTrumpetStartupSettingsPageViewModel() : base(null)
        {
            Title = Properties.Resources.StartupSettingsPageText;
            Glyph = "\xE7E8";

            // Safe to resolve here: the page is created on demand when Settings opens, and
            // the read is a bounded registry query (see StartupHelper.OperationTimeout).
            RefreshState();
        }

        private void RefreshState()
        {
            _state = StartupHelper.GetState();

            // Windows may have refused the change, so every dependent bit is re-published.
            RaisePropertyChanged(nameof(RunAtStartup));
            RaisePropertyChanged(nameof(IsRunAtStartupConfigurable));
            RaisePropertyChanged(nameof(RunAtStartupStatusText));
            RaisePropertyChanged(nameof(HasRunAtStartupStatusText));
        }
    }
}
