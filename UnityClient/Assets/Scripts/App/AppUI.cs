using System;
using System.Collections.Generic;
using Ricochet.Client.Board;
using Ricochet.Client.Game;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ricochet.Client.App
{
    /// <summary>
    /// The game's UI (UI Toolkit, made at runtime so the scene needn't change): a menu on the left, the board stage
    /// (goal, banner, the board itself, its controls) and a side panel whose screen depends on what the player is doing
    /// (home, practice; lobby, match and daily screens to come). Reads <see cref="GameController"/>,
    /// <see cref="DailyController"/> and the board session; the camera fits the board into the stage's board area. The
    /// look is <c>UIToolkit/Resources/Ricochet/App.uss</c> (palette variables at the top).
    /// </summary>
    public class AppUI : MonoBehaviour
    {
        private enum Screen { Home, Practice, Lobby, Daily }

        // The palette's ground, behind the board (the camera's clear colour).
        private static readonly Color Ground = new Color32(0x17, 0x1a, 0x29, 0xff);

        private GameController _game;
        private DailyController _daily;
        private VisualElement _root;
        private Screen _chosen = Screen.Home;

        private VisualElement _main, _boardArea, _boardOverlay, _goalIcon, _replayBar, _settings;
        private Label _goalLabel, _banner, _boardStatus, _overlayCaption, _overlayNumber, _overlayHint;
        private Button _navHome, _navDaily, _navPlay, _navPractice, _navSettings;
        private Button _undo, _reset, _newBoard, _shortest, _replayPause, _replayBack, _replayForward;
        private readonly Dictionary<Screen, VisualElement> _screens = new Dictionary<Screen, VisualElement>();
        private VisualElement _soonScreen;
        private Label _soonTitle, _soonText, _homeTitle, _homeDailyTitle, _homeDailyStreak, _homeStatus, _profileAvatar, _profileName;
        private Button _soonAction;
        private TextField _nameField, _codeField;
        private readonly List<(Button Switch, Func<bool> IsOn)> _switches = new List<(Button, Func<bool>)>();
        private readonly List<(Label Value, Button Minus, Button Plus)> _steppers = new List<(Label, Button, Button)>();
        private Action _soonClicked;

        private ScratchpadController Scratchpad => _game.Scratchpad;
        private BoardSession Session => _game.Scratchpad.Session;
        private GameTheme Theme => _game.Scratchpad.View.Theme;

        // ---- Start-up: the new UI replaces the old side panel and IMGUI overlays ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var network = FindFirstObjectByType<NetworkManager>();
            var scratchpad = FindFirstObjectByType<ScratchpadController>();
            if (network == null || scratchpad == null)
                return;

            // The old UI, until it's deleted: its panel (switched off before its Start, so it never auto-joins) and the
            // board's IMGUI overlay.
            var legacy = FindFirstObjectByType<UI.LobbyUI>();
            if (legacy != null)
            {
                Canvas canvas = legacy.GetComponentInParent<Canvas>();
                legacy.gameObject.SetActive(false);
                if (canvas != null)
                    canvas.rootCanvas.gameObject.SetActive(false);
            }
            var overlay = scratchpad.GetComponent<BoardOverlay>();
            if (overlay != null)
                overlay.enabled = false;

            var app = new GameObject("App");
            var game = app.AddComponent<GameController>();
            game.Init(network, scratchpad);
            app.AddComponent<UsageTracker>().Init(() => NetworkManager.NormalizeServerUrl(game.ServerUrl));
            var daily = app.AddComponent<DailyController>();
            daily.Init(game);
            app.AddComponent<AppUI>().Init(game, daily);
        }

        public void Init(GameController game, DailyController daily)
        {
            _game = game;
            _daily = daily;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Ricochet/RicochetTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1440, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.clearColor = false;

            var holder = new GameObject("UI Document");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var document = holder.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("Ricochet/App");
            holder.SetActive(true);
            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;

            Bind();
            Scratchpad.BoardArea = BoardAreaOnScreen;
            ScratchpadController.TypingInOtherUI = IsTyping;
        }

        private void Bind()
        {
            _main = _root.Q("main");
            _boardArea = _root.Q("board-area");
            _boardOverlay = _root.Q("board-overlay");
            _goalIcon = _root.Q("goal-icon");
            _goalLabel = _root.Q<Label>("goal-label");
            _banner = _root.Q<Label>("banner");
            _boardStatus = _root.Q<Label>("board-status");
            _overlayCaption = _root.Q<Label>("overlay-caption");
            _overlayNumber = _root.Q<Label>("overlay-number");
            _overlayHint = _root.Q<Label>("overlay-hint");
            _replayBar = _root.Q("replay-bar");
            _settings = _root.Q("settings");

            _navHome = Click("nav-home", () => Choose(Screen.Home));
            _navDaily = Click("nav-daily", OpenDaily);
            _navPlay = Click("nav-play", () =>
            {
                Choose(Screen.Home);
                _codeField.Focus();
            });
            _navPractice = Click("nav-practice", () => Choose(Screen.Practice));
            _navSettings = Click("nav-settings", () => _settings.EnableInClassList("is-open", !_settings.ClassListContains("is-open")));

            _undo = Click("control-undo", () => Scratchpad.Perform(ControlAction.Undo));
            _reset = Click("control-reset", () => Scratchpad.Perform(ControlAction.Reset));
            _newBoard = Click("control-new", () => Scratchpad.Perform(ControlAction.NewBoard));
            _shortest = Click("control-shortest", () => Scratchpad.Perform(ControlAction.Shortest));
            _replayPause = Click("replay-pause", () => Scratchpad.Perform(ControlAction.ReplayPause));
            _replayBack = Click("replay-back", () => Scratchpad.Perform(ControlAction.ReplayBack));
            _replayForward = Click("replay-forward", () => Scratchpad.Perform(ControlAction.ReplayForward));

            _screens[Screen.Home] = _root.Q("screen-home");
            _screens[Screen.Practice] = _root.Q("screen-practice");
            _soonScreen = _root.Q("screen-soon");
            _soonTitle = _root.Q<Label>("soon-title");
            _soonText = _root.Q<Label>("soon-text");
            _soonAction = Click("soon-action", () => _soonClicked?.Invoke());

            _homeTitle = _root.Q<Label>("home-title");
            _homeDailyTitle = _root.Q<Label>("home-daily-title");
            _homeDailyStreak = _root.Q<Label>("home-daily-streak");
            _homeStatus = _root.Q<Label>("home-status");
            _profileAvatar = _root.Q<Label>("profile-avatar");
            _profileName = _root.Q<Label>("profile-name");
            Click("home-daily-play", OpenDaily);
            Click("home-create", () => _game.CreateLobby(_nameField.value));
            Click("home-join", () => _game.Join(_codeField.value, _nameField.value));

            _nameField = _root.Q<TextField>("home-name");
            _nameField.value = _game.PlayerName;
            _nameField.RegisterValueChangedCallback(e => _game.PlayerName = e.newValue);
            _codeField = _root.Q<TextField>("home-code");
            // Lobby codes aren't case-sensitive; show them the way the lobby does.
            _codeField.RegisterValueChangedCallback(e =>
            {
                string upper = e.newValue.ToUpperInvariant();
                if (upper != e.newValue)
                    _codeField.SetValueWithoutNotify(upper);
            });
            _codeField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    _game.Join(_codeField.value, _nameField.value);
            });

            foreach (string slot in new[] { "home", "practice" })
            {
                AddSwitch(_root.Q($"{slot}-puzzle-row"), "Puzzle mode", () => PlayerSettings.PracticeExact,
                    () => Scratchpad.SetPracticeExact(!PlayerSettings.PracticeExact));
                AddStepper(_root.Q($"{slot}-stepper-slot"));
            }

            VisualElement settingsList = _root.Q("settings-list");
            AddSwitch(AddRow(settingsList), "Sound  (M)", () => !PlayerSettings.Muted, () => PlayerSettings.Muted = !PlayerSettings.Muted);
            AddSwitch(AddRow(settingsList), "Move dots", () => PlayerSettings.MovePreview, () => PlayerSettings.MovePreview = !PlayerSettings.MovePreview);
            AddSwitch(AddRow(settingsList), "Big burst", () => PlayerSettings.BigBurst, () => PlayerSettings.BigBurst = !PlayerSettings.BigBurst);
        }

        // A button that does `action`, then gives up focus: a focused button would take Space/Enter/arrow presses meant
        // for the board (moves, replay pause).
        private Button Click(string name, Action action)
        {
            var button = _root.Q<Button>(name);
            button.clicked += () =>
            {
                action();
                button.Blur();
            };
            return button;
        }

        private static VisualElement AddRow(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("setting-row");
            parent.Add(row);
            return row;
        }

        private void AddSwitch(VisualElement row, string label, Func<bool> isOn, Action toggle)
        {
            var text = new Label(label);
            text.AddToClassList("setting-label");
            row.Add(text);
            var button = new Button();
            button.AddToClassList("switch");
            var knob = new VisualElement();
            knob.AddToClassList("switch-knob");
            button.Add(knob);
            button.clicked += () =>
            {
                toggle();
                button.Blur();
            };
            row.Add(button);
            _switches.Add((button, isOn));
        }

        private void AddStepper(VisualElement slot)
        {
            var stepper = new VisualElement();
            stepper.AddToClassList("stepper");
            var minus = new Button { text = "−" };
            var plus = new Button { text = "+" };
            foreach (Button b in new[] { minus, plus })
            {
                b.AddToClassList("btn");
                b.AddToClassList("btn--small");
                b.AddToClassList("btn--square");
            }
            var value = new Label();
            value.AddToClassList("stepper-value");
            minus.clicked += () => { Scratchpad.SetPracticeMoves(PlayerSettings.PracticeMoves - 1); minus.Blur(); };
            plus.clicked += () => { Scratchpad.SetPracticeMoves(PlayerSettings.PracticeMoves + 1); plus.Blur(); };
            stepper.Add(minus);
            stepper.Add(value);
            stepper.Add(plus);
            slot.Add(stepper);
            _steppers.Add((value, minus, plus));
        }

        // ---- Actions ----

        private void Choose(Screen screen)
        {
            _chosen = screen;
            if (_daily.State.IsActive)
                _daily.Close(backToPractice: true);
        }

        private void OpenDaily()
        {
            if (_game.Match.InLobby)
                return;
            _daily.Open();
        }

        private Screen Current => _game.Match.InLobby ? Screen.Lobby : _daily.State.IsActive ? Screen.Daily : _chosen;

        // ---- Each frame: show the state ----

        private void Update()
        {
            if (_root == null)
                return;
            // The palette's ground behind the board (the board view sets its own when it builds a board).
            if (Scratchpad.Camera != null)
                Scratchpad.Camera.backgroundColor = Ground;

            Screen screen = Current;
            _main.EnableInClassList("main--home", screen == Screen.Home);
            foreach (KeyValuePair<Screen, VisualElement> pair in _screens)
                pair.Value.EnableInClassList("is-visible", pair.Key == screen);
            ShowSoon(screen);
            _navHome.EnableInClassList("is-active", screen == Screen.Home);
            _navDaily.EnableInClassList("is-active", screen == Screen.Daily);
            _navPlay.EnableInClassList("is-active", screen == Screen.Lobby);
            _navPractice.EnableInClassList("is-active", screen == Screen.Practice);
            _navSettings.EnableInClassList("is-active", _settings.ClassListContains("is-open"));

            ShowProfile();
            ShowHome();
            foreach ((Button button, Func<bool> isOn) in _switches)
                button.EnableInClassList("is-on", isOn());
            foreach ((Label value, Button minus, Button plus) in _steppers)
            {
                int moves = PlayerSettings.PracticeMoves;
                value.text = $"Solve in {moves}";
                minus.SetEnabled(PlayerSettings.PracticeExact && moves > MatchSettings.MinMinMoves);
                plus.SetEnabled(PlayerSettings.PracticeExact && moves < MatchSettings.MaxMinMoves);
            }

            if (Session.Round != null)
                ShowStage();
        }

        // Lobby and daily screens arrive in the next steps; until then a placeholder with a way back.
        private void ShowSoon(Screen screen)
        {
            bool soon = screen == Screen.Lobby || screen == Screen.Daily;
            _soonScreen.EnableInClassList("is-visible", soon);
            if (screen == Screen.Lobby)
            {
                _soonTitle.text = $"Lobby {_game.Match.LobbyCode}";
                _soonText.text = "You're in the lobby: the new lobby and match screens come in the next step. Leave to get back to practice.";
                _soonAction.text = "Leave lobby";
                _soonClicked = _game.Leave;
            }
            else if (screen == Screen.Daily)
            {
                DailyState daily = _daily.State;
                _soonTitle.text = daily.Number > 0 ? $"Daily Puzzle #{daily.Number}" : "Daily Puzzle";
                _soonText.text = daily.Stage == DailyStage.Error ? daily.Error
                    : daily.Stage == DailyStage.Loading ? "Loading today's puzzle..."
                    : "The board is live: solve it! The new daily screen (results, leaderboard, sharing) comes in a later step.";
                _soonAction.text = "Back to practice";
                _soonClicked = () => Choose(Screen.Practice);
            }
        }

        private void ShowProfile()
        {
            string name = _game.PlayerName;
            _profileName.text = name.Length > 0 ? name : "Guest";
            _profileAvatar.text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
        }

        private void ShowHome()
        {
            string name = _game.PlayerName;
            _homeTitle.text = name.Length > 0 ? $"Welcome back, {name}" : "Welcome to Ricochet";
            int streak = _daily.KnownStreak;
            _homeDailyTitle.text = "Daily Puzzle";
            _homeDailyStreak.text = $"{streak}-day streak";
            _homeDailyStreak.style.display = streak > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _homeStatus.text = _game.Status.StartsWith("Join failed") || _game.Status.StartsWith("Enter") ? _game.Status : "";
        }

        private void ShowStage()
        {
            GameTheme theme = Theme;
            Target target = Session.Round.Target;
            Color targetColor = theme.TargetColor(target);
            _goalIcon.style.backgroundImage = new StyleBackground(ShapeSprites.TargetSymbol(target.Robot));
            _goalIcon.style.unityBackgroundImageTintColor = targetColor;
            string robot = target.Robot.HasValue ? $"the {target.Robot.Value} robot" : "any robot";
            int best = Scratchpad.PracticeShortest;
            _goalLabel.text = $"Get <b><color=#{ColorUtility.ToHtmlStringRGB(Lighter(targetColor))}>{robot}</color></b> to its target"
                + (best > 0 ? $" in {best} moves" : "");

            Color frame = theme.FrameColor(Session.Mode, target);
            _banner.text = BannerText(Session);
            _banner.style.backgroundColor = frame;
            _banner.style.color = theme.TextOn(frame);

            bool practice = Session.IsPractice;
            _newBoard.style.display = practice ? DisplayStyle.Flex : DisplayStyle.None;
            _shortest.style.display = practice ? DisplayStyle.Flex : DisplayStyle.None;
            _undo.SetEnabled(Scratchpad.CanPerform(ControlAction.Undo));
            _reset.SetEnabled(Scratchpad.CanPerform(ControlAction.Reset));
            _newBoard.SetEnabled(Scratchpad.CanPerform(ControlAction.NewBoard));
            _shortest.SetEnabled(Scratchpad.CanPerform(ControlAction.Shortest));

            bool replay = Session.CanControlReplay;
            _replayBar.style.display = replay ? DisplayStyle.Flex : DisplayStyle.None;
            if (replay)
                _replayPause.text = !Session.ReplayPaused && !Session.ReplayFinished ? "Pause" : Session.ReplayFinished ? "Again" : "Play";

            _boardStatus.text = StatusText(best);
            ShowOverlay();
        }

        // The countdown before a board, or "Finding a 6-move board...", over the board itself.
        private void ShowOverlay()
        {
            string search = Scratchpad.SearchStatus;
            bool counting = Session.IsCountingDown;
            bool visible = counting || search != null;
            _boardOverlay.EnableInClassList("is-visible", visible);
            if (!visible)
                return;
            Rect board = BoardOnPanel();
            _boardOverlay.style.left = board.x;
            _boardOverlay.style.top = board.y;
            _boardOverlay.style.width = board.width;
            _boardOverlay.style.height = board.height;
            if (counting)
            {
                float left = Session.CountdownLeft(Time.time);
                _overlayCaption.text = left > 0f ? Session.CountdownCaption : "";
                _overlayNumber.text = left > 0f ? Mathf.CeilToInt(left).ToString() : "";
                _overlayHint.text = "";
            }
            else
            {
                _overlayCaption.text = search;
                _overlayNumber.text = "";
                _overlayHint.text = "N or Esc to stop";
            }
        }

        private string StatusText(int best)
        {
            int moves = Session.Moves.Count;
            string status = Session.IsReplaying ? ""
                : Session.IsSolved && best > 0 && moves <= best ? $"Solved in {moves} moves: optimal!"
                : Session.IsSolved && best > 0 ? $"Solved in {moves} moves (best is {best})"
                : Session.IsSolved ? $"Solved in {moves} moves!"
                : "Click a robot, then a dot to move it. Arrow keys and dragging work too.";
            if (Session.IsPractice && Session.HintText != null)
                status = Session.HintText;
            return status;
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
                    return session.Title ?? "Round over";
                default:
                    string board = session.Title ?? (session.Mode == BoardMode.Practice ? "Practice board" : "Your board");
                    return session.IsSolved ? $"{board}  ·  solved in {moves}!" : $"{board}  ·  {moves} {(moves == 1 ? "move" : "moves")}";
            }
        }

        private static Color Lighter(Color color) => Color.Lerp(color, Color.white, 0.25f);

        // ---- Geometry: the panel (UI units, y down) vs the screen (pixels, y up) ----

        private float PanelScale => _root.layout.width > 0f ? UnityEngine.Screen.width / _root.layout.width : 1f;

        // The stage's board area in screen pixels, for the camera to fit the board into.
        private Rect BoardAreaOnScreen()
        {
            Rect area = _boardArea.worldBound;
            float s = PanelScale;
            if (area.width <= 1f || area.height <= 1f)
                return new Rect(0f, 0f, UnityEngine.Screen.width, UnityEngine.Screen.height);
            return new Rect(area.x * s, UnityEngine.Screen.height - area.yMax * s, area.width * s, area.height * s);
        }

        // Where the board (with its frame) is drawn, in panel units.
        private Rect BoardOnPanel()
        {
            Camera camera = Scratchpad.Camera;
            GridState grid = Session.Round.Grid;
            float pad = BoardView.FrameThickness + 0.06f;
            Vector3 min = camera.WorldToScreenPoint(new Vector3(-pad, -pad));
            Vector3 max = camera.WorldToScreenPoint(new Vector3(grid.Width + pad, grid.Height + pad));
            float s = PanelScale;
            return Rect.MinMaxRect(min.x / s, (UnityEngine.Screen.height - max.y) / s, max.x / s, (UnityEngine.Screen.height - min.y) / s);
        }

        // Typing in a text field: the board ignores the keys.
        private bool IsTyping()
        {
            Focusable focused = _root?.panel?.focusController?.focusedElement;
            return focused is TextField || (focused is VisualElement element && element.GetFirstAncestorOfType<TextField>() != null);
        }
    }
}
