using Ricochet.Client.Game;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Theme
{
    /// <summary>
    /// Every colour the game draws with, in one asset (<c>Assets/Settings/GameTheme.asset</c>), so the look
    /// can be swapped without touching game code. Views without a theme assigned use these defaults. The board's tiles and
    /// walls come from the player's <see cref="BoardStyle"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Ricochet/Game Theme", fileName = "GameTheme")]
    public class GameTheme : ScriptableObject
    {
        [Header("Board")]
        [Tooltip("Behind the board (the camera's clear colour).")]
        [SerializeField] private Color _background = new Color(0.12f, 0.13f, 0.19f);
        [Tooltip("Drop shadows under the board, walls and robots (alpha = strength).")]
        [SerializeField] private Color _shadow = new Color(0.05f, 0.04f, 0.10f, 0.35f);
        [Tooltip("Puffs where a robot hits a wall.")]
        [SerializeField] private Color _dust = new Color(0.55f, 0.45f, 0.34f, 0.8f);
        [Tooltip("The selection ring is the selected robot's colour blended 20% towards this.")]
        [SerializeField] private Color _selection = new Color(0.08f, 0.07f, 0.16f);

        [Header("Robots and targets")]
        [SerializeField] private Color _red = new Color(0.90f, 0.28f, 0.30f);
        [SerializeField] private Color _blue = new Color(0.24f, 0.48f, 0.98f);
        [SerializeField] private Color _green = new Color(0.18f, 0.70f, 0.42f);
        [SerializeField] private Color _yellow = new Color(0.96f, 0.72f, 0.18f);

        [Header("What the board shows (frame + banner); replay frames come from the BoardStyle")]
        [SerializeField] private Color _roundOver = new Color(0.30f, 0.31f, 0.39f);
        [Tooltip("Highlighted panel text (the feedback line).")]
        [SerializeField] private Color _highlightText = new Color(0.98f, 0.74f, 0.25f);
        [Tooltip("Banner text on light frames; dark frames get the panel text colour.")]
        [SerializeField] private Color _bannerText = new Color(0.08f, 0.07f, 0.16f);

        [Header("UI panels and text")]
        [Tooltip("Body text (falls back to Unity's built-in font when empty).")]
        [SerializeField] private Font _font;
        [Tooltip("Labels, buttons and banners.")]
        [SerializeField] private Font _boldFont;
        [Tooltip("Titles and the countdown.")]
        [SerializeField] private Font _titleFont;
        [SerializeField] private Color _panel = new Color(0.09f, 0.10f, 0.15f, 0.97f);
        [SerializeField] private Color _panelText = new Color(0.92f, 0.93f, 0.97f);
        [SerializeField] private Color _mutedText = new Color(0.60f, 0.63f, 0.74f);
        [SerializeField] private Color _input = new Color(0.16f, 0.18f, 0.26f);
        [Tooltip("Main actions: Join, Start.")]
        [SerializeField] private Color _primaryButton = new Color(0.47f, 0.42f, 0.82f);
        [Tooltip("Votes: Skip board / I'm done / Skip replay.")]
        [SerializeField] private Color _secondaryButton = new Color(0.27f, 0.29f, 0.40f);
        [Tooltip("Leave lobby.")]
        [SerializeField] private Color _dangerButton = new Color(0.78f, 0.30f, 0.33f);
        [SerializeField] private Color _buttonText = Color.white;

        [Header("Countdown")]
        [SerializeField] private Color _countdownDim = new Color(0f, 0f, 0f, 0.6f);
        [SerializeField] private Color _countdownText = Color.white;

        public Color Background => _background;
        public Color Shadow => _shadow;
        public Color Dust => _dust;
        public Font Font => _font != null ? _font : BuiltInFont;
        public Font BoldFont => _boldFont != null ? _boldFont : Font;
        public Font TitleFont => _titleFont != null ? _titleFont : BoldFont;
        public Color Panel => _panel;
        public Color PanelText => _panelText;
        public Color MutedText => _mutedText;
        public Color Input => _input;
        public Color PrimaryButton => _primaryButton;
        public Color SecondaryButton => _secondaryButton;
        public Color DangerButton => _dangerButton;
        public Color ButtonText => _buttonText;
        public Color Selection => _selection;
        public Color BannerText => _bannerText;
        public Color HighlightText => _highlightText;

        /// <summary>Readable text on <paramref name="background"/>: dark on light colours, light on dark ones.</summary>
        public Color TextOn(Color background) =>
            0.2126f * background.r + 0.7152f * background.g + 0.0722f * background.b > 0.5f ? _bannerText : _panelText;
        public Color CountdownDim => _countdownDim;
        public Color CountdownText => _countdownText;

        public Color RobotColor(RobotColor robot)
        {
            switch (robot)
            {
                case SharedLogic.RobotColor.Red: return _red;
                case SharedLogic.RobotColor.Blue: return _blue;
                case SharedLogic.RobotColor.Green: return _green;
                default: return _yellow;
            }
        }

        /// <summary>
        /// A target's colour: its robot's, or for the any-robot target a soft white (its symbol shows all four robot
        /// colours; this is for its glow, tile tint and the goal line).
        /// </summary>
        public Color TargetColor(Target target) => target.Robot.HasValue ? RobotColor(target.Robot.Value) : AnyTarget;

        public static readonly Color AnyTarget = new Color(1f, 0.96f, 0.88f);

        /// <summary>The four robot colours in order (red, blue, green, yellow): the any-robot target's ring.</summary>
        public Color[] RobotColors => new[] { _red, _blue, _green, _yellow };

        /// <summary>
        /// The frame/banner colour for what the board shows: the player's own board (practice or a round) in the board style's
        /// frame (wood: walnut) or else the palette's, replays in shades of their own (playing vs watching; the target shows on
        /// the board itself), which a palette may replace where they'd clash with its own frame.
        /// </summary>
        public Color FrameColor(BoardMode mode, Target target)
        {
            switch (mode)
            {
                // Replays are framed in the board style's own materials (light = a player's, dark = the shortest).
                case BoardMode.PlayerReplay: return BoardStyle.Current.ReplayFrame;
                case BoardMode.ShortestReplay: return BoardStyle.Current.ShortestFrame;
                case BoardMode.RoundOver: return _roundOver;
                default: return BoardStyle.Current.Frame ?? Palette.Current.BoardFrame;
            }
        }

        private static Font _builtInFont;
        private static Font BuiltInFont => _builtInFont != null ? _builtInFont
            : (_builtInFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private static GameTheme _defaults;

        /// <summary><paramref name="theme"/>, or the built-in defaults when none is assigned.</summary>
        public static GameTheme OrDefault(GameTheme theme)
        {
            if (theme != null)
                return theme;
            if (_defaults == null)
                _defaults = CreateInstance<GameTheme>();
            return _defaults;
        }
    }
}
