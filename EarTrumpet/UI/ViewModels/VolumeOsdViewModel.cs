using EarTrumpet.DataModel;
using System.Windows.Media;

namespace EarTrumpet.UI.ViewModels
{
    /// <summary>Which palette the volume overlay uses.</summary>
    public enum VolumeOsdTheme
    {
        /// <summary>Follow Windows. What the app uses; the others exist for the preview switch.</summary>
        System,
        Light,
        Dark,
    }

    /// <summary>
    /// Backs the volume overlay shown when a reconnecting device gets its remembered volume
    /// back.
    ///
    /// Everything here mirrors the overlay Windows 11 draws for the volume keys. The numbers
    /// were measured off a screenshot of the real thing at 125% scale and converted to DIPs,
    /// which lands on round values (192x46 card, 110x4 track), so the geometry below is the
    /// real design rather than a guess.
    /// </summary>
    public class VolumeOsdViewModel : BindableBase
    {
        /// <summary>Width of the track, in DIPs. See the class comment.</summary>
        public const double TrackWidth = 110;

        // Measured colours, light theme (the case the screenshot was taken in).
        private static readonly Brush LightCard = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        private static readonly Brush LightBorder = Frozen(Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
        private static readonly Brush LightText = Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly Brush LightTrack = Frozen(Color.FromRgb(0x7A, 0x80, 0x85));
        private static readonly Brush LightFill = Frozen(Color.FromRgb(0x00, 0x67, 0xC0));

        // Dark theme is not screenshot-measured - it is the conventional Windows palette, so
        // the overlay does not turn into a white slab for anyone running Windows in dark mode.
        private static readonly Brush DarkCard = Frozen(Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B));
        private static readonly Brush DarkBorder = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        private static readonly Brush DarkText = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush DarkTrack = Frozen(Color.FromRgb(0x9A, 0x9A, 0x9A));
        private static readonly Brush DarkFill = Frozen(Color.FromRgb(0x60, 0xCD, 0xFF));

        private Brush _cardBrush = LightCard;
        private Brush _borderBrush = LightBorder;
        private Brush _textBrush = LightText;
        private Brush _trackBrush = LightTrack;
        private Brush _fillBrush = LightFill;
        private string _glyph = "\uE992";
        private string _volumeText = "0";
        private double _fillWidth;

        public Brush CardBrush => _cardBrush;

        /// <summary>
        /// The 1px rim around the card. The real overlay has one - measured on all four sides,
        /// the edge pixel is cooler than the interior (red down, blue up) - and without it the
        /// card reads as having no edge at all against a light background.
        /// </summary>
        public Brush BorderBrush => _borderBrush;

        public Brush TextBrush => _textBrush;
        public Brush TrackBrush => _trackBrush;
        public Brush FillBrush => _fillBrush;

        /// <summary>Segoe Fluent / MDL2 codepoint for the speaker-with-waves, by level.</summary>
        public string Glyph => _glyph;

        /// <summary>The percentage on the right, no sign. Windows shows the value as-is.</summary>
        public string VolumeText => _volumeText;

        public double FillWidth => _fillWidth;

        public void Update(int volume, bool isMuted, VolumeOsdTheme theme = VolumeOsdTheme.System)
        {
            _glyph = GlyphFor(volume, isMuted);
            _volumeText = volume.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _fillWidth = TrackWidth * (volume / 100.0);
            ApplyTheme(IsLight(theme));

            RaisePropertyChanged(nameof(Glyph));
            RaisePropertyChanged(nameof(VolumeText));
            RaisePropertyChanged(nameof(FillWidth));
            RaisePropertyChanged(nameof(CardBrush));
            RaisePropertyChanged(nameof(BorderBrush));
            RaisePropertyChanged(nameof(TextBrush));
            RaisePropertyChanged(nameof(TrackBrush));
            RaisePropertyChanged(nameof(FillBrush));
        }

        /// <summary>
        /// Follows the SYSTEM theme, not the app theme. This copies something the shell draws
        /// (ShellExperienceHost), and shell surfaces - taskbar, flyouts, the real volume
        /// overlay - follow the Windows light/dark setting, which is what SystemUsesLightTheme
        /// records. AppsUseLightTheme is a separate setting and would pick the wrong palette.
        /// </summary>
        private static bool IsLight(VolumeOsdTheme theme)
        {
            switch (theme)
            {
                case VolumeOsdTheme.Light:
                    return true;
                case VolumeOsdTheme.Dark:
                    return false;
                default:
                    return SystemSettings.IsSystemLightTheme;
            }
        }

        /// <summary>
        /// Windows picks the wave count from the level: muted/none, then a third each. At 50%
        /// the real overlay shows two waves, which is what this reproduces.
        /// </summary>
        private static string GlyphFor(int volume, bool isMuted)
        {
            if (isMuted || volume <= 0)
            {
                return "\uE992";    // Volume0
            }
            if (volume <= 33)
            {
                return "\uE993";    // Volume1
            }
            if (volume <= 66)
            {
                return "\uE994";    // Volume2
            }
            return "\uE995";        // Volume3
        }

        private void ApplyTheme(bool isLight)
        {
            _cardBrush = isLight ? LightCard : DarkCard;
            _borderBrush = isLight ? LightBorder : DarkBorder;
            _textBrush = isLight ? LightText : DarkText;
            _trackBrush = isLight ? LightTrack : DarkTrack;
            _fillBrush = isLight ? LightFill : DarkFill;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
