using EarTrumpet.Interop.Helpers;
using EarTrumpet.UI.Helpers;
using System;
using System.Diagnostics;
using System.Windows.Input;

namespace EarTrumpet.UI.ViewModels
{
    class EarTrumpetAboutPageViewModel : SettingsPageViewModel
    {
        /// <summary>This fork. The About page links here for "learn more", not to upstream.</summary>
        private const string ThisRepoUrl = "https://github.com/YHuanheg/EarTrumpet";

        /// <summary>The project this fork is built from. Linked separately, and where bug reports go.</summary>
        private const string UpstreamUrl = "https://github.com/File-New-Project/EarTrumpet";

        public ICommand OpenDiagnosticsCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenUpstreamCommand { get; }
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

            OpenAboutCommand = new RelayCommand(OpenThisRepo);
            OpenUpstreamCommand = new RelayCommand(OpenUpstream);
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

        // "Learn more" belongs to this build: it is where the extra features are documented.
        private void OpenThisRepo() => ProcessHelper.StartNoThrow(ThisRepoUrl);
        private void OpenUpstream() => ProcessHelper.StartNoThrow(UpstreamUrl);

        // Feedback goes to this fork's own tracker. Its Issues were enabled for exactly this,
        // and .github/ISSUE_TEMPLATE/config.yml no longer offers upstream's discussions, so
        // every entry on that page lands here.
        private void OpenGitHubIssueChooser() => ProcessHelper.StartNoThrow($"{ThisRepoUrl}/issues/new/choose");
    }
}
