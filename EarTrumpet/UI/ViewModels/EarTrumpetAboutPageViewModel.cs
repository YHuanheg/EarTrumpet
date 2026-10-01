using EarTrumpet.Interop.Helpers;
using EarTrumpet.UI.Helpers;
using System;
using System.Diagnostics;
using System.Windows.Input;

namespace EarTrumpet.UI.ViewModels
{
    class EarTrumpetAboutPageViewModel : SettingsPageViewModel
    {
        public ICommand OpenDiagnosticsCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenFeedbackCommand { get; }
        public string AboutText { get; }

        private readonly Action _openDiagnostics;

        // No telemetry opt-in and no privacy-policy link: this build never reports anything,
        // so both would be describing something that does not happen. See ErrorReporter.cs.
        public EarTrumpetAboutPageViewModel(Action openDiagnostics) : base(null)
        {
            _openDiagnostics = openDiagnostics;
            Glyph = "\xE946";
            Title = Properties.Resources.AboutTitle;
            AboutText = $"EarTrumpet {App.PackageVersion}";

            OpenAboutCommand = new RelayCommand(OpenAbout);
            OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
            OpenFeedbackCommand = new RelayCommand(OpenGitHubIssueChooser);
        }

        private void OpenDiagnostics()
        {
            if (Keyboard.IsKeyDown(Key.LeftShift) && Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                Trace.WriteLine($"EarTrumpetAboutPageViewModel OpenDiagnostics - CRASH");
                throw new Exception("This is an intentional crash.");
            }

            _openDiagnostics.Invoke();
        }

        private void OpenGitHubIssueChooser() => ProcessHelper.StartNoThrow("https://github.com/File-New-Project/EarTrumpet/issues/new/choose");
        private void OpenAbout() => ProcessHelper.StartNoThrow("https://github.com/File-New-Project/EarTrumpet");
    }
}
