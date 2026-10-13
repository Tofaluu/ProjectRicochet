using Ricochet.Client.Game;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// Placeholder overlay around the board (IMGUI until the UI pass): the banner saying what the board
    /// shows, the countdown before a board, and the controls help. Only reads the
    /// <see cref="BoardSession"/>; swap this class to change how it looks.
    /// </summary>
    [RequireComponent(typeof(ScratchpadController))]
    public class BoardOverlay : MonoBehaviour
    {
        [SerializeField] private GameTheme _theme;

        private ScratchpadController _controller;
        private GUIStyle _bannerStyle;
        private GUIStyle _numberStyle;
        private GUIStyle _captionStyle;
        private GUIStyle _helpStyle;

        private GameTheme Colors => GameTheme.OrDefault(_theme);

        private void Awake() => _controller = GetComponent<ScratchpadController>();

        private void OnGUI()
        {
            BoardSession session = _controller.Session;
            Camera camera = _controller.Camera;
            if (session.Round == null || camera == null)
                return;
            CreateStyles();

            Rect board = BoardScreenRect(camera, session.Round.Grid);
            DrawBanner(session, board);
            if (session.IsCountingDown)
                DrawCountdown(session, board);
            DrawHelp(session);
        }

        private void CreateStyles()
        {
            if (_bannerStyle != null)
                return;
            _bannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
            };
            _numberStyle = new GUIStyle(_bannerStyle);
            _captionStyle = new GUIStyle(_bannerStyle);
            _helpStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, wordWrap = true };
        }

        // The board's corners in IMGUI coordinates (pixels, y down), including the frame.
        private static Rect BoardScreenRect(Camera camera, GridState grid)
        {
            float pad = BoardView.FrameThickness + 0.06f;
            Vector3 min = camera.WorldToScreenPoint(new Vector3(-pad, -pad));
            Vector3 max = camera.WorldToScreenPoint(new Vector3(grid.Width + pad, grid.Height + pad));
            return Rect.MinMaxRect(min.x, Screen.height - max.y, max.x, Screen.height - min.y);
        }

        // In the frame's colour, just above the board.
        private void DrawBanner(BoardSession session, Rect board)
        {
            const float height = 34f;
            var banner = new Rect(board.x, board.y - height - 4f, board.width, height);
            Fill(banner, Colors.FrameColor(session.Mode));
            _bannerStyle.normal.textColor = Colors.BannerText;
            GUI.Label(banner, BannerText(session), _bannerStyle);
        }

        private static string BannerText(BoardSession session)
        {
            int moves = session.Moves.Count;
            switch (session.Mode)
            {
                case BoardMode.PlayerReplay:
                case BoardMode.ShortestReplay:
                    ReplaySegment replay = session.CurrentReplay;
                    string who = replay.IsShortest ? "SHORTEST POSSIBLE" : "REPLAY: " + replay.Label;
                    if (session.ReplayFinished)
                        return $"{who}  ·  {replay.Moves.Count} moves  ·  finished";
                    return session.ReplayStep == 0
                        ? $"{who}  ·  {replay.Moves.Count} moves"
                        : $"{who}  ·  move {session.ReplayStep} of {replay.Moves.Count}";
                case BoardMode.RoundOver:
                    return "ROUND OVER";
                default:
                    string board = session.Mode == BoardMode.Practice ? "PRACTICE BOARD" : "YOUR BOARD";
                    return session.IsSolved ? $"{board}  ·  solved in {moves} moves!" : $"{board}  ·  {moves} moves";
            }
        }

        // Dims the board under a big number, then "Go!" until the board arrives.
        private void DrawCountdown(BoardSession session, Rect board)
        {
            Fill(board, Colors.CountdownDim);
            _numberStyle.normal.textColor = Colors.CountdownText;
            _numberStyle.fontSize = Mathf.RoundToInt(board.height * 0.3f);
            _captionStyle.normal.textColor = Colors.CountdownText;
            _captionStyle.fontSize = Mathf.RoundToInt(board.height * 0.06f);

            float captionHeight = board.height * 0.12f;
            GUI.Label(new Rect(board.x, board.center.y - board.height * 0.28f, board.width, captionHeight),
                session.CountdownCaption, _captionStyle);
            float left = session.CountdownLeft(Time.time);
            GUI.Label(board, left > 0f ? Mathf.CeilToInt(left).ToString() : "Go!", _numberStyle);
        }

        private void DrawHelp(BoardSession session)
        {
            Target target = session.Round.Target;
            string goal = target.Robot.HasValue ? target.Robot.Value + " robot" : "any robot";
            int moves = session.Moves.Count;
            string status = session.IsReplaying ? $"Replaying solution ({moves} moves)"
                : session.IsSolved ? $"SOLVED in {moves} moves!" : $"Moves: {moves}";
            bool practice = session.IsPractice;
            string text =
                $"Get {goal} to the big diamond\n" +
                $"{status}\n" +
                $"Selected: {session.Selected}\n" +
                (session.Seed != 0 ? $"Seed: {session.Seed}\n" : "Online board\n") +
                (practice && session.HintText != null ? session.HintText + "\n" : "") + "\n" +
                "Click / 1-4 / Tab: select\nArrows / WASD / drag: move\nZ: undo   R: reset" +
                (practice ? "\nN: new board   H: show shortest solution" : "");
            GUI.Box(new Rect(10, 10, 320, 240), text, _helpStyle);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
