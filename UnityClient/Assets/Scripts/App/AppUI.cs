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
        private enum Screen { Home, Play, Practice, Lobby, Match, Daily }

        // Avatar colours, one per player in join order.
        private static readonly RobotColor[] AvatarColors = { RobotColor.Blue, RobotColor.Red, RobotColor.Green, RobotColor.Yellow };

        // The palette's ground, behind the board (the camera's clear colour).
        private static readonly Color Ground = new Color32(0x17, 0x1a, 0x29, 0xff);

        private GameController _game;
        private DailyController _daily;
        private VisualElement _root;
        private Screen _chosen = Screen.Home;

        private VisualElement _main, _boardArea, _boardOverlay, _goalIcon, _replayBar, _settings;
        private Label _goalLabel, _banner, _boardTip, _overlayCaption, _overlayNumber, _overlayHint;
        private Button _navHome, _navDaily, _navPlay, _navPractice, _navSettings;
        private Button _undo, _reset, _newBoard, _shortest, _replayPause, _replayBack, _replayForward;
        private readonly Dictionary<Screen, VisualElement> _screens = new Dictionary<Screen, VisualElement>();
        private VisualElement _dailySquares, _dailyRows, _dailyStats, _dailyBoard;
        private Label _dailyHeader, _dailyHeadline, _dailyMoves, _dailyMovesLabel, _dailyTime, _dailyStreak, _dailyDetail, _dailyNotice, _dailyRank, _dailyNext;
        private Button _dailyCopy, _dailyRetry;
        private string _shownSquares, _shownRows;
        private Label _homeTitle, _homeDailyTitle, _homeDailyStreak, _playStatus, _profileAvatar, _profileName;
        private VisualElement _lobbyPlayers, _lobbySettings, _lobbyHistory, _historySolutions, _matchPlayers;
        private Label _lobbyTitle, _lobbySub, _lobbyCode, _lobbyCopied, _lobbyPlayersSub, _lobbySummary, _lobbyFeedback;
        private Label _historyBoard, _historyWinner, _matchTitle, _matchSub, _timerCaption, _timerValue, _timerSub, _matchBest, _matchFeedback;
        private Button _lobbyStart, _historyPrev, _historyNext, _matchWatch, _matchDone;
        private readonly List<Action> _settingRefreshers = new List<Action>();
        private string _shownLobbyPlayers, _shownMatchPlayers, _shownHistory;
        private int _historyIndex = -1;
        private float _copiedUntil;
        private ScrollView _side;
        private float _scrollTarget, _scrollSetTo;
        private bool _scrollGliding;
        private const float WheelStep = 60f, GlideRate = 18f;
        private TextField _nameField, _codeField;
        private readonly List<(Button Switch, Func<bool> IsOn)> _switches = new List<(Button, Func<bool>)>();
        private readonly List<(Label Value, Button Minus, Button Plus)> _steppers = new List<(Label, Button, Button)>();

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

            // Tab picks robots, so UI Toolkit's own Tab focus cycling (through every button, then the text fields, where the
            // board stops taking keys) is off; Tab only hops between the name and code fields while typing in one.
            _root.RegisterCallback<NavigationMoveEvent>(OnTabNavigation, TrickleDown.TrickleDown);

            Bind();
            Scratchpad.BoardArea = BoardAreaOnScreen;
            ScratchpadController.TypingInOtherUI = IsTyping;
        }

        private void OnTabNavigation(NavigationMoveEvent e)
        {
            if (e.direction != NavigationMoveEvent.Direction.Next && e.direction != NavigationMoveEvent.Direction.Previous)
                return;
            _root.focusController.IgnoreEvent(e);
            e.StopPropagation();
            if (!IsTyping())
                return;
            var focused = _root.focusController.focusedElement as VisualElement;
            bool inName = focused == _nameField || focused?.GetFirstAncestorOfType<TextField>() == _nameField;
            (inName ? _codeField : _nameField).Focus();
        }

        private void Bind()
        {
            _main = _root.Q("main");
            _boardArea = _root.Q("board-area");
            _boardOverlay = _root.Q("board-overlay");
            _goalIcon = _root.Q("goal-icon");
            _goalLabel = _root.Q<Label>("goal-label");
            _banner = _root.Q<Label>("banner");
            _boardTip = _root.Q<Label>("board-tip");
            _overlayCaption = _root.Q<Label>("overlay-caption");
            _overlayNumber = _root.Q<Label>("overlay-number");
            _overlayHint = _root.Q<Label>("overlay-hint");
            _replayBar = _root.Q("replay-bar");
            _settings = _root.Q("settings");
            // The side panel scrolls smoothly: wheel and trackpad events move a target the panel glides to (the
            // ScrollView's own wheel handling jumps a fixed step per event, which stutters on a trackpad).
            _side = _root.Q<ScrollView>("side");
            _side.RegisterCallback<WheelEvent>(OnSideWheel, TrickleDown.TrickleDown);

            _navHome = Click("nav-home", () => Choose(Screen.Home));
            _navDaily = Click("nav-daily", OpenDaily);
            _navPlay = Click("nav-play", () => Choose(Screen.Play));
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
            _screens[Screen.Play] = _root.Q("screen-play");
            _screens[Screen.Practice] = _root.Q("screen-practice");
            _screens[Screen.Daily] = _root.Q("screen-daily");
            _dailyHeader = _root.Q<Label>("daily-header");
            _dailyHeadline = _root.Q<Label>("daily-headline");
            _dailyStats = _root.Q("daily-stats");
            _dailyMoves = _root.Q<Label>("daily-moves");
            _dailyMovesLabel = _root.Q<Label>("daily-moves-label");
            _dailyTime = _root.Q<Label>("daily-time");
            _dailyStreak = _root.Q<Label>("daily-streak");
            _dailyDetail = _root.Q<Label>("daily-detail");
            _dailySquares = _root.Q("daily-squares");
            _dailyNotice = _root.Q<Label>("daily-notice");
            _dailyBoard = _root.Q("daily-board");
            _dailyRank = _root.Q<Label>("daily-rank");
            _dailyRows = _root.Q("daily-rows");
            _dailyNext = _root.Q<Label>("daily-next");
            _dailyCopy = Click("daily-copy", _daily.CopyResult);
            _dailyRetry = Click("daily-retry", _daily.Open);
            Click("daily-back", () => Choose(Screen.Practice));
            BindLobby();

            _homeTitle = _root.Q<Label>("home-title");
            _homeDailyTitle = _root.Q<Label>("home-daily-title");
            _homeDailyStreak = _root.Q<Label>("home-daily-streak");
            _playStatus = _root.Q<Label>("play-status");
            _profileAvatar = _root.Q<Label>("profile-avatar");
            _profileName = _root.Q<Label>("profile-name");
            Click("home-daily-play", OpenDaily);
            Click("home-play", () => Choose(Screen.Play));
            Click("play-create", () => _game.CreateLobby(_nameField.value));
            Click("play-join", () => _game.Join(_codeField.value, _nameField.value));

            _nameField = _root.Q<TextField>("play-name");
            _nameField.value = _game.PlayerName;
            _nameField.RegisterValueChangedCallback(e => _game.PlayerName = e.newValue);
            _codeField = _root.Q<TextField>("play-code");
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

        private void BindLobby()
        {
            _screens[Screen.Lobby] = _root.Q("screen-lobby");
            _screens[Screen.Match] = _root.Q("screen-match");
            _lobbyTitle = _root.Q<Label>("lobby-title");
            _lobbySub = _root.Q<Label>("lobby-sub");
            _lobbyCode = _root.Q<Label>("lobby-code");
            _lobbyCopied = _root.Q<Label>("lobby-copied");
            _lobbyPlayers = _root.Q("lobby-players");
            _lobbyPlayersSub = _root.Q<Label>("lobby-players-sub");
            _lobbySettings = _root.Q("lobby-settings");
            _lobbySummary = _root.Q<Label>("lobby-summary");
            _lobbyFeedback = _root.Q<Label>("lobby-feedback");
            _lobbyHistory = _root.Q("lobby-history");
            _historyBoard = _root.Q<Label>("history-board");
            _historyWinner = _root.Q<Label>("history-winner");
            _historySolutions = _root.Q("history-solutions");
            _matchTitle = _root.Q<Label>("match-title");
            _matchSub = _root.Q<Label>("match-sub");
            _timerCaption = _root.Q<Label>("timer-caption");
            _timerValue = _root.Q<Label>("timer-value");
            _timerSub = _root.Q<Label>("timer-sub");
            _matchBest = _root.Q<Label>("match-best");
            _matchFeedback = _root.Q<Label>("match-feedback");
            _matchPlayers = _root.Q("match-players");

            Click("lobby-copy", () =>
            {
                Clipboard.Copy($"https://ricochet.thomasliu.dev/?lobby={_game.Match.LobbyCode}");
                _copiedUntil = Time.time + 2.5f;
            });
            _lobbyStart = Click("lobby-start", _game.StartMatch);
            Click("lobby-leave", _game.Leave);
            Click("match-leave", _game.Leave);
            _matchWatch = Click("match-watch", _game.WatchBest);
            _matchDone = Click("match-done", _game.ToggleDone);
            _historyPrev = Click("history-prev", () => SelectHistory(_historyIndex - 1));
            _historyNext = Click("history-next", () => SelectHistory(_historyIndex + 1));

            // The host's settings: steppers send the change at once (the server clamps it and echoes it back).
            VisualElement list = _root.Q("lobby-settings-list");
            AddSettingStepper(list, "Points to win", s => s.PointsToWin, (s, v) => s.PointsToWin = v, 1,
                MatchSettings.MinPointsToWin, MatchSettings.MaxPointsToWin, v => v.ToString());
            AddSettingStepper(list, "Timer after the first solution", s => s.FirstTimerSeconds, (s, v) => s.FirstTimerSeconds = v, 10,
                MatchSettings.MinTimerSeconds, MatchSettings.MaxTimerSeconds, v => $"{v} s");
            AddSettingStepper(list, "Extra time for a better one", s => s.ExtensionSeconds, (s, v) => s.ExtensionSeconds = v, 5,
                MatchSettings.MinTimerSeconds, MatchSettings.MaxTimerSeconds, v => $"{v} s");
            AddSettingStepper(list, "Time to find a first solution", s => s.RoundLimitSeconds, (s, v) => s.RoundLimitSeconds = v, 60,
                0, MatchSettings.MaxRoundLimitSeconds, v => v <= 0 ? "No limit" : $"{v / 60} min");
            AddSettingStepper(list, "Fewest moves per board", s => s.MinMoves, (s, v) => s.MinMoves = v, 1,
                MatchSettings.MinMinMoves, MatchSettings.MaxMinMoves, v => v.ToString());
            AddSettingStepper(list, "Most moves per board", s => s.MaxMoves, (s, v) => s.MaxMoves = v, 1,
                MatchSettings.MinMinMoves, MatchSettings.MaxMinMoves, v => v.ToString());
            AddSwitch(AddRow(list), "Show the best solution", () => _game.Match.Settings != null && _game.Match.Settings.ShowBestSolution,
                () => EditSettings(s => s.ShowBestSolution = !s.ShowBestSolution));
        }

        private void AddSettingStepper(VisualElement parent, string label, Func<MatchSettings, int> get, Action<MatchSettings, int> set,
            int step, int min, int max, Func<int, string> format)
        {
            VisualElement row = AddRow(parent);
            var text = new Label(label);
            text.AddToClassList("setting-label");
            text.style.flexShrink = 1;
            text.style.flexGrow = 1;
            text.style.flexBasis = 0;
            text.style.whiteSpace = WhiteSpace.Normal;
            row.Add(text);
            var stepper = new VisualElement();
            stepper.AddToClassList("mini-stepper");
            var minus = new Button { text = "\u2212" };
            var plus = new Button { text = "+" };
            minus.AddToClassList("btn");
            plus.AddToClassList("btn");
            var value = new Label();
            value.AddToClassList("mini-stepper-value");
            minus.clicked += () => { EditSettings(s => set(s, Step(get(s), -step, min, max))); minus.Blur(); };
            plus.clicked += () => { EditSettings(s => set(s, Step(get(s), step, min, max))); plus.Blur(); };
            stepper.Add(minus);
            stepper.Add(value);
            stepper.Add(plus);
            row.Add(stepper);
            _settingRefreshers.Add(() =>
            {
                MatchSettings settings = _game.Match.Settings;
                if (settings == null)
                    return;
                int current = get(settings);
                value.text = format(current);
                minus.SetEnabled(current > min);
                plus.SetEnabled(current < max);
            });
        }

        // One step up or down within [min, max]; the round limit jumps between "no limit" (0) and its 1-minute minimum.
        private static int Step(int value, int delta, int min, int max)
        {
            int next = value + delta;
            if (min == 0 && delta > 0 && value <= 0)
                next = MatchSettings.MinRoundLimitSeconds;
            else if (min == 0 && delta < 0 && next < MatchSettings.MinRoundLimitSeconds)
                next = 0;
            return Math.Max(min, Math.Min(max, next));
        }

        private void EditSettings(Action<MatchSettings> change)
        {
            MatchSettings settings = _game.Match.Settings;
            if (!_game.Match.IAmHost || settings == null)
                return;
            MatchSettings edited = GameController.Copy(settings);
            change(edited);
            _game.UpdateSettings(edited);
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

        private void OnSideWheel(WheelEvent e)
        {
            float max = Mathf.Max(0f, _side.contentContainer.layout.height - _side.contentViewport.layout.height);
            if (!_scrollGliding)
                _scrollTarget = _side.scrollOffset.y;
            _scrollTarget = Mathf.Clamp(_scrollTarget + e.delta.y * WheelStep, 0f, max);
            _scrollSetTo = _side.scrollOffset.y;
            _scrollGliding = true;
            e.StopPropagation(); // not the ScrollView's own step-per-event scrolling too
        }

        // Eases the side panel towards the wheel's target; dragging the scrollbar meanwhile takes over.
        private void GlideSide()
        {
            if (!_scrollGliding)
                return;
            float current = _side.scrollOffset.y;
            if (Mathf.Abs(current - _scrollSetTo) > 1f)
            {
                _scrollGliding = false; // the player moved it themselves
                return;
            }
            float next = Mathf.Lerp(current, _scrollTarget, 1f - Mathf.Exp(-GlideRate * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - _scrollTarget) < 0.5f)
            {
                next = _scrollTarget;
                _scrollGliding = false;
            }
            _side.scrollOffset = new Vector2(_side.scrollOffset.x, next);
            _scrollSetTo = _side.scrollOffset.y;
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

        private Screen Current => _game.Match.InLobby ? (_game.Match.Stage == MatchStage.BetweenMatches ? Screen.Lobby : Screen.Match)
            : _daily.State.IsActive ? Screen.Daily : _chosen;

        // ---- Each frame: show the state ----

        private void Update()
        {
            if (_root == null)
                return;
            // The palette's ground behind the board (the board view sets its own when it builds a board).
            if (Scratchpad.Camera != null)
                Scratchpad.Camera.backgroundColor = Ground;

            CloseSettingsOnOutsideClick();
            GlideSide();
            Screen screen = Current;
            // Puzzle mode is the Practice side's: Play with friends and the lobby get plain random boards.
            Scratchpad.PlainPractice = screen == Screen.Play || screen == Screen.Lobby || screen == Screen.Match;
            _main.EnableInClassList("main--home", screen == Screen.Home);
            foreach (KeyValuePair<Screen, VisualElement> pair in _screens)
                pair.Value.EnableInClassList("is-visible", pair.Key == screen);
            _navHome.EnableInClassList("is-active", screen == Screen.Home);
            _navDaily.EnableInClassList("is-active", screen == Screen.Daily);
            _navPlay.EnableInClassList("is-active", screen == Screen.Play || screen == Screen.Lobby || screen == Screen.Match);
            _navPractice.EnableInClassList("is-active", screen == Screen.Practice);
            _navSettings.EnableInClassList("is-active", _settings.ClassListContains("is-open"));

            ShowProfile();
            ShowHome();
            if (screen == Screen.Daily)
                ShowDaily();
            else if (screen == Screen.Lobby)
                ShowLobby();
            else if (screen == Screen.Match)
                ShowMatch();
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

        // The settings popover closes when the player clicks anywhere else (the board included) or presses Esc. Read
        // from the Input System: clicks on the board never reach the UI.
        private void CloseSettingsOnOutsideClick()
        {
            if (!_settings.ClassListContains("is-open"))
                return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _settings.RemoveFromClassList("is-open");
                return;
            }
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame)
                return;
            Vector2 position = pointer.position.ReadValue();
            float s = PanelScale;
            var onPanel = new Vector2(position.x / s, (UnityEngine.Screen.height - position.y) / s);
            // The Settings button toggles it by itself.
            if (!_settings.worldBound.Contains(onPanel) && !_navSettings.worldBound.Contains(onPanel))
                _settings.RemoveFromClassList("is-open");
        }

        // ---- Lobby (between matches, including the results) ----

        private void ShowLobby()
        {
            MatchState match = _game.Match;
            MatchSettings settings = match.Settings;
            bool finished = match.Phase == MatchPhase.Finished;
            _lobbyTitle.text = !finished ? "Lobby" : match.MatchWinner == match.MyName ? "You win the match!" : $"{match.MatchWinner} wins the match!";
            _lobbySub.text = match.IAmHost
                ? (finished ? "Change the settings or play again." : "You're the host: pick the settings, then start when everyone's in.")
                : (finished ? $"Waiting for {match.Host} to start a new match." : $"Waiting for {match.Host} to start the match.");
            _lobbyCode.text = match.LobbyCode;
            _lobbyCopied.style.display = Time.time < _copiedUntil ? DisplayStyle.Flex : DisplayStyle.None;
            _lobbyPlayersSub.text = settings != null ? $"first to {settings.PointsToWin}" : "";
            ShowPlayers(_lobbyPlayers, ref _shownLobbyPlayers, duringMatch: false);

            _lobbySettings.style.display = match.IAmHost ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (Action refresh in _settingRefreshers)
                refresh();
            _lobbySummary.style.display = !match.IAmHost && settings != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!match.IAmHost && settings != null)
                _lobbySummary.text = SettingsSummary(settings);
            _lobbyStart.style.display = match.IAmHost ? DisplayStyle.Flex : DisplayStyle.None;
            _lobbyStart.text = finished ? "Play again" : "Start match";
            ShowFeedback(_lobbyFeedback, match.Feedback);
            ShowHistory(finished);
        }

        private static string SettingsSummary(MatchSettings s) =>
            $"First to {s.PointsToWin}. {s.FirstTimerSeconds} s timer after the first solution (+{s.ExtensionSeconds} s for a better one). "
            + (s.RoundLimitSeconds > 0 ? $"{s.RoundLimitSeconds / 60} min to find a first solution. " : "")
            + (s.MinMoves == s.MaxMoves ? $"{s.MinMoves}-move boards." : $"Boards of {s.MinMoves} to {s.MaxMoves} moves.")
            + (s.ShowBestSolution ? " The best solution is shown." : "");

        // The finished match's boards: step through them and replay anyone's best, or the shortest, on its board.
        private void ShowHistory(bool finished)
        {
            IReadOnlyList<HistoryRound> history = _game.Match.History;
            bool show = finished && history.Count > 0;
            _lobbyHistory.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                _historyIndex = -1;
                return;
            }
            if (_historyIndex < 0 || _historyIndex >= history.Count)
                _historyIndex = history.Count - 1; // start on the last board
            HistoryRound round = history[_historyIndex];
            _historyBoard.text = $"Board {round.MatchRound} of {history.Count}";
            _historyPrev.SetEnabled(_historyIndex > 0);
            _historyNext.SetEnabled(_historyIndex < history.Count - 1);
            _historyWinner.text = round.WinnerName == null ? "Nobody solved it"
                : round.WinnerName == _game.Match.MyName ? "You won this board" : $"{round.WinnerName} won this board";

            string signature = _historyIndex + "/" + history.Count;
            if (signature == _shownHistory)
                return;
            _shownHistory = signature;
            _historySolutions.Clear();
            foreach (HistorySolution solution in round.Solutions)
            {
                string who = solution.Name == _game.Match.MyName ? "You" : solution.Name;
                string won = solution.Name == round.WinnerName ? "  (won)" : "";
                AddSolutionButton(round, $"{who}  ·  {solution.Moves.Count} moves{won}", new ReplaySegment(who, solution.Moves, 0.6f));
            }
            if (round.Shortest != null)
                AddSolutionButton(round, $"Shortest possible  ·  {round.Shortest.Count} moves",
                    new ReplaySegment("Shortest possible", round.Shortest, 0.6f, isShortest: true));
        }

        private void AddSolutionButton(HistoryRound round, string label, ReplaySegment segment)
        {
            var button = new Button { text = label };
            button.AddToClassList("btn");
            button.AddToClassList("solution-btn");
            if (segment.IsShortest)
                button.AddToClassList("solution-btn--shortest");
            button.clicked += () =>
            {
                if (Session.Round != round.Board)
                    Scratchpad.ShowHistoryBoard(round.Board);
                Scratchpad.ShowSolution(segment);
                button.Blur();
            };
            _historySolutions.Add(button);
        }

        private void SelectHistory(int index)
        {
            IReadOnlyList<HistoryRound> history = _game.Match.History;
            if (index < 0 || index >= history.Count)
                return;
            _historyIndex = index;
            Scratchpad.ShowHistoryBoard(history[index].Board);
        }

        // ---- Match ----

        private void ShowMatch()
        {
            MatchState match = _game.Match;
            float now = Time.time;
            MatchStage stage = match.Stage;
            int next = match.MatchRound + 1;
            _matchTitle.text = stage == MatchStage.Countdown || stage == MatchStage.Dealing ? $"Round {next}" : $"Round {match.MatchRound}";
            _matchSub.text = match.Settings != null ? $"Lobby {match.LobbyCode}  ·  first to {match.Settings.PointsToWin}" : $"Lobby {match.LobbyCode}";

            string caption, value, sub;
            bool bumped = false;
            switch (stage)
            {
                case MatchStage.Dealing:
                    caption = "GET READY"; value = ""; sub = "Dealing the first board...";
                    break;
                case MatchStage.Countdown:
                    float left = match.CountdownLeft(now);
                    caption = "NEXT BOARD"; value = left > 0f ? Mathf.CeilToInt(left).ToString() : ""; sub = left > 0f ? $"Round {next} starts soon." : "Dealing...";
                    break;
                case MatchStage.RoundOver:
                    caption = "ROUND OVER"; value = ""; sub = ReplayText();
                    break;
                default:
                    if (match.HasTimeLimit)
                    {
                        caption = "TIME LIMIT"; value = Clock(match.TimeLimitLeft(now)); sub = "Find a first solution in time. The timer starts on the first one.";
                    }
                    else if (!match.TimerRunning)
                    {
                        caption = "NO SOLUTION YET"; value = ""; sub = "The timer starts on the first solution.";
                    }
                    else
                    {
                        caption = "TIME LEFT"; value = Clock(match.TimeLeft(now));
                        bumped = match.TimeJustAdded(now);
                        sub = bumped ? "A better solution: more time!" : "Shorter solutions add time.";
                    }
                    break;
            }
            _timerCaption.text = caption;
            _timerValue.text = value;
            _timerValue.EnableInClassList("is-bumped", bumped);
            _timerSub.text = sub;

            bool solving = stage == MatchStage.Solving;
            _matchBest.text = match.BestPlayer == null || !solving ? ""
                : match.BestPlayer == match.MyName ? $"Best so far: you, {match.BestMoves} moves."
                : $"Best so far: {match.BestPlayer}, {match.BestMoves} moves. Beat it!";
            _matchBest.style.display = _matchBest.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _matchWatch.style.display = match.CanWatchBest ? DisplayStyle.Flex : DisplayStyle.None;
            if (match.CanWatchBest)
            {
                _matchWatch.text = $"Watch {match.BestPlayer}'s {match.BestMoves}";
                _matchWatch.SetEnabled(!Session.IsReplaying);
            }

            ShowPlayers(_matchPlayers, ref _shownMatchPlayers, duringMatch: true);
            _matchDone.style.display = match.DoneAction != DoneAction.None ? DisplayStyle.Flex : DisplayStyle.None;
            _matchDone.text = DoneLabel(match);
            ShowFeedback(_matchFeedback, match.Feedback);
        }

        private string ReplayText()
        {
            ReplaySegment replay = Session.CurrentReplay;
            if (replay == null)
                return "The next board is on its way.";
            int step = Session.ReplayStep;
            return step == 0 ? $"Replaying {replay.Label}..." : $"Replaying {replay.Label}: move {step} of {replay.Moves.Count}";
        }

        private static string DoneLabel(MatchState match)
        {
            if (match.IAmDone)
                return $"Waiting for others ({match.DoneCount}/{match.VoterCount})  ·  undo";
            switch (match.DoneAction)
            {
                case DoneAction.SkipReplay: return "Skip replay";
                case DoneAction.ImDone: return "I'm done";
                default: return "Skip this board";
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        private static void ShowFeedback(Label label, string feedback)
        {
            label.text = feedback ?? "";
            label.style.display = string.IsNullOrEmpty(feedback) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // The players, in standings order, rebuilt only when something about them changes.
        private void ShowPlayers(VisualElement list, ref string shown, bool duringMatch)
        {
            MatchState match = _game.Match;
            var signature = new System.Text.StringBuilder(duringMatch ? "m" : "l");
            foreach (PlayerInfo p in match.Standings)
                signature.Append('|').Append(p.Name).Append('/').Append(p.Score).Append('/').Append(p.Done).Append('/')
                    .Append(p.Leading).Append('/').Append(p.Connected);
            signature.Append('|').Append(match.Host).Append('|').Append(match.BestMoves);
            if (signature.ToString() == shown)
                return;
            shown = signature.ToString();
            list.Clear();
            var order = new List<string>(); // avatar colours by name, so they stay put as the standings change
            foreach (PlayerInfo p in match.Standings)
                order.Add(p.Name);
            order.Sort(StringComparer.Ordinal);
            foreach (PlayerInfo p in match.Standings)
                list.Add(PlayerRow(p, AvatarColors[order.IndexOf(p.Name) % AvatarColors.Length], duringMatch));
        }

        private VisualElement PlayerRow(PlayerInfo p, RobotColor color, bool duringMatch)
        {
            MatchState match = _game.Match;
            var row = new VisualElement();
            row.AddToClassList("player-row");
            row.EnableInClassList("is-me", p.Name == match.MyName);
            var avatar = new Label(p.Name.Length > 0 ? p.Name.Substring(0, 1).ToUpperInvariant() : "?");
            avatar.AddToClassList("player-avatar");
            avatar.style.backgroundColor = Theme.RobotColor(color);
            if (color == RobotColor.Yellow)
                avatar.style.color = (Color)new Color32(0x1b, 0x1d, 0x2a, 0xff);
            row.Add(avatar);
            var name = new Label(p.Name == match.MyName ? $"{p.Name} (you)" : p.Name);
            name.AddToClassList("player-name");
            row.Add(name);
            if (p.Name == match.Host)
                row.Add(Tag("host", "tag--host"));
            if (!p.Connected)
                row.Add(Tag("reconnecting...", "tag--away"));
            else if (duringMatch && p.Leading)
                row.Add(Tag(match.BestMoves > 0 ? $"leading · {match.BestMoves}" : "leading", "tag--lead"));
            else if (duringMatch && p.Done)
                row.Add(Tag("done", "tag--done"));
            var score = new Label(p.Score.ToString());
            score.AddToClassList("player-score");
            row.Add(score);
            return row;
        }

        private static Label Tag(string text, string kind)
        {
            var tag = new Label(text);
            tag.AddToClassList("tag");
            tag.AddToClassList(kind);
            return tag;
        }

        // The daily puzzle's side: how the player stands (clock or best, streak), their share squares, the leaderboard.
        private void ShowDaily()
        {
            DailyState daily = _daily.State;
            float now = Time.time;
            _dailyHeader.text = daily.Number > 0 ? $"DAILY PUZZLE #{daily.Number}  ·  {daily.Weekday.ToUpperInvariant()}" : "DAILY PUZZLE";
            bool error = daily.Stage == DailyStage.Error, loading = daily.Stage == DailyStage.Loading;
            bool solved = daily.Stage == DailyStage.Playing && daily.HasSolved;

            _dailyHeadline.text = loading ? "Loading today's puzzle..." : error ? "Couldn't load the puzzle"
                : daily.IsPerfect ? "Perfect solve!" : solved ? $"Solved in {daily.BestMoves} moves" : "Today's puzzle";
            _dailyStats.style.display = loading || error ? DisplayStyle.None : DisplayStyle.Flex;
            _dailyMoves.text = solved ? $"{daily.BestMoves}/{daily.ShortestLength}" : $"{daily.ShortestLength}";
            _dailyMovesLabel.text = solved ? "moves" : "best possible";
            _dailyTime.text = DailyState.Clock(solved ? daily.BestSeconds : daily.Elapsed(now));
            _dailyStreak.text = daily.Streak.ToString();
            _dailyDetail.text = error ? daily.Error
                : loading ? ""
                : daily.IsPerfect ? "You found the best possible solution. See you tomorrow!"
                : solved ? "Keep going: a shorter solution beats your best, until midnight."
                : "Solve it in as few moves as you can. Your time counts from when you first opened it, and you can keep improving until midnight (Eastern).";
            _dailyCopy.style.display = solved ? DisplayStyle.Flex : DisplayStyle.None;
            _dailyRetry.style.display = error ? DisplayStyle.Flex : DisplayStyle.None;
            string notice = _daily.Notice ?? (daily.Submitting ? "Sending..." : null);
            _dailyNotice.text = notice ?? "";
            _dailyNotice.style.display = notice != null ? DisplayStyle.Flex : DisplayStyle.None;

            // The squares and rows only change when the result does: rebuild them then, not every frame.
            string squares = solved ? string.Join(",", daily.BestSolution) : "";
            _dailySquares.style.display = solved ? DisplayStyle.Flex : DisplayStyle.None;
            if (squares != _shownSquares)
            {
                _shownSquares = squares;
                _dailySquares.Clear();
                foreach (Move move in solved ? daily.BestSolution : new Move[0])
                {
                    var square = new VisualElement();
                    square.AddToClassList("square");
                    square.style.backgroundColor = Theme.RobotColor(move.Robot);
                    _dailySquares.Add(square);
                }
            }

            DailyLeaderboard board = daily.Leaderboard;
            bool hasBoard = solved && board != null && board.Top != null && board.Top.Length > 0;
            _dailyBoard.style.display = hasBoard ? DisplayStyle.Flex : DisplayStyle.None;
            _dailyRank.text = daily.Rank > 0 ? $"#{daily.Rank} of {daily.Players}" : "";
            string rows = hasBoard ? string.Join("|", Array.ConvertAll(board.Top, e => $"{e.Name}/{e.Moves}/{e.Seconds:0}/{e.IsMe}")) + board.MyRank : "";
            if (rows != _shownRows)
            {
                _shownRows = rows;
                _dailyRows.Clear();
                if (hasBoard)
                {
                    for (int i = 0; i < board.Top.Length && i < 5; i++)
                        _dailyRows.Add(LeaderboardRow(i + 1, board.Top[i].IsMe ? "You" : board.Top[i].Name, board.Top[i].Moves, board.Top[i].Seconds, board.Top[i].IsMe));
                    if (board.MyRank > 5)
                        _dailyRows.Add(LeaderboardRow(board.MyRank, "You", daily.BestMoves, daily.BestSeconds, true));
                }
            }
            _dailyNext.text = daily.Stage == DailyStage.Playing ? $"Next puzzle in {DailyState.Clock(daily.UntilNext(now))}" : "";
        }

        private static VisualElement LeaderboardRow(int place, string name, int moves, double seconds, bool me)
        {
            var row = new VisualElement();
            row.AddToClassList("lb-row");
            row.EnableInClassList("is-me", me);
            var rank = new Label(place.ToString());
            rank.AddToClassList("lb-rank");
            if (place <= 3)
                rank.AddToClassList($"lb-rank--{place}");
            var who = new Label(name);
            who.AddToClassList("lb-name");
            var count = new Label(moves.ToString());
            count.AddToClassList("lb-moves");
            var time = new Label(DailyState.Clock(seconds));
            time.AddToClassList("lb-time");
            row.Add(rank);
            row.Add(who);
            row.Add(count);
            row.Add(time);
            return row;
        }

        private void ShowProfile()
        {
            string name = _game.PlayerName;
            _profileName.text = name.Length > 0 ? name : "Guest";
            // A name set elsewhere (e.g. ?name= in the link) shows in the box too, unless the player is typing in it.
            if (_nameField.value != name && !IsTyping())
                _nameField.SetValueWithoutNotify(name);
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
            _playStatus.text = _game.Status.StartsWith("Join failed") || _game.Status.StartsWith("Enter") ? _game.Status : "";
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

            // A controllable replay puts its buttons in this row in place of Undo and Shortest (both greyed during it,
            // and the same as Reset / Again once it ends); the keys still work.
            bool practice = Session.IsPractice;
            bool replay = Session.CanControlReplay;
            _undo.style.display = !replay ? DisplayStyle.Flex : DisplayStyle.None;
            _newBoard.style.display = practice ? DisplayStyle.Flex : DisplayStyle.None;
            _shortest.style.display = practice && !replay ? DisplayStyle.Flex : DisplayStyle.None;
            _undo.SetEnabled(Scratchpad.CanPerform(ControlAction.Undo));
            _reset.SetEnabled(Scratchpad.CanPerform(ControlAction.Reset));
            _newBoard.SetEnabled(Scratchpad.CanPerform(ControlAction.NewBoard));
            _shortest.SetEnabled(Scratchpad.CanPerform(ControlAction.Shortest));

            _replayBar.style.display = replay ? DisplayStyle.Flex : DisplayStyle.None;
            if (replay)
                _replayPause.text = !Session.ReplayPaused && !Session.ReplayFinished ? "Pause" : Session.ReplayFinished ? "Again" : "Play";

            ShowTip();
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

        private const string TipSeenKey = "Ricochet.SeenMoveTip";

        // How to move, over the bottom of the board, until the player's first move ever (then never again here).
        private void ShowTip()
        {
            bool seen = PlayerPrefs.GetInt(TipSeenKey, 0) == 1;
            if (!seen && Session.Moves.Count > 0 && !Session.IsReplaying)
            {
                PlayerPrefs.SetInt(TipSeenKey, 1);
                seen = true;
            }
            bool show = !seen && Session.AcceptsInput && !Session.IsCountingDown && Scratchpad.SearchStatus == null;
            _boardTip.EnableInClassList("is-visible", show);
            if (!show)
                return;
            Rect board = BoardOnPanel();
            _boardTip.style.left = board.x + board.width * 0.08f;
            _boardTip.style.width = board.width * 0.84f;
            _boardTip.style.top = board.yMax - 64f;
        }

        private string BannerText(BoardSession session)
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
                    if (!session.IsSolved)
                        return $"{board}  ·  {moves} {(moves == 1 ? "move" : "moves")}";
                    // A practice board of a known length says how the solve compares.
                    int best = Scratchpad.PracticeShortest;
                    string verdict = best <= 0 ? "!" : moves <= best ? ": optimal!" : $"  ·  best is {best}";
                    return $"{board}  ·  solved in {moves}{verdict}";
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
