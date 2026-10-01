using EarTrumpet.Extensions;
using EarTrumpet.Interop;
using EarTrumpet.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EarTrumpet.UI.Views
{
    /// <summary>
    /// The volume overlay shown when a reconnecting device gets its remembered volume back,
    /// drawn to look like the one Windows shows for the volume keys.
    ///
    /// Why a copy rather than the real thing: the Windows overlay belongs to
    /// ShellExperienceHost.exe, no public API brings it up, and calling into it is not an
    /// option - it only reacts to hardware AppCommands, and Windows 11 dropped the older
    /// SndVolSSO overlay that software volume changes used to trigger. Emulating a HID device
    /// or injecting into the shell would be the only ways in, and neither is acceptable for
    /// this. So the appearance is reproduced instead, measured off a screenshot of the real
    /// overlay at 125% scale (see the XAML for the numbers).
    /// </summary>
    public partial class VolumeOsdWindow : Window
    {
        /// <summary>Card size, measured off the real overlay. DIPs.</summary>
        private const double CardWidth = 192;
        private const double CardHeight = 46;

        /// <summary>
        /// Slack around the card for the drop shadow. The window clips at its own bounds, so
        /// without this the shadow would be cut off; the card keeps the measured size and the
        /// positioning code subtracts this again.
        /// </summary>
        private const double ShadowPadding = 12;

        /// <summary>How long Windows leaves its overlay up after the last change.</summary>
        private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(3);

        private static readonly Duration FadeDuration = new Duration(TimeSpan.FromMilliseconds(150));

        /// <summary>Measured gap between the bottom of the card and the top of the taskbar.</summary>
        private const double GapAboveTaskbar = 14;

        private readonly VolumeOsdViewModel _viewModel = new VolumeOsdViewModel();
        private readonly DispatcherTimer _hideTimer = new DispatcherTimer();

        // Bumped on every change, so a fade-out that is already running cannot hide a newer
        // overlay - two endpoints of the same headset reconnect within the same second.
        private int _generation;

        public VolumeOsdWindow()
        {
            InitializeComponent();

            // Size the window around the card so both stay in step with the constants above.
            Width = CardWidth + (ShadowPadding * 2);
            Height = CardHeight + (ShadowPadding * 2);
            Card.Margin = new Thickness(ShadowPadding);

            DataContext = _viewModel;
            Opacity = 0;

            _hideTimer.Interval = VisibleDuration;
            _hideTimer.Tick += OnHideTimerTick;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Tool window keeps it out of Alt+Tab. No-activate: it must never take focus from
            // whatever the user is doing. Transparent: clicks pass straight through, which is
            // how the real overlay behaves.
            this.ApplyExtendedWindowStyle(
                User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE | User32.WS_EX_TRANSPARENT);
        }

        /// <summary>
        /// Shows the overlay, or refreshes it if it is already up. Safe to call repeatedly:
        /// the value, the timer and the fade all restart, so a burst of reconnects reads as one
        /// overlay tracking the latest value instead of a flicker.
        /// </summary>
        public void ShowVolume(int volume, bool isMuted, VolumeOsdTheme theme = VolumeOsdTheme.System)
        {
            _viewModel.Update(volume, isMuted, theme);
            MoveToOverlayPosition();

            _generation++;
            _hideTimer.Stop();
            _hideTimer.Start();

            if (!IsVisible)
            {
                // ShowActivated=False plus WS_EX_NOACTIVATE means the focus stays where it was.
                Show();
            }

            BeginAnimation(OpacityProperty, new DoubleAnimation(1, FadeDuration));
        }

        private void OnHideTimerTick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            BeginHide(_generation);
        }

        private void BeginHide(int generation)
        {
            var fade = new DoubleAnimation(0, FadeDuration);
            fade.Completed += (_, __) =>
            {
                if (generation != _generation)
                {
                    // A newer change arrived while this fade was running - keep showing.
                    return;
                }

                Hide();
            };

            BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// Bottom centre, just clear of the taskbar: where the real overlay sits. Based on the
        /// primary display's work area, so it lands correctly whatever the taskbar's size or
        /// edge. The shadow padding is taken back out so it is the CARD that lands on the
        /// measured spot, not the window. (The real one may follow the active monitor instead;
        /// we keep it predictable.)
        /// </summary>
        private void MoveToOverlayPosition()
        {
            var workArea = SystemParameters.WorkArea;

            var cardLeft = workArea.Left + ((workArea.Width - CardWidth) / 2);
            var cardTop = workArea.Bottom - GapAboveTaskbar - CardHeight;

            Left = Math.Round(cardLeft - ShadowPadding);
            Top = Math.Round(cardTop - ShadowPadding);
        }
    }
}
