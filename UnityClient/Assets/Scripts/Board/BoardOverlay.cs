using System.Collections.Generic;
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
        private const float HeaderHeight = 22f;
        private const float CardMargin = 16f;
        private const float ToggleWidth = 160f;
        private const float ToggleHeight = 30f;

        [SerializeField] private GameTheme _theme;

        private ScratchpadController _controller;
        private GUIStyle _bannerStyle;
        private GUIStyle _numberStyle;
        private GUIStyle _captionStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _goalStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _toggleStyle;

        private GameTheme Colors => GameTheme.OrDefault(_theme);

        /// <summary>
        /// The card's and banner's scale, following the side panel's (its canvas scales with the screen from
        /// 1920x1080). No upper limit: on high-DPI screens (e.g. a Retina Mac's browser) Screen counts
        /// physical pixels, so fixed pixel sizes would come out tiny.
        /// </summary>
        public static float UiScale => Mathf.Max(0.7f, Mathf.Sqrt(Screen.width / 1920f * Screen.height / 1080f));

        /// <summary>Screen pixels on the left taken by the card; the camera keeps the board right of it.</summary>
        public float LeftInsetPixels => (CardMargin * 2f + CardWidth) * UiScale;

        /// <summary>Bottom of the card and toggles, in unscaled GUI units (multiply by <see cref="UiScale"/>), for what goes under them.</summary>
        public float LeftColumnBottom { get; private set; } = CardMargin;

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
            else if (_controller.SearchStatus != null)
                DrawSearching(_controller.SearchStatus, board);
            // The card and the toggles scale with the screen like the side panel does.
            Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
            Rect card = DrawCard(session);
            float y = card.yMax + 10f;
            if (DrawToggle(new Rect(card.x, y, ToggleWidth, ToggleHeight), "Sound", !PlayerSettings.Muted, "M"))
                PlayerSettings.Muted = !PlayerSettings.Muted;
            y += ToggleHeight + 6f;
            if (DrawToggle(new Rect(card.x, y, ToggleWidth, ToggleHeight), "Move dots", PlayerSettings.MovePreview))
                PlayerSettings.MovePreview = !PlayerSettings.MovePreview;
            y += ToggleHeight + 6f;
            if (DrawToggle(new Rect(card.x, y, ToggleWidth, ToggleHeight), "Big burst", PlayerSettings.BigBurst))
                PlayerSettings.BigBurst = !PlayerSettings.BigBurst;
            if (session.IsPractice)
            {
                y += ToggleHeight + 6f;
                if (DrawToggle(new Rect(card.x, y, ToggleWidth, ToggleHeight), "Puzzle mode", PlayerSettings.PracticeExact))
                    _controller.SetPracticeExact(!PlayerSettings.PracticeExact);
                if (PlayerSettings.PracticeExact)
                {
                    y += ToggleHeight + 6f;
                    int moves = PlayerSettings.PracticeMoves;
                    int change = DrawStepper(new Rect(card.x, y, ToggleWidth, ToggleHeight), $"Solve in {moves}",
                        moves > MatchSettings.MinMinMoves, moves < MatchSettings.MaxMinMoves);
                    if (change != 0)
                        _controller.SetPracticeMoves(moves + change);
                }
            }
            LeftColumnBottom = y + ToggleHeight;
            GUI.matrix = matrix;
        }

        // A pill under the card ("Sound on  ·  M"); true when clicked.
        private bool DrawToggle(Rect rect, string label, bool on, string key = null)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(on ? Colors.Panel : Colors.Input);
            _toggleStyle.normal.textColor = on ? Colors.PanelText : Colors.MutedText;
            _toggleStyle.hover.textColor = Colors.PanelText;
            string text = $"{label} {(on ? "on" : "off")}" + (key != null ? $"  ·  {key}" : "");
            bool clicked = GUI.Button(rect, text, _toggleStyle);
            GUI.backgroundColor = previous;
            return clicked;
        }

        // A pill with − and + at its ends ("−  Solve in 6  +"); -1 / +1 when one is clicked, else 0.
        // A button that can't be used (at the range's end) is greyed out.
        private int DrawStepper(Rect rect, string label, bool canDecrease, bool canIncrease)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.Panel);
            _toggleStyle.normal.textColor = Colors.PanelText;
            _toggleStyle.hover.textColor = Colors.PanelText;
            GUI.Box(rect, label, _toggleStyle);
            GUI.backgroundColor = Tint(Colors.Input);
            float button = rect.height - 6f;
            int change = 0;
            if (StepButton(new Rect(rect.x + 3f, rect.y + 3f, button, button), "\u2212", canDecrease))
                change = -1;
            if (StepButton(new Rect(rect.xMax - 3f - button, rect.y + 3f, button, button), "+", canIncrease))
                change = 1;
            GUI.backgroundColor = previous;
            return change;
        }

        private bool StepButton(Rect rect, string text, bool enabled)
        {
            bool wasEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            GUI.enabled = enabled;
            if (!enabled)
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
            _toggleStyle.normal.textColor = enabled ? Colors.PanelText : Colors.MutedText;
            bool clicked = GUI.Button(rect, text, _toggleStyle);
            GUI.enabled = wasEnabled;
            GUI.color = previousColor;
            return clicked && enabled;
        }

        // Dims the board while a practice board is being searched for, with what it's looking for.
        private void DrawSearching(string status, Rect board)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.CountdownDim);
            GUI.Box(board, GUIContent.none, _cardStyle);
            GUI.backgroundColor = previous;

            _captionStyle.normal.textColor = Colors.CountdownText;
            _captionStyle.fontSize = Mathf.RoundToInt(board.height * 0.045f);
            GUI.Label(board, status + "\n<size=" + Mathf.RoundToInt(board.height * 0.03f) + ">N or Esc to stop</size>", _captionStyle);
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
            _captionStyle = new GUIStyle(_numberStyle) { font = Colors.BoldFont, richText = true };
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
            _headerStyle = new GUIStyle(_goalStyle) { fontSize = 13, clipping = TextClipping.Overflow, normal = { textColor = Colors.MutedText } };
            _keyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, font = Colors.BoldFont, fontSize = 13, wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { background = ShapeSprites.RoundedTexture(6), textColor = Colors.PanelText },
                border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(9, 9, 0, 0),
            };
            _toggleStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter, font = Colors.BoldFont, fontSize = 13,
                normal = { background = rounded }, hover = { background = rounded }, active = { background = rounded },
                border = new RectOffset(CardRadius, CardRadius, CardRadius, CardRadius),
            };
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
            float height = 38f * UiScale;
            var banner = new Rect(board.x + board.width * 0.15f, board.y - height - 8f * UiScale, board.width * 0.7f, height);
            _bannerStyle.fontSize = Mathf.RoundToInt(20f * UiScale);
            Color previous = GUI.backgroundColor;
            Color frame = Colors.FrameColor(session.Mode, session.Round.Target);
            GUI.backgroundColor = Tint(frame);
            _bannerStyle.normal.textColor = Colors.TextOn(frame);
            GUI.Label(banner, BannerText(session), _bannerStyle);
            GUI.backgroundColor = previous;
            if (session.CanControlReplay)
                DrawReplayControls(session, banner);
        }

        // Practice replays: [Pause][<] left of the banner, [>] right of it (also Space, comma, period).
        private void DrawReplayControls(BoardSession session, Rect banner)
        {
            float gap = 6f * UiScale, size = banner.height, wide = 74f * UiScale;
            int fontSize = _toggleStyle.fontSize;
            _toggleStyle.fontSize = Mathf.RoundToInt(15f * UiScale);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.Panel);
            _toggleStyle.normal.textColor = Colors.PanelText;
            _toggleStyle.hover.textColor = Colors.PanelText;

            bool playing = !session.ReplayPaused && !session.ReplayFinished;
            var back = new Rect(banner.x - gap - size, banner.y, size, size);
            var pause = new Rect(back.x - gap - wide, banner.y, wide, size);
            var forward = new Rect(banner.xMax + gap, banner.y, size, size);
            if (GUI.Button(pause, playing ? "Pause" : session.ReplayFinished ? "Again" : "Play", _toggleStyle))
                _controller.ToggleReplayPause();
            if (GUI.Button(back, "<", _toggleStyle))
                _controller.StepReplay(-1);
            if (GUI.Button(forward, ">", _toggleStyle))
                _controller.StepReplay(1);

            GUI.backgroundColor = previous;
            _toggleStyle.fontSize = fontSize;
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
                    string paused = session.ReplayPaused ? "  ·  paused" : "";
                    return session.ReplayStep == 0
                        ? $"{who}  ·  {replay.Moves.Count} moves{paused}"
                        : $"{who}  ·  move {session.ReplayStep} of {replay.Moves.Count}{paused}";
                case BoardMode.RoundOver:
                    return "Round over";
                default:
                    string board = session.Mode == BoardMode.Practice ? "Practice board" : "Your board";
                    return session.IsSolved ? $"{board}  ·  solved in {moves}!" : $"{board}  ·  {moves} {(moves == 1 ? "move" : "moves")}";
            }
        }

        // Dims the board under a big number; at zero it stays dimmed (empty) until the board arrives, which
        // plays the start tone (ScratchpadController.LoadRound).
        private void DrawCountdown(BoardSession session, Rect board)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.CountdownDim);
            GUI.Box(board, GUIContent.none, _cardStyle);
            GUI.backgroundColor = previous;

            float left = session.CountdownLeft(Time.time);
            if (left <= 0f)
                return;
            _numberStyle.normal.textColor = Colors.CountdownText;
            _numberStyle.fontSize = Mathf.RoundToInt(board.height * 0.3f);
            _captionStyle.normal.textColor = Colors.CountdownText;
            _captionStyle.fontSize = Mathf.RoundToInt(board.height * 0.05f);

            float captionHeight = board.height * 0.12f;
            GUI.Label(new Rect(board.x, board.center.y - board.height * 0.28f, board.width, captionHeight),
                session.CountdownCaption, _captionStyle);
            GUI.Label(board, Mathf.CeilToInt(left).ToString(), _numberStyle);
        }

        // Top-left card: the goal (with the target's symbol), the move count, then the controls as
        // "action  [key] [key]" rows.
        private Rect DrawCard(BoardSession session)
        {
            const float pad = 18f, rowHeight = 30f, actionWidth = 104f;
            float inner = CardWidth - 2 * pad;
            Target target = session.Round.Target;
            Color targetColor = Colors.TargetColor(target);
            string robot = target.Robot.HasValue ? $"the {target.Robot.Value} robot" : "any robot";
            // A practice board of a known length says it ("solve in N", like a chess puzzle's "mate in N").
            int best = _controller.PracticeShortest;
            string goal = $"Get {Colorize(robot, targetColor)} to its target" + (best > 0 ? $" in {best} moves" : "");

            int moves = session.Moves.Count;
            string status = session.IsReplaying ? $"Replaying a solution ({moves} moves)"
                : session.IsSolved && best > 0 && moves <= best ? $"<b>Solved in {moves} moves: optimal!</b>"
                : session.IsSolved && best > 0 ? $"<b>Solved in {moves} moves</b> (best is {best})"
                : session.IsSolved ? $"<b>Solved in {moves} moves!</b>"
                : best > 0 ? $"Moves: {moves} of {best}" : $"Moves: {moves}";
            if (session.IsPractice && session.HintText != null)
                status += "\n" + session.HintText;

            var controls = new List<(string action, string[] keys)>
            {
                ("Select", new[] { "Click", "1-4", "Tab" }),
                ("Move", new[] { "Arrows", "WASD", "Drag" }),
                ("Undo / reset", new[] { "Z", "R" }),
            };
            if (session.IsPractice)
            {
                controls.Add(("New board", new[] { "N" }));
                controls.Add(("Shortest", new[] { "H" }));
            }
            if (session.CanControlReplay)
                controls.Add(("Replay", new[] { "Space", ",", "." }));

            float goalHeight = Mathf.Max(28f, _goalStyle.CalcHeight(new GUIContent(goal), inner - 36f));
            float statusHeight = _bodyStyle.CalcHeight(new GUIContent(status), inner);
            float height = pad + HeaderHeight + goalHeight + 6f + statusHeight + 14f + HeaderHeight + 4f + controls.Count * rowHeight + pad - 4f;

            var card = new Rect(CardMargin, CardMargin, CardWidth, height);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.Panel);
            GUI.Box(card, GUIContent.none, _cardStyle);

            float x = card.x + pad, y = card.y + pad;
            GUI.Label(new Rect(x, y, inner, HeaderHeight), "GOAL", _headerStyle);
            y += HeaderHeight;
            // The target's symbol in its colour, as on the board.
            Color previousColor = GUI.color;
            GUI.color = Tint(targetColor);
            GUI.DrawTexture(new Rect(x, y + 2f, 26f, 26f), ShapeSprites.TargetSymbol(target.Robot).texture);
            GUI.color = previousColor;
            GUI.Label(new Rect(x + 36f, y, inner - 36f, goalHeight), goal, _goalStyle);
            y += goalHeight + 6f;
            GUI.Label(new Rect(x, y, inner, statusHeight), status, _bodyStyle);
            y += statusHeight + 14f;

            GUI.Label(new Rect(x, y, inner, HeaderHeight), "CONTROLS", _headerStyle);
            y += HeaderHeight + 4f;
            Color body = _bodyStyle.normal.textColor;
            _bodyStyle.normal.textColor = Colors.MutedText;
            GUI.backgroundColor = Tint(Colors.Input);
            foreach ((string action, string[] keys) in controls)
            {
                GUI.Label(new Rect(x, y, actionWidth, rowHeight - 4f), action, _bodyStyle);
                float keyX = x + actionWidth;
                foreach (string key in keys)
                {
                    float width = _keyStyle.CalcSize(new GUIContent(key)).x + 2f;
                    GUI.Label(new Rect(keyX, y + 1f, width, rowHeight - 8f), key, _keyStyle);
                    keyX += width + 6f;
                }
                y += rowHeight;
            }
            _bodyStyle.normal.textColor = body;
            GUI.backgroundColor = previous;
            return card;
        }

        // In browser builds (linear colour space) IMGUI gamma-corrects tint colours once more than on desktop,
        // washing backgrounds out; text colours aren't affected. Pre-linearize the tints there.
        private static Color Tint(Color color)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                return color.linear;
#endif
            return color;
        }

        private static string Colorize(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
    }
}
