using Ricochet.Client.Game;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// Overlay around the board (IMGUI until the UI pass): the banner saying what the board shows, the
    /// countdown before a board, and the goal + controls card. Only reads the <see cref="BoardSession"/>;
    /// swap this class to change how it looks. Fonts and colours come from the <see cref="GameTheme"/>.
    /// </summary>
    [RequireComponent(typeof(ScratchpadController))]
    public class BoardOverlay : MonoBehaviour
    {
        private const int CardRadius = 14;
        private const float CardWidth = 340f;

        [SerializeField] private GameTheme _theme;

        private ScratchpadController _controller;
        private GUIStyle _bannerStyle;
        private GUIStyle _numberStyle;
        private GUIStyle _captionStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _goalStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _headerStyle;

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
            DrawCard(session);
        }

        private void CreateStyles()
        {
            if (_bannerStyle != null)
                return;
            Texture2D rounded = ShapeSprites.RoundedTexture(CardRadius);
            _bannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                font = Colors.BoldFont,
                fontSize = 20,
                normal = { background = rounded, textColor = Colors.BannerText },
                border = new RectOffset(CardRadius, CardRadius, CardRadius, CardRadius),
            };
            _numberStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = Colors.TitleFont };
            _captionStyle = new GUIStyle(_numberStyle) { font = Colors.BoldFont };
            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = rounded },
                border = new RectOffset(CardRadius, CardRadius, CardRadius, CardRadius),
            };
            _goalStyle = new GUIStyle(GUI.skin.label)
            {
                font = Colors.BoldFont, fontSize = 18, wordWrap = true, richText = true,
                normal = { textColor = Colors.PanelText },
            };
            _bodyStyle = new GUIStyle(_goalStyle) { font = Colors.Font, fontSize = 16 };
            _headerStyle = new GUIStyle(_goalStyle) { fontSize = 13, normal = { textColor = Colors.MutedText } };
        }

        // The board's corners in IMGUI coordinates (pixels, y down), including the frame.
        private static Rect BoardScreenRect(Camera camera, GridState grid)
        {
            float pad = BoardView.FrameThickness + 0.06f;
            Vector3 min = camera.WorldToScreenPoint(new Vector3(-pad, -pad));
            Vector3 max = camera.WorldToScreenPoint(new Vector3(grid.Width + pad, grid.Height + pad));
            return Rect.MinMaxRect(min.x, Screen.height - max.y, max.x, Screen.height - min.y);
        }

        // A rounded pill in the frame's colour, just above the board.
        private void DrawBanner(BoardSession session, Rect board)
        {
            const float height = 38f;
            var banner = new Rect(board.x + board.width * 0.15f, board.y - height - 8f, board.width * 0.7f, height);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Colors.FrameColor(session.Mode);
            GUI.Label(banner, BannerText(session), _bannerStyle);
            GUI.backgroundColor = previous;
        }

        private static string BannerText(BoardSession session)
        {
            int moves = session.Moves.Count;
            switch (session.Mode)
            {
                case BoardMode.PlayerReplay:
                case BoardMode.ShortestReplay:
                    ReplaySegment replay = session.CurrentReplay;
                    string who = replay.IsShortest ? "Shortest possible" : "Replay: " + replay.Label;
                    if (session.ReplayFinished)
                        return $"{who}  ·  {replay.Moves.Count} moves";
                    return session.ReplayStep == 0
                        ? $"{who}  ·  {replay.Moves.Count} moves"
                        : $"{who}  ·  move {session.ReplayStep} of {replay.Moves.Count}";
                case BoardMode.RoundOver:
                    return "Round over";
                default:
                    string board = session.Mode == BoardMode.Practice ? "Practice board" : "Your board";
                    return session.IsSolved ? $"{board}  ·  solved in {moves}!" : $"{board}  ·  {moves} {(moves == 1 ? "move" : "moves")}";
            }
        }

        // Dims the board under a big number, then "Go!" until the board arrives.
        private void DrawCountdown(BoardSession session, Rect board)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Colors.CountdownDim;
            GUI.Box(board, GUIContent.none, _cardStyle);
            GUI.backgroundColor = previous;

            _numberStyle.normal.textColor = Colors.CountdownText;
            _numberStyle.fontSize = Mathf.RoundToInt(board.height * 0.3f);
            _captionStyle.normal.textColor = Colors.CountdownText;
            _captionStyle.fontSize = Mathf.RoundToInt(board.height * 0.05f);

            float captionHeight = board.height * 0.12f;
            GUI.Label(new Rect(board.x, board.center.y - board.height * 0.28f, board.width, captionHeight),
                session.CountdownCaption, _captionStyle);
            float left = session.CountdownLeft(Time.time);
            GUI.Label(board, left > 0f ? Mathf.CeilToInt(left).ToString() : "Go!", _numberStyle);
        }

        // Top-left card: the goal (with the target's symbol), the move count, then the controls.
        private void DrawCard(BoardSession session)
        {
            const float pad = 18f;
            float inner = CardWidth - 2 * pad;
            Target target = session.Round.Target;
            Color targetColor = Colors.TargetColor(target);
            string robot = target.Robot.HasValue ? $"the {target.Robot.Value} robot" : "any robot";
            string goal = $"Get {Colorize(robot, targetColor)} to its target";

            int moves = session.Moves.Count;
            string status = session.IsReplaying ? $"Replaying a solution ({moves} moves)"
                : session.IsSolved ? $"<b>Solved in {moves} moves!</b>" : $"Moves: {moves}";
            if (session.IsPractice && session.HintText != null)
                status += "\n" + session.HintText;
            string controls = "Click, 1-4 or Tab   select\nArrows, WASD or drag   move\nZ   undo        R   reset" +
                (session.IsPractice ? "\nN   new board   H   shortest" : "");

            float goalHeight = Mathf.Max(28f, _goalStyle.CalcHeight(new GUIContent(goal), inner - 36f));
            float statusHeight = _bodyStyle.CalcHeight(new GUIContent(status), inner);
            float controlsHeight = _bodyStyle.CalcHeight(new GUIContent(controls), inner);
            float height = pad + 18f + goalHeight + 6f + statusHeight + 14f + 18f + controlsHeight + pad;

            var card = new Rect(16f, 16f, CardWidth, height);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Colors.Panel;
            GUI.Box(card, GUIContent.none, _cardStyle);
            GUI.backgroundColor = previous;

            float x = card.x + pad, y = card.y + pad;
            GUI.Label(new Rect(x, y, inner, 18f), "GOAL", _headerStyle);
            y += 18f;
            // The target's symbol in its colour, as on the board.
            Color previousColor = GUI.color;
            GUI.color = targetColor;
            GUI.DrawTexture(new Rect(x, y + 2f, 26f, 26f), ShapeSprites.TargetSymbol(target.Robot).texture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 36f, y, inner - 36f, goalHeight), goal, _goalStyle);
            y += goalHeight + 6f;
            GUI.Label(new Rect(x, y, inner, statusHeight), status, _bodyStyle);
            y += statusHeight + 14f;
            GUI.Label(new Rect(x, y, inner, 18f), "CONTROLS", _headerStyle);
            y += 18f;
            Color muted = _bodyStyle.normal.textColor;
            _bodyStyle.normal.textColor = Colors.MutedText;
            GUI.Label(new Rect(x, y, inner, controlsHeight), controls, _bodyStyle);
            _bodyStyle.normal.textColor = muted;
        }

        private static string Colorize(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
    }
}
