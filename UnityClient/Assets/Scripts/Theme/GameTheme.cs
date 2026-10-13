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
        [SerializeField] private Color _tileA = new Color(0.86f, 0.84f, 0.80f);
        [SerializeField] private Color _tileB = new Color(0.82f, 0.80f, 0.76f);
        [SerializeField] private Color _wall = new Color(0.15f, 0.15f, 0.18f);
        [SerializeField] private Color _selection = Color.white;

        [Header("Robots and targets")]
        [SerializeField] private Color _red = new Color(0.86f, 0.20f, 0.20f);
        [SerializeField] private Color _blue = new Color(0.20f, 0.40f, 0.90f);
        [SerializeField] private Color _green = new Color(0.15f, 0.65f, 0.30f);
        [SerializeField] private Color _yellow = new Color(0.95f, 0.75f, 0.10f);
        [Tooltip("The wildcard target any robot may reach.")]
        [SerializeField] private Color _anyRobot = new Color(0.60f, 0.30f, 0.80f);

        [Header("What the board shows (frame + banner)")]
        [SerializeField] private Color _ownBoard = new Color(0.10f, 0.65f, 0.75f);
        [SerializeField] private Color _playerReplay = new Color(1.00f, 0.72f, 0.15f);
        [SerializeField] private Color _shortestReplay = new Color(0.55f, 0.57f, 0.62f);
        [SerializeField] private Color _roundOver = new Color(0.40f, 0.42f, 0.46f);
        [SerializeField] private Color _bannerText = Color.black;

        [Header("Countdown")]
        [SerializeField] private Color _countdownDim = new Color(0f, 0f, 0f, 0.6f);
        [SerializeField] private Color _countdownText = Color.white;

        public Color TileA => _tileA;
        public Color TileB => _tileB;
        public Color Wall => _wall;
        public Color Selection => _selection;
        public Color BannerText => _bannerText;
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
