using Ricochet.Client.Game;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Theme
{
    /// <summary>
    /// Every colour the game draws with, in one asset (<c>Assets/Settings/GameTheme.asset</c>), so the look
    /// can be swapped without touching game code. Views without a theme assigned use these defaults.
    /// </summary>
    [CreateAssetMenu(menuName = "Ricochet/Game Theme", fileName = "GameTheme")]
    public class GameTheme : ScriptableObject
    {
        [Header("Board")]
        [Tooltip("Behind the board (the camera's clear colour).")]
        [SerializeField] private Color _background = new Color(0.12f, 0.13f, 0.19f);
        [SerializeField] private Color _tileA = new Color(0.96f, 0.93f, 0.87f);
        [SerializeField] private Color _tileB = new Color(0.93f, 0.89f, 0.82f);
        [Tooltip("The gaps between tiles.")]
        [SerializeField] private Color _grout = new Color(0.80f, 0.75f, 0.66f);
        [SerializeField] private Color _wall = new Color(0.23f, 0.21f, 0.31f);
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
        [Tooltip("The wildcard target any robot may reach.")]
        [SerializeField] private Color _anyRobot = new Color(0.61f, 0.36f, 0.90f);

        [Header("What the board shows (frame + banner)")]
        [SerializeField] private Color _ownBoard = new Color(0.47f, 0.42f, 0.78f);
        [SerializeField] private Color _playerReplay = new Color(0.98f, 0.74f, 0.25f);
        [SerializeField] private Color _shortestReplay = new Color(0.62f, 0.61f, 0.72f);
        [SerializeField] private Color _roundOver = new Color(0.33f, 0.33f, 0.43f);
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
        [Tooltip("Main actions: Join, Lock In, Start.")]
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
        public Color Grout => _grout;
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
        public Color TileA => _tileA;
        public Color TileB => _tileB;
        public Color Wall => _wall;
        public Color Selection => _selection;
        public Color BannerText => _bannerText;

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

        public Color TargetColor(Target target) => target.Robot.HasValue ? RobotColor(target.Robot.Value) : _anyRobot;

        public Color FrameColor(BoardMode mode)
        {
            switch (mode)
            {
                case BoardMode.PlayerReplay: return _playerReplay;
                case BoardMode.ShortestReplay: return _shortestReplay;
                case BoardMode.RoundOver: return _roundOver;
                default: return _ownBoard;
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
