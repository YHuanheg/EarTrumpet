using EarTrumpet.Interop.Helpers;
using EarTrumpet.UI.Helpers;
using System.Windows;
using System.Windows.Input;

namespace EarTrumpet.UI.ViewModels
{
    class WelcomeViewModel
    {
        public string VisibleTitle => ""; // We have a header instead
        public string Title { get; } // Used for the window title.
        public ICommand LearnMore { get; }
        public ICommand DisplaySettingsChanged { get; }

        private WindowViewState _state;

        // No "send crash data" opt-in here: this build reports nothing, so there is nothing to
        // ask about. See Diagnosis/ErrorReporter.cs.
        //
        // "Learn more" points at this fork: on first run the thing worth reading is what this
        // build does differently, which upstream's page does not describe.
        public WelcomeViewModel()
        {
            Title = Properties.Resources.WelcomeDialogHeaderText;
            LearnMore = new RelayCommand(() => ProcessHelper.StartNoThrow("https://github.com/YHuanheg/EarTrumpet"));
        }

        public void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            switch (_state)
            {
                case WindowViewState.Open:
                    _state = WindowViewState.Closing;
                    e.Cancel = true;

                    var window = (Window)sender;
                    WindowAnimationLibrary.BeginWindowExitAnimation(window, () =>
                    {
                        _state = WindowViewState.CloseReady;
                        window.Close();
                    });
                    break;
                case WindowViewState.Closing:
                    // Ignore any requests while playing the close animation.
                    e.Cancel = true;
                    break;
                case WindowViewState.CloseReady:
                    // Accept the close.
                    break;
            }
        }
    }
}
