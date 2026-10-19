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
        private enum Screen { Home, Play, Practice, Lobby, Daily }

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
        private VisualElement _soonScreen, _dailySquares, _dailyRows, _dailyStats, _dailyBoard;
        private Label _dailyHeader, _dailyHeadline, _dailyMoves, _dailyMovesLabel, _dailyTime, _dailyStreak, _dailyDetail, _dailyNotice, _dailyRank, _dailyNext;
        private Button _dailyCopy, _dailyRetry;
        private string _shownSquares, _shownRows;
        private Label _soonTitle, _soonText, _homeTitle, _homeDailyTitle, _homeDailyStreak, _playStatus, _profileAvatar, _profileName;
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
            _soonScreen = _root.Q("screen-soon");
            _soonTitle = _root.Q<Label>("soon-title");
            _soonText = _root.Q<Label>("soon-text");
            _soonAction = Click("soon-action", () => _soonClicked?.Invoke());

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

            CloseSettingsOnOutsideClick();
            Screen screen = Current;
            _main.EnableInClassList("main--home", screen == Screen.Home);
            foreach (KeyValuePair<Screen, VisualElement> pair in _screens)
                pair.Value.EnableInClassList("is-visible", pair.Key == screen);
            ShowSoon(screen);
            _navHome.EnableInClassList("is-active", screen == Screen.Home);
            _navDaily.EnableInClassList("is-active", screen == Screen.Daily);
            _navPlay.EnableInClassList("is-active", screen == Screen.Play || screen == Screen.Lobby);
            _navPractice.EnableInClassList("is-active", screen == Screen.Practice);
            _navSettings.EnableInClassList("is-active", _settings.ClassListContains("is-open"));

            ShowProfile();
            ShowHome();
            if (screen == Screen.Daily)
                ShowDaily();
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

        // Lobby and daily screens arrive in the next steps; until then a placeholder with a way back.
        private void ShowSoon(Screen screen)
        {
            bool soon = screen == Screen.Lobby;
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
            _dailyMovesLabel.text = solved ? "moves" : "moves possible";
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
