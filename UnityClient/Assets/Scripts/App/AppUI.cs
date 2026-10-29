using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public partial class AppUI : MonoBehaviour
    {
        private enum Screen { Home, Play, Practice, Lobby, Match, Daily, Archive }

        // Avatar colours, one per player in join order.
        private static readonly RobotColor[] AvatarColors = { RobotColor.Blue, RobotColor.Red, RobotColor.Green, RobotColor.Yellow };

        // The palette's ground, behind the board (the camera's clear colour).

        private GameController _game;
        private DailyController _daily;
        private VisualElement _root;
        private VisualElement _leavePopover;
        private Label _leaveTitle, _leaveBody;
        private Button _leaveConfirm;
        // Where a nav tab clicked in a lobby was going: asked first (the leave popover), done once the lobby is left.
        private Action _askedToGo, _afterLeave;
        private Label _paletteLabel;
        private readonly List<(Button, Palette)> _swatches = new List<(Button, Palette)>();
        private Label _boardStyleLabel;
        private readonly List<(Button, BoardStyle)> _boardSwatches = new List<(Button, BoardStyle)>();
        private Screen _chosen = Screen.Home;

        private VisualElement _main, _boardArea, _boardOverlay, _goalIcon, _replayBar, _settings, _profilePopover, _profileButton;
        private readonly TutorialState _tutorial = new TutorialState();
        // A device that has never played (checked before the first visit makes a guest id): gets the tutorial.
        private static bool _newPlayer;
        private bool _tutorialChecked;
        private TutorialStep? _tutorialShown;
        private float _tutorialStepAt;
        private VisualElement _tutorialCard;
        private readonly List<VisualElement> _tutorialDots = new List<VisualElement>();
        private Label _tutorialBadge, _tutorialTitle, _tutorialBody;
        private Button _tutorialSkip, _tutorialDone;
        private const string TutorialDoneKey = "Ricochet.TutorialDone";

        private Label _goalLabel, _goalMeasure, _banner, _boardTip, _overlayCaption, _overlayNumber, _overlayHint;
        private Button _navHome, _navDaily, _navPlay, _navPractice, _navSettings;
        private Button _undo, _reset, _newBoard, _shortest, _replayPause, _replayBack, _replayForward;
        private readonly Dictionary<Screen, VisualElement> _screens = new Dictionary<Screen, VisualElement>();
        private VisualElement _dailySquares, _dailyRows, _dailyStats, _dailyBoard;
        private Label _dailyHeader, _dailyHeadline, _dailyMoves, _dailyMovesLabel, _dailyTime, _dailyStreak, _dailyDetail, _dailyNotice, _dailyRank, _dailyNext;
        private Button _dailyCopy, _dailyRetry, _dailyTabToday, _dailyTabStreaks;
        private string _shownSquares, _shownRows;
        private VisualElement _dailyPast, _dailyStrip, _archiveGrid, _archiveDay, _archiveStats;
        private Label _dailyBoardEmpty, _archiveSub, _archiveMonth, _archiveDayTitle, _archiveDayHeadline, _archiveDayDetail;
        private Label _archiveDayMoves, _archiveLaterMoves, _archiveShortest, _archiveNotice;
        private Button _archivePrev, _archiveNext, _archiveWatch, _archiveSolution;
        private string _shownStrip, _shownCalendar, _shownPastRows;
        private VisualElement _archiveBoard, _archiveRows;
        private Label _archiveBoardEmpty;
        private bool _streaksTab; // the daily leaderboard shows streaks instead of today's results
        private Screen? _scrolledFor; // the screen the side panel's scroll belongs to
        private Label _homeTitle, _homeDailyTitle, _homeDailyStreak, _playStatus, _profileAvatar, _profileName;
        private VisualElement _lobbyPlayers, _lobbySettings, _lobbySettingsView, _lobbyHistory, _historyList, _matchPlayers;
        private VisualElement _lobbyStandings, _lobbyStandingsList, _lobbyCodeCard, _lobbyPlayersCard, _roundCard, _roundProgress, _roundProgressFill, _bestBox;
        private Label _lobbyTitle, _lobbySub, _lobbyCode, _lobbyCopied, _lobbyPlayersTitle, _lobbyAlone, _lobbyFeedback, _lobbyTip;
        private Label _matchTitle, _matchSub, _timerCaption, _timerValue, _timerUnit, _roundDetail, _timerSub, _matchBest, _matchBestSub;
        private Label _matchFeedback, _matchTip;
        private Button _lobbyStart, _lobbyLeave, _matchWatch, _matchDone;
        private readonly List<Action> _settingRefreshers = new List<Action>();
        private string _shownLobbyPlayers, _shownMatchPlayers, _shownHistory, _shownStandings;
        private int _historyIndex = -1; // the history board that's open (-1: none)
        private float _copiedUntil;
        private string _copiedText = "";
        private bool? _resultsOrder; // the lobby's cards are in the results' order (true) or the waiting room's (false)
        private ScrollView _side;
        private float _scrollTarget, _scrollSetTo;
        private bool _scrollGliding;
        private const float WheelStep = 60f, GlideRate = 18f;
        private TextField _nameField, _codeField, _profileNameField;
        private readonly List<(Button Switch, Func<bool> IsOn)> _switches = new List<(Button, Func<bool>)>();
        private readonly List<(Label Value, Button Minus, Button Plus)> _steppers = new List<(Label, Button, Button)>();

        private ScratchpadController Scratchpad => _game.Scratchpad;
        private BoardSession Session => _game.Scratchpad.Session;
        private GameTheme Theme => _game.Scratchpad.View.Theme;

        // ---- Start-up: the scene holds the network and the board; the UI and its controllers are made here ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var network = FindAnyObjectByType<NetworkManager>();
            var scratchpad = FindAnyObjectByType<ScratchpadController>();
            if (network == null || scratchpad == null)
                return;

            _newPlayer = !GuestIdentity.Exists && !PlayerPrefs.HasKey(TipSeenKey) && !PlayerPrefs.HasKey(TutorialDoneKey);
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
            _panelSettings = settings;

            var holder = new GameObject("UI Document");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var document = holder.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("Ricochet/App");
            holder.SetActive(true);
            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;

            // Tab picks robots and the arrows move them, so UI Toolkit's own keyboard navigation (focus moving from button to
            // button, then into a text field, where the board stops taking keys) is off; Tab only hops between the name and
            // code fields while typing in one.
            _root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);

            Bind();
            _root.Query<Button>(className: "btn").ForEach(AddShadow);
            InitLayout();
            ApplyPalette();
            Palette.Changed += ApplyPalette;
            ShowBoardStyle();
            BoardStyle.Changed += () =>
            {
                ShowBoardStyle();
                Scratchpad.View?.Restyle();
            };
            Scratchpad.BoardArea = BoardAreaOnScreen;
            ScratchpadController.TypingInOtherUI = IsTyping;
            ScratchpadController.PointerBlocked = PointerBlocked;
        }

        private void OnNavigationMove(NavigationMoveEvent e)
        {
            _root.focusController.IgnoreEvent(e);
            e.StopPropagation();
            if (e.direction != NavigationMoveEvent.Direction.Next && e.direction != NavigationMoveEvent.Direction.Previous)
                return;
            if (HasFocus(_nameField))
                _codeField.Focus();
            else if (HasFocus(_codeField))
                _nameField.Focus();
        }

        private void Bind()
        {
            _main = _root.Q("main");
            _boardArea = _root.Q("board-area");
            _boardOverlay = _root.Q("board-overlay");
            _goalIcon = _root.Q("goal-icon");
            _goalLabel = _root.Q<Label>("goal-label");
            // A hidden copy of the goal sized to its text, to tell whether the full sentence fits (GoalFits).
            _goalMeasure = new Label { pickingMode = PickingMode.Ignore };
            _goalMeasure.AddToClassList("goal-label");
            _goalMeasure.style.position = Position.Absolute;
            _goalMeasure.style.visibility = Visibility.Hidden;
            _goalLabel.parent.Add(_goalMeasure);
            _banner = _root.Q<Label>("banner");
            _boardTip = _root.Q<Label>("board-tip");
            _tutorialCard = _root.Q("tutorial-card");
            _tutorialBadge = _root.Q<Label>("tutorial-badge");
            _tutorialTitle = _root.Q<Label>("tutorial-title");
            _tutorialBody = _root.Q<Label>("tutorial-body");
            VisualElement dots = _root.Q("tutorial-dots");
            for (int i = 0; i < 4; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("tutorial-dot");
                dots.Add(dot);
                _tutorialDots.Add(dot);
            }
            _tutorialSkip = Click("tutorial-skip", () => EndTutorial(finished: false));
            _tutorialDone = Click("tutorial-done", () => EndTutorial(finished: true));
            Click("practice-tutorial", StartTutorial);
            _overlayCaption = _root.Q<Label>("overlay-caption");
            _overlayNumber = _root.Q<Label>("overlay-number");
            _overlayHint = _root.Q<Label>("overlay-hint");
            _replayBar = _root.Q("replay-bar");
            _settings = _root.Q("settings");
            _profilePopover = _root.Q("profile-popover");
            _leavePopover = _root.Q("leave-popover");
            _leaveTitle = _root.Q<Label>("leave-title");
            _leaveBody = _root.Q<Label>("leave-body");
            _leaveConfirm = Click("leave-confirm", ConfirmLeave);
            Click("leave-stay", () => ClosePopover(_leavePopover));
            // The side panel scrolls smoothly: wheel and trackpad events move a target the panel glides to (the
            // ScrollView's own wheel handling jumps a fixed step per event, which stutters on a trackpad).
            _side = _root.Q<ScrollView>("side");
            _side.RegisterCallback<WheelEvent>(OnSideWheel, TrickleDown.TrickleDown);
            Viewport.ListenForWheel();

            _navHome = Click("nav-home", () => Choose(Screen.Home));
            _navDaily = Click("nav-daily", OpenDaily);
            _navPlay = Click("nav-play", () => Choose(Screen.Play));
            _navPractice = Click("nav-practice", () => Choose(Screen.Practice));
            _navSettings = Click("nav-settings", () => TogglePopover(_settings));
            _profileButton = _root.Q("profile-button");
            _profileButton.RegisterCallback<ClickEvent>(_ => TogglePopover(_profilePopover));

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
            _dailyBoardEmpty = _root.Q<Label>("daily-board-empty");
            _dailyTabToday = Click("daily-tab-today", () => _streaksTab = false);
            _dailyTabStreaks = Click("daily-tab-streaks", () =>
            {
                _streaksTab = true;
                _daily.RefreshStreaks();
            });
            _dailyPast = _root.Q("daily-past");
            _dailyStrip = _root.Q("daily-strip");
            Click("daily-see-all", () => _daily.OpenArchive());
            BindArchive();
            BindLobby();

            _homeTitle = _root.Q<Label>("home-title");
            _homeDailyTitle = _root.Q<Label>("home-daily-title");
            _homeDailyStreak = _root.Q<Label>("home-daily-streak");
            _playStatus = _root.Q<Label>("play-status");
            _profileAvatar = _root.Q<Label>("profile-avatar");
            _profileName = _root.Q<Label>("profile-name");
            Click("home-daily-play", OpenDaily);
            Click("home-play", () => Choose(Screen.Play));
            Click("home-practice-open", () => Choose(Screen.Practice));
            _root.Q("daily-icon-star").style.backgroundImage = new StyleBackground(ShapeSprites.TargetSymbol(RobotColor.Yellow));
            Click("play-create", () => _game.CreateLobby(_nameField.value));
            Click("play-join", () => _game.Join(_codeField.value, _nameField.value));

            _nameField = _root.Q<TextField>("play-name");
            _nameField.value = _game.PlayerName;
            _nameField.RegisterValueChangedCallback(e => _game.PlayerName = e.newValue);
            // The same name from the profile popover, on any screen (both boxes stay in step via ShowProfile).
            _profileNameField = _root.Q<TextField>("profile-name-field");
            _profileNameField.value = _game.PlayerName;
            _profileNameField.RegisterValueChangedCallback(e => _game.PlayerName = e.newValue);
            _profileNameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    ClosePopover(_profilePopover);
            });
            _codeField = _root.Q<TextField>("play-code");
            // A click puts the caret where it lands: UI Toolkit selects all the text on focus and on mouse-up by default,
            // which flashed as a highlight.
            foreach (TextField field in new[] { _nameField, _profileNameField, _codeField })
            {
                field.selectAllOnFocus = false;
                field.selectAllOnMouseUp = false;
            }
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
            AddSwitch(AddRow(settingsList), "Dashed selection ring", () => PlayerSettings.DashedSelection,
                () => PlayerSettings.DashedSelection = !PlayerSettings.DashedSelection);

            // The palette picker: a swatch per palette (its ground with its action colour inside).
            _paletteLabel = _root.Q<Label>("palette-label");
            VisualElement paletteList = _root.Q("palette-list");
            foreach (Palette palette in Palette.All)
            {
                var swatch = new Button { tooltip = palette.Name };
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = palette.Ground;
                var dot = new VisualElement();
                dot.AddToClassList("swatch-dot");
                dot.style.backgroundColor = palette.Accent;
                swatch.Add(dot);
                swatch.clicked += () =>
                {
                    Palette.Select(palette);
                    swatch.Blur();
                };
                paletteList.Add(swatch);
                _swatches.Add((swatch, palette));
            }

            // The board picker: a swatch per board style (its tiles with its walls' colour inside; two tones: the second tone,
            // edged in the walls' colour).
            _boardStyleLabel = _root.Q<Label>("board-style-label");
            VisualElement boardList = _root.Q("board-style-list");
            foreach (BoardStyle style in BoardStyle.All)
            {
                var swatch = new Button { tooltip = style.Name };
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = style.TileA;
                var dot = new VisualElement();
                dot.AddToClassList("swatch-dot");
                bool twoTone = style.TileB != style.TileA;
                dot.style.backgroundColor = twoTone ? style.TileB : style.Wall;
                if (twoTone)
                {
                    dot.style.borderTopWidth = dot.style.borderBottomWidth = dot.style.borderLeftWidth = dot.style.borderRightWidth = 2f;
                    dot.style.borderTopColor = dot.style.borderBottomColor = dot.style.borderLeftColor = dot.style.borderRightColor = style.Wall;
                }
                swatch.Add(dot);
                swatch.clicked += () =>
                {
                    BoardStyle.Select(style);
                    swatch.Blur();
                };
                boardList.Add(swatch);
                _boardSwatches.Add((swatch, style));
            }
        }

        private void ShowBoardStyle()
        {
            foreach ((Button swatch, BoardStyle style) in _boardSwatches)
                swatch.EnableInClassList("is-active", style == BoardStyle.Current);
            _boardStyleLabel.text = $"Board: {BoardStyle.Current.Name}";
        }

        // Re-colours everything for the current palette: the UI's variables (a class on #app), the swatches, the board's frame.
        private void ApplyPalette()
        {
            Palette current = Palette.Current;
            foreach (Palette palette in Palette.All)
                _app.EnableInClassList(palette.UssClass, palette == current);
            foreach ((Button swatch, Palette palette) in _swatches)
                swatch.EnableInClassList("is-active", palette == current);
            _paletteLabel.text = $"Palette: {current.Name}";
            current.RememberForLoadingPage();
            _root.Query<Button>(className: "btn").ForEach(b => b.MarkDirtyRepaint());
            Scratchpad.View?.RefreshFrame();
        }

        private void BindLobby()
        {
            _screens[Screen.Lobby] = _root.Q("screen-lobby");
            _screens[Screen.Match] = _root.Q("screen-match");
            _lobbyTitle = _root.Q<Label>("lobby-title");
            _lobbySub = _root.Q<Label>("lobby-sub");
            _lobbyStandings = _root.Q("lobby-standings");
            _lobbyStandingsList = _root.Q("lobby-standings-list");
            _lobbyCodeCard = _root.Q("lobby-code-card");
            _lobbyCode = _root.Q<Label>("lobby-code");
            _lobbyCopied = _root.Q<Label>("lobby-copied");
            _lobbyPlayersCard = _root.Q("lobby-players-card");
            _lobbyPlayersTitle = _root.Q<Label>("lobby-players-title");
            _lobbyPlayers = _root.Q("lobby-players");
            _lobbyAlone = _root.Q<Label>("lobby-alone");
            _lobbySettings = _root.Q("lobby-settings");
            _lobbySettingsView = _root.Q("lobby-settings-view");
            _lobbyFeedback = _root.Q<Label>("lobby-feedback");
            _lobbyTip = _root.Q<Label>("lobby-tip");
            _lobbyHistory = _root.Q("lobby-history");
            _historyList = _root.Q("history-list");
            _matchTitle = _root.Q<Label>("match-title");
            _matchSub = _root.Q<Label>("match-sub");
            _roundCard = _root.Q("round-card");
            _timerCaption = _root.Q<Label>("timer-caption");
            _timerValue = _root.Q<Label>("timer-value");
            _timerUnit = _root.Q<Label>("timer-unit");
            _roundDetail = _root.Q<Label>("round-detail");
            _timerSub = _root.Q<Label>("timer-sub");
            _roundProgress = _root.Q("round-progress");
            _roundProgressFill = _root.Q("round-progress-fill");
            _bestBox = _root.Q("match-best-box");
            _matchBest = _root.Q<Label>("match-best");
            _matchBestSub = _root.Q<Label>("match-best-sub");
            _matchFeedback = _root.Q<Label>("match-feedback");
            _matchTip = _root.Q<Label>("match-tip");
            _matchPlayers = _root.Q("match-players");

            Click("lobby-copy", () => Copy($"https://ricochet.thomasliu.dev/?lobby={_game.Match.LobbyCode}", "Copied! Send it to your friends."));
            Click("lobby-copy-code", () => Copy(_game.Match.LobbyCode, "Code copied."));
            _lobbyStart = Click("lobby-start", _game.StartMatch);
            _lobbyLeave = Click("lobby-leave", _game.Leave);
            Click("match-leave", _game.Leave);
            _matchWatch = Click("match-watch", _game.WatchBest);
            _matchDone = Click("match-done", _game.ToggleDone);

            // The host's settings, in groups: steppers send the change at once (the server clamps it and echoes it back).
            VisualElement list = _root.Q("lobby-settings-list");
            AddSettingsGroup(list, "Match", first: true);
            AddSettingStepper(list, "Points to win", s => s.PointsToWin, (s, v) => s.PointsToWin = v, 1,
                MatchSettings.MinPointsToWin, MatchSettings.MaxPointsToWin, v => v.ToString());
            AddSettingsGroup(list, "Timers");
            AddSettingStepper(list, "Round timer", s => s.RoundLimitSeconds, (s, v) => s.RoundLimitSeconds = v, 60,
                0, MatchSettings.MaxRoundLimitSeconds, LimitText);
            AddSettingHint(list, "Time to find a first solution.");
            // One countdown: it starts on the first solution and every shorter one starts it again (the extension is kept
            // equal to it).
            AddSettingStepper(list, "Countdown after a solution", s => s.FirstTimerSeconds,
                (s, v) => { s.FirstTimerSeconds = v; s.ExtensionSeconds = v; }, 10,
                MatchSettings.MinTimerSeconds, MatchSettings.MaxTimerSeconds, v => $"{v} s");
            AddSettingHint(list, "Each shorter solution starts it again.");
            AddSettingsGroup(list, "Boards");
            AddSettingStepper(list, "Fewest moves", s => s.MinMoves, (s, v) => s.MinMoves = v, 1,
                MatchSettings.MinMinMoves, MatchSettings.MaxMinMoves, v => v.ToString());
            AddSettingStepper(list, "Most moves", s => s.MaxMoves, (s, v) => s.MaxMoves = v, 1,
                MatchSettings.MinMinMoves, MatchSettings.MaxMinMoves, v => v.ToString());
            AddSettingsGroup(list, "During a round");
            AddSwitch(AddRow(list), "Watch the best so far", () => _game.Match.Settings != null && _game.Match.Settings.ShowBestSolution,
                () => EditSettings(s => s.ShowBestSolution = !s.ShowBestSolution));
            AddSettingHint(list, "Players who are behind can replay the current best solution.");

            // Everyone else sees the same settings, read-only.
            VisualElement view = _root.Q("lobby-settings-view-list");
            AddSettingValue(view, "Points to win", s => s.PointsToWin.ToString());
            AddSettingValue(view, "Round timer", s => LimitText(s.RoundLimitSeconds));
            AddSettingValue(view, "Countdown after a solution", s => $"{s.FirstTimerSeconds} s");
            AddSettingValue(view, "Board moves", s => s.MinMoves == s.MaxMoves ? s.MinMoves.ToString() : $"{s.MinMoves} to {s.MaxMoves}");
            AddSettingValue(view, "Watch the best so far", s => s.ShowBestSolution ? "On" : "Off");
        }

        private static string LimitText(int seconds) => seconds <= 0 ? "No limit" : $"{seconds / 60} min";

        private void Copy(string text, string notice)
        {
            Clipboard.Copy(text);
            _copiedText = notice;
            _copiedUntil = Time.time + 2.5f;
        }

        // A muted line under the setting above it, saying what it does.
        private static void AddSettingHint(VisualElement parent, string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("setting-hint");
            parent.Add(hint);
        }

        private static void AddSettingsGroup(VisualElement parent, string title, bool first = false)
        {
            if (!first)
            {
                var divider = new VisualElement();
                divider.AddToClassList("settings-divider");
                parent.Add(divider);
            }
            var label = new Label(title);
            label.AddToClassList("settings-group");
            parent.Add(label);
        }

        private static Label SettingLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("setting-label");
            label.style.flexShrink = 1;
            label.style.flexGrow = 1;
            label.style.flexBasis = 0;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void AddSettingStepper(VisualElement parent, string label, Func<MatchSettings, int> get, Action<MatchSettings, int> set,
            int step, int min, int max, Func<int, string> format)
        {
            VisualElement row = AddRow(parent);
            row.Add(SettingLabel(label));
            row.Add(MiniStepper(get, set, step, min, max, format));
        }

        // A - value + stepper for one setting.
        private VisualElement MiniStepper(Func<MatchSettings, int> get, Action<MatchSettings, int> set, int step, int min, int max,
            Func<int, string> format)
        {
            var stepper = new VisualElement();
            stepper.AddToClassList("mini-stepper");
            var minus = new Button { text = "−" };
            var plus = new Button { text = "+" };
            minus.AddToClassList("btn");
            plus.AddToClassList("btn");
            AddShadow(minus);
            AddShadow(plus);
            var value = new Label();
            value.AddToClassList("mini-stepper-value");
            minus.clicked += () => { EditSettings(s => set(s, Step(get(s), -step, min, max))); minus.Blur(); };
            plus.clicked += () => { EditSettings(s => set(s, Step(get(s), step, min, max))); plus.Blur(); };
            stepper.Add(minus);
            stepper.Add(value);
            stepper.Add(plus);
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
            return stepper;
        }

        private void AddSettingValue(VisualElement parent, string label, Func<MatchSettings, string> value)
        {
            VisualElement row = AddRow(parent);
            row.AddToClassList("setting-row--view");
            row.Add(SettingLabel(label));
            var text = new Label();
            text.AddToClassList("setting-value");
            row.Add(text);
            _settingRefreshers.Add(() =>
            {
                MatchSettings settings = _game.Match.Settings;
                if (settings != null)
                    text.text = value(settings);
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

        // A press-down shadow under the button in palettes that have one (Toybox night): the button's rounded bottom
        // edge repeated a few pixels lower, drawn as the band between the two (USS has no box-shadow; a child element
        // would change how the button is sized).
        private const float PressShadowDepth = 4f;

        private static void AddShadow(Button button) => button.generateVisualContent += DrawPressShadow;

        private static void DrawPressShadow(MeshGenerationContext context)
        {
            if (!Palette.Current.PressShadow)
                return;
            VisualElement element = context.visualElement;
            float w = element.layout.width, h = element.layout.height, d = PressShadowDepth;
            float r = Mathf.Min(element.resolvedStyle.borderBottomLeftRadius, w / 2f, h / 2f);
            if (w <= 0f || h <= 0f)
                return;
            Painter2D painter = context.painter2D;
            painter.fillColor = Color.black;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, h - r));
            painter.Arc(new Vector2(r, h - r), r, Angle.Degrees(180f), Angle.Degrees(90f), ArcDirection.CounterClockwise);
            painter.LineTo(new Vector2(w - r, h));
            painter.Arc(new Vector2(w - r, h - r), r, Angle.Degrees(90f), Angle.Degrees(0f), ArcDirection.CounterClockwise);
            painter.LineTo(new Vector2(w, h - r + d));
            painter.Arc(new Vector2(w - r, h - r + d), r, Angle.Degrees(0f), Angle.Degrees(90f), ArcDirection.Clockwise);
            painter.LineTo(new Vector2(r, h + d));
            painter.Arc(new Vector2(r, h - r + d), r, Angle.Degrees(90f), Angle.Degrees(180f), ArcDirection.Clockwise);
            painter.ClosePath();
            painter.Fill();
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
                AddShadow(b);
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
            // Browsers: the distance the page itself would scroll (each event is one step otherwise, so a trackpad swipe's
            // dozens of events went straight to the end).
            float delta = Viewport.TakeWheel(out float css) ? css * Viewport.PixelRatio / PanelScale : e.delta.y * WheelStep;
            _scrollTarget = Mathf.Clamp(_scrollTarget + delta, 0f, max);
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
            if (_game.Match.InLobby)
            {
                // Play with friends is the lobby itself; the other tabs mean leaving it, so ask first.
                if (screen != Screen.Play)
                    AskToLeave(() => Choose(screen));
                return;
            }
            _chosen = screen;
            if (_daily.State.IsActive || _daily.Archive.IsOpen)
                _daily.Close(backToPractice: true);
        }

        private void OpenDaily()
        {
            if (_game.Match.InLobby)
            {
                AskToLeave(OpenDaily);
                return;
            }
            _daily.Open();
        }

        private void AskToLeave(Action then)
        {
            _askedToGo = then;
            bool match = _game.Match.Stage != MatchStage.BetweenMatches;
            string code = _game.Match.LobbyCode;
            _leaveTitle.text = match ? "Leave the match?" : $"Leave lobby {code}?";
            _leaveBody.text = match ? $"You'll lose your seat and score in lobby {code}." : "You can come back with the code while the lobby is open.";
            _leaveConfirm.text = match ? "Leave match" : "Leave lobby";
            ClosePopover(_settings);
            ClosePopover(_profilePopover);
            _leavePopover.AddToClassList("is-open");
        }

        private void ConfirmLeave()
        {
            ClosePopover(_leavePopover);
            _afterLeave = _askedToGo;
            _game.Leave();
        }

        // After leaving (the lobby state catches up a moment later): go where the tab was going.
        private void GoAfterLeaving()
        {
            if (_afterLeave == null || _game.Match.InLobby)
                return;
            Action go = _afterLeave;
            _afterLeave = null;
            go();
        }

        private Screen Current => _game.Match.InLobby ? (_game.Match.Stage == MatchStage.BetweenMatches ? Screen.Lobby : Screen.Match)
            : _daily.State.IsActive ? Screen.Daily : _daily.Archive.IsOpen ? Screen.Archive : _chosen;

        // ---- Each frame: show the state ----

        private void Update()
        {
            if (_root == null)
                return;
            ApplyLayout();
            // The palette's ground behind the board (the board view sets its own when it builds a board).
            if (Scratchpad.Camera != null)
                Scratchpad.Camera.backgroundColor = Palette.Current.Ground;

            ClosePopoversOnOutsideClick();
            GoAfterLeaving();
            GlideSide();
            Screen screen = Current;
            if (_scrolledFor != screen)
            {
                // A new screen starts at its top (the panel's scroll would otherwise carry over from the last one).
                _scrolledFor = screen;
                _scrollGliding = false;
                _side.scrollOffset = new Vector2(_side.scrollOffset.x, 0f);
            }
            ShowBoardVisibility();
            // Puzzle mode is the Practice side's: Play with friends and the lobby get plain random boards.
            Scratchpad.PlainPractice = screen == Screen.Play || screen == Screen.Lobby || screen == Screen.Match;
            _main.EnableInClassList("main--home", screen == Screen.Home);
            foreach (KeyValuePair<Screen, VisualElement> pair in _screens)
                pair.Value.EnableInClassList("is-visible", pair.Key == screen);
            _navHome.EnableInClassList("is-active", screen == Screen.Home);
            _navDaily.EnableInClassList("is-active", screen == Screen.Daily || screen == Screen.Archive);
            _navPlay.EnableInClassList("is-active", screen == Screen.Play || screen == Screen.Lobby || screen == Screen.Match);
            _navPractice.EnableInClassList("is-active", screen == Screen.Practice);
            _navSettings.EnableInClassList("is-active", _settings.ClassListContains("is-open"));
            _topbarSettings.EnableInClassList("is-active", _settings.ClassListContains("is-open"));
            _profileButton.EnableInClassList("is-active", _profilePopover.ClassListContains("is-open"));

            ShowProfile();
            ShowHome();
            if (screen == Screen.Daily)
                ShowDaily();
            else if (screen == Screen.Archive)
                ShowArchive();
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

            ShowSheet(screen);
            KeepFieldAboveKeyboard();
            if (Session.Round != null)
                ShowStage();
            ShowTutorial();
        }

        // The settings and profile popovers: one open at a time, closed by a click anywhere else (the board included) or
        // Esc. Read from the Input System: clicks on the board never reach the UI.
        private void TogglePopover(VisualElement popover)
        {
            bool open = !popover.ClassListContains("is-open");
            ClosePopover(_settings);
            ClosePopover(_profilePopover);
            ClosePopover(_leavePopover);
            popover.EnableInClassList("is-open", open);
        }

        // A closed popover gives up the focus too: a hidden text field holding it would keep the board from taking keys.
        private void ClosePopover(VisualElement popover)
        {
            popover.RemoveFromClassList("is-open");
            if (HasFocus(_profileNameField) && popover == _profilePopover)
                _profileNameField.Blur();
        }

        private void ClosePopoversOnOutsideClick()
        {
            bool settings = _settings.ClassListContains("is-open"), profile = _profilePopover.ClassListContains("is-open");
            bool leave = _leavePopover.ClassListContains("is-open");
            if (!settings && !profile && !leave)
                return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ClosePopover(_settings);
                ClosePopover(_profilePopover);
                ClosePopover(_leavePopover);
                return;
            }
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame)
                return;
            Vector2 position = pointer.position.ReadValue();
            float s = PanelScale;
            var onPanel = new Vector2(position.x / s, (UnityEngine.Screen.height - position.y) / s);
            // The button that opened a popover toggles it by itself.
            if (settings && !_settings.worldBound.Contains(onPanel) && !_navSettings.worldBound.Contains(onPanel)
                && !_topbarSettings.worldBound.Contains(onPanel))
                ClosePopover(_settings);
            if (profile && !_profilePopover.worldBound.Contains(onPanel) && !_profileButton.worldBound.Contains(onPanel)
                && !_topbarAvatar.worldBound.Contains(onPanel))
                ClosePopover(_profilePopover);
            // (A nav tab clicked meanwhile asks again on its own.)
            if (leave && !_leavePopover.worldBound.Contains(onPanel))
                ClosePopover(_leavePopover);
        }

        // ---- Lobby (between matches, including the results) ----

        private void ShowLobby()
        {
            MatchState match = _game.Match;
            MatchSettings settings = match.Settings;
            bool finished = match.Phase == MatchPhase.Finished;
            OrderLobbyCards(finished);
            int players = match.Standings.Count;
            if (finished)
            {
                _lobbyTitle.text = match.MatchWinner == match.MyName ? "You win the match!" : $"{match.MatchWinner} wins the match!";
                string line = ResultsLine(match);
                _lobbySub.text = match.IAmHost ? line : Join(line, $"waiting for {match.Host} to start a new match");
            }
            else
            {
                _lobbyTitle.text = "Lobby";
                _lobbySub.text = match.IAmHost ? "Share the code, then start when everyone's in." : $"Waiting for {match.Host} to start the match.";
            }
            _lobbySub.style.display = _lobbySub.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _lobbyCode.text = match.LobbyCode;
            _lobbyCopied.text = _copiedText;
            _lobbyCopied.style.display = Time.time < _copiedUntil ? DisplayStyle.Flex : DisplayStyle.None;

            // The results show the final standings instead of the players (who's still here is marked there).
            _lobbyStandings.style.display = finished ? DisplayStyle.Flex : DisplayStyle.None;
            if (finished)
                ShowStandings();
            _lobbyPlayersCard.style.display = finished ? DisplayStyle.None : DisplayStyle.Flex;
            _lobbyPlayersTitle.text = $"PLAYERS  ·  {players}";
            _lobbyAlone.style.display = !finished && players < 2 ? DisplayStyle.Flex : DisplayStyle.None;
            ShowPlayers(_lobbyPlayers, ref _shownLobbyPlayers, duringMatch: false);

            _lobbySettings.style.display = match.IAmHost ? DisplayStyle.Flex : DisplayStyle.None;
            _lobbySettingsView.style.display = !match.IAmHost && settings != null ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (Action refresh in _settingRefreshers)
                refresh();
            _lobbyStart.style.display = match.IAmHost ? DisplayStyle.Flex : DisplayStyle.None;
            _lobbyStart.text = finished ? "Play again" : players < 2 ? "Start match" : $"Start match  ·  {players} players";
            ShowFeedback(_lobbyFeedback, match.Feedback);
            _lobbyTip.text = finished ? "Practice on the board until the next match." : "Practice on the board while you wait.";
            ShowHistory(finished);
        }

        // The waiting room leads with the code and the players (Start under them); the results with the standings, Play
        // again and the boards, the code moving down.
        private void OrderLobbyCards(bool finished)
        {
            if (_resultsOrder == finished)
                return;
            _resultsOrder = finished;
            VisualElement screen = _screens[Screen.Lobby];
            VisualElement[] order = finished
                ? new[] { _lobbyTitle, _lobbySub, _lobbyStandings, _lobbyStart, _lobbyHistory, _lobbySettings, _lobbySettingsView,
                    _lobbyCodeCard, _lobbyPlayersCard, _lobbyFeedback, _lobbyTip, _lobbyLeave }
                : new[] { _lobbyTitle, _lobbySub, _lobbyStandings, _lobbyCodeCard, _lobbyPlayersCard, _lobbyStart, _lobbyHistory,
                    _lobbySettings, _lobbySettingsView, _lobbyFeedback, _lobbyTip, _lobbyLeave };
            foreach (VisualElement element in order)
                screen.Add(element); // re-adding moves it to the end
        }

        // "5 to 2 · 7 boards": the top scores and how many boards the match took.
        private static string ResultsLine(MatchState match)
        {
            var scores = new List<string>();
            foreach (FinalPlace place in match.FinalStandings)
            {
                if (scores.Count == 3)
                    break;
                scores.Add(place.Score.ToString());
            }
            int boards = match.History.Count;
            return Join(scores.Count > 1 ? string.Join(" to ", scores) : null, boards == 0 ? null : boards == 1 ? "1 board" : $"{boards} boards");
        }

        // The finished match's final scores (players who left stay, marked), then anyone who joined since.
        private void ShowStandings()
        {
            MatchState match = _game.Match;
            var signature = new System.Text.StringBuilder();
            foreach (FinalPlace place in match.FinalStandings)
                signature.Append(place.Name).Append('/').Append(place.Score).Append('/').Append(place.Left).Append('|');
            foreach (PlayerInfo p in match.Standings)
                signature.Append(p.Name).Append(',');
            if (signature.ToString() == _shownStandings)
                return;
            _shownStandings = signature.ToString();
            _lobbyStandingsList.Clear();

            var names = new List<string>();
            foreach (FinalPlace place in match.FinalStandings)
                names.Add(place.Name);
            var newcomers = new List<string>();
            foreach (PlayerInfo p in match.Standings)
                if (!names.Contains(p.Name))
                    newcomers.Add(p.Name);
            var all = new List<string>(names);
            all.AddRange(newcomers);

            int rank = 0, position = 0, lastScore = -1;
            foreach (FinalPlace place in match.FinalStandings)
            {
                position++;
                if (place.Score != lastScore)
                {
                    rank = position;
                    lastScore = place.Score;
                }
                VisualElement row = PlayerRowStart(place.Name, all, rank.ToString(), rank == 1);
                if (place.Left)
                    row.Add(Tag("left", "tag--left"));
                var score = new Label(place.Score.ToString());
                score.AddToClassList("player-score");
                row.Add(score);
                _lobbyStandingsList.Add(row);
            }
            foreach (string name in newcomers)
            {
                VisualElement row = PlayerRowStart(name, all, "", false);
                row.Add(Tag("new", "tag--host"));
                _lobbyStandingsList.Add(row);
            }
        }

        // A player row's start: an optional rank, the avatar (its colour from the name's place among `names`, so it stays
        // put as the order changes) and the name.
        private VisualElement PlayerRowStart(string name, List<string> names, string rank, bool first)
        {
            var row = new VisualElement();
            row.AddToClassList("player-row");
            row.EnableInClassList("is-me", name == _game.Match.MyName);
            if (rank != null)
            {
                var r = new Label(rank);
                r.AddToClassList("rank");
                r.EnableInClassList("rank--first", first);
                row.Add(r);
            }
            var sorted = new List<string>(names);
            sorted.Sort(StringComparer.Ordinal);
            RobotColor color = AvatarColors[Math.Max(0, sorted.IndexOf(name)) % AvatarColors.Length];
            var avatar = new Label(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?");
            avatar.AddToClassList("player-avatar");
            avatar.style.backgroundColor = Theme.RobotColor(color);
            if (color == RobotColor.Yellow)
                avatar.style.color = (Color)new Color32(0x1b, 0x1d, 0x2a, 0xff);
            row.Add(avatar);
            var label = new Label(name == _game.Match.MyName ? $"{name} (you)" : name);
            label.AddToClassList("player-name");
            row.Add(label);
            return row;
        }

        // The finished match's boards, one row each (who won it, the shortest); picking one opens it: its board on screen and
        // everyone's best plus the shortest to replay.
        private void ShowHistory(bool finished)
        {
            IReadOnlyList<HistoryRound> history = _game.Match.History;
            bool show = finished && history.Count > 0;
            _lobbyHistory.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                _historyIndex = -1;
                _shownHistory = null;
                return;
            }
            if (_historyIndex >= history.Count)
                _historyIndex = -1;
            string signature = _historyIndex + "/" + history.Count;
            if (signature == _shownHistory)
                return;
            _shownHistory = signature;
            _historyList.Clear();
            for (int i = 0; i < history.Count; i++)
            {
                HistoryRound round = history[i];
                int index = i;
                var row = new Button();
                row.AddToClassList("btn");
                row.AddToClassList("history-row");
                row.EnableInClassList("is-selected", i == _historyIndex);
                AddShadow(row);
                var text = new Label(HistoryLine(round));
                text.AddToClassList("history-row-text");
                text.pickingMode = PickingMode.Ignore;
                var meta = new Label(round.Shortest != null ? $"shortest {round.Shortest.Count}" : "");
                meta.AddToClassList("history-row-meta");
                meta.pickingMode = PickingMode.Ignore;
                row.Add(text);
                row.Add(meta);
                row.clicked += () =>
                {
                    SelectHistory(index);
                    row.Blur();
                };
                _historyList.Add(row);
                if (i != _historyIndex)
                    continue;
                var detail = new VisualElement();
                detail.AddToClassList("history-detail");
                foreach (HistorySolution solution in round.Solutions)
                {
                    string who = solution.Name == _game.Match.MyName ? "You" : solution.Name;
                    string won = solution.Name == round.WinnerName ? "  (won)" : "";
                    AddSolutionButton(detail, round, $"{who}  ·  {solution.Moves.Count} moves{won}", new ReplaySegment(who, solution.Moves, 0.6f));
                }
                if (round.Shortest != null)
                    AddSolutionButton(detail, round, $"Shortest possible  ·  {round.Shortest.Count} moves",
                        new ReplaySegment("Shortest possible", round.Shortest, 0.6f, isShortest: true));
                _historyList.Add(detail);
            }
        }

        private string HistoryLine(HistoryRound round)
        {
            if (round.WinnerName == null)
                return $"Board {round.MatchRound}  ·  nobody";
            string who = round.WinnerName == _game.Match.MyName ? "you" : round.WinnerName;
            foreach (HistorySolution solution in round.Solutions)
                if (solution.Name == round.WinnerName)
                    return $"Board {round.MatchRound}  ·  {who}, {solution.Moves.Count}";
            return $"Board {round.MatchRound}  ·  {who}";
        }

        private void AddSolutionButton(VisualElement parent, HistoryRound round, string label, ReplaySegment segment)
        {
            var button = new Button { text = label };
            button.AddToClassList("btn");
            button.AddToClassList("solution-btn");
            AddShadow(button);
            if (segment.IsShortest)
                button.AddToClassList("solution-btn--shortest");
            button.clicked += () =>
            {
                if (Session.Round != round.Board)
                    Scratchpad.ShowHistoryBoard(round.Board);
                Scratchpad.ShowSolution(segment);
                button.Blur();
            };
            parent.Add(button);
        }

        // Opens a board of the history (picking the open one again closes it).
        private void SelectHistory(int index)
        {
            IReadOnlyList<HistoryRound> history = _game.Match.History;
            if (index < 0 || index >= history.Count)
                return;
            if (index == _historyIndex)
            {
                _historyIndex = -1;
                return;
            }
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
            _matchSub.text = match.Settings != null ? $"First to {match.Settings.PointsToWin}" : "";

            // The round card: its clock (before the first solution: the time to find one; after: the round's countdown and
            // the best so far), then the round's result while the replays play.
            string caption, value = "", unit = "", detail = "", sub = "";
            bool bumped = false, result = false;
            float replayed = -1f;
            int firstTimer = match.Settings != null ? match.Settings.FirstTimerSeconds : 60;
            switch (stage)
            {
                case MatchStage.Dealing:
                    caption = "GET READY";
                    sub = "Dealing the first board...";
                    break;
                case MatchStage.Countdown:
                    float left = match.CountdownLeft(now);
                    caption = "NEXT BOARD";
                    value = left > 0f ? Mathf.CeilToInt(left).ToString() : "";
                    sub = left > 0f ? $"Round {next} starts soon." : "Dealing...";
                    break;
                case MatchStage.RoundOver:
                    result = match.ResultTitle != null;
                    caption = (match.ResultTitle ?? "Round over").ToUpperInvariant();
                    detail = match.ResultDetail ?? "";
                    sub = ReplayText();
                    ReplaySegment replay = Session.CurrentReplay;
                    if (replay != null && replay.Moves.Count > 0)
                        replayed = (float)Session.ReplayStep / replay.Moves.Count;
                    break;
                default:
                    if (match.TimerRunning)
                    {
                        caption = "ROUND ENDS IN";
                        value = Clock(match.TimeLeft(now));
                        bumped = match.TimeJustAdded(now);
                        unit = bumped ? "+ more time" : "";
                    }
                    else
                    {
                        caption = "NO SOLUTION YET";
                        if (match.HasTimeLimit)
                        {
                            value = Clock(match.TimeLimitLeft(now));
                            unit = "left to find one";
                        }
                        sub = $"The first solution starts a {firstTimer} s countdown for everyone.";
                    }
                    break;
            }
            _roundCard.EnableInClassList("card--gold", result);
            _timerCaption.text = caption;
            _timerValue.text = value;
            _timerValue.EnableInClassList("is-bumped", bumped);
            _timerUnit.text = unit;
            _timerUnit.EnableInClassList("is-bumped", bumped);
            _timerValue.parent.style.display = value.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _roundDetail.text = detail;
            _roundDetail.style.display = detail.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _timerSub.text = sub;
            _timerSub.style.display = sub.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _roundProgress.style.display = replayed >= 0f ? DisplayStyle.Flex : DisplayStyle.None;
            if (replayed >= 0f)
                _roundProgressFill.style.width = Length.Percent(100f * Mathf.Clamp01(replayed));

            bool best = stage == MatchStage.Solving && match.TimerRunning && match.BestPlayer != null;
            _bestBox.style.display = best ? DisplayStyle.Flex : DisplayStyle.None;
            if (best)
            {
                bool mine = match.BestPlayer == match.MyName;
                _matchBest.text = $"{(mine ? "You" : match.BestPlayer)}  ·  {match.BestMoves} moves";
                _matchBestSub.text = mine ? "Best so far. Find a shorter one to stay ahead." : "Best so far. Beat it to take the round.";
            }
            _matchWatch.style.display = match.CanWatchBest ? DisplayStyle.Flex : DisplayStyle.None;
            _matchWatch.SetEnabled(!Session.IsReplaying);

            ShowPlayers(_matchPlayers, ref _shownMatchPlayers, duringMatch: true);
            _matchDone.style.display = match.DoneAction != DoneAction.None ? DisplayStyle.Flex : DisplayStyle.None;
            _matchDone.text = DoneLabel(match);
            ShowFeedback(_matchFeedback, match.Feedback);
            // How rounds work, on the match's first board only.
            _matchTip.text = stage == MatchStage.Solving && match.MatchRound <= 1
                ? "Reaching the target sends your solution. Only a shorter one beats the best." : "";
            _matchTip.style.display = _matchTip.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private string ReplayText()
        {
            ReplaySegment replay = Session.CurrentReplay;
            if (replay == null)
                return "The next board is on its way.";
            int step = Session.ReplayStep;
            return step == 0 ? $"Replaying {replay.Label}..." : $"Replaying {replay.Label}  ·  move {step} of {replay.Moves.Count}";
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

        // The players, in standings order, rebuilt only when something about them changes. During a match each shows their
        // points out of the target (with a bar of pips when the target is small); the waiting room shows no scores.
        private void ShowPlayers(VisualElement list, ref string shown, bool duringMatch)
        {
            MatchState match = _game.Match;
            int target = match.Settings != null ? match.Settings.PointsToWin : 0;
            var signature = new System.Text.StringBuilder(duringMatch ? "m" : "l");
            foreach (PlayerInfo p in match.Standings)
                signature.Append('|').Append(p.Name).Append('/').Append(p.Score).Append('/').Append(p.Done).Append('/')
                    .Append(p.Leading).Append('/').Append(p.Connected);
            signature.Append('|').Append(match.Host).Append('|').Append(match.BestMoves).Append('|').Append(target);
            if (signature.ToString() == shown)
                return;
            shown = signature.ToString();
            list.Clear();
            var names = new List<string>();
            foreach (PlayerInfo p in match.Standings)
                names.Add(p.Name);
            foreach (PlayerInfo p in match.Standings)
                list.Add(PlayerRow(p, names, duringMatch, target));
        }

        private VisualElement PlayerRow(PlayerInfo p, List<string> names, bool duringMatch, int target)
        {
            MatchState match = _game.Match;
            VisualElement row = PlayerRowStart(p.Name, names, null, false);
            if (p.Name == match.Host)
                row.Add(Tag("host", "tag--host"));
            if (!p.Connected)
                row.Add(Tag("reconnecting...", "tag--away"));
            else if (duringMatch && p.Leading)
                row.Add(Tag(match.BestMoves > 0 ? $"leading · {match.BestMoves}" : "leading", "tag--lead"));
            else if (duringMatch && p.Done)
                row.Add(Tag("done", "tag--done"));
            if (!duringMatch)
                return row;

            var box = new VisualElement();
            box.AddToClassList("player-score-box");
            var line = new VisualElement();
            line.AddToClassList("row");
            var score = new Label(p.Score.ToString());
            score.AddToClassList("player-score");
            line.Add(score);
            if (target > 0)
            {
                var of = new Label($"/ {target}");
                of.AddToClassList("player-score-of");
                line.Add(of);
            }
            box.Add(line);
            if (target > 0 && target <= 10)
            {
                var pips = new VisualElement();
                pips.AddToClassList("pips");
                for (int i = 0; i < target; i++)
                {
                    var pip = new VisualElement();
                    pip.AddToClassList("pip");
                    pip.EnableInClassList("is-on", i < p.Score);
                    pips.Add(pip);
                }
                box.Add(pips);
            }
            row.Add(box);
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
            _dailyTime.EnableInClassList("stat-value--long", _dailyTime.text.Length > 5); // "12h 30m" doesn't fit at full size
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
                    // Which way the robot went: Direction runs clockwise from Up, a quarter turn each.
                    var arrow = new VisualElement();
                    arrow.AddToClassList("square-arrow");
                    arrow.style.backgroundImage = new StyleBackground(ShapeSprites.Arrow);
                    arrow.style.unityBackgroundImageTintColor = move.Robot == RobotColor.Yellow
                        ? new Color(0.11f, 0.11f, 0.16f, 0.85f) : new Color(1f, 1f, 1f, 0.95f);
                    arrow.style.rotate = new Rotate(90f * (int)move.Direction);
                    square.Add(arrow);
                    _dailySquares.Add(square);
                }
            }

            _dailyBoard.style.display = loading || error ? DisplayStyle.None : DisplayStyle.Flex;
            _dailyTabToday.EnableInClassList("is-on", !_streaksTab);
            _dailyTabStreaks.EnableInClassList("is-on", _streaksTab);
            string rows;
            if (_streaksTab)
            {
                StreakLeaderboard streaks = _daily.Archive.Streaks;
                bool any = streaks?.Top != null && streaks.Top.Length > 0;
                _dailyRank.text = streaks != null && streaks.MyRank == 0 ? "Solve today's puzzle to start a streak." : "";
                _dailyBoardEmpty.text = streaks == null ? "Loading..." : any ? "" : "Nobody has a streak going yet.";
                rows = "s" + (any ? string.Join("|", Array.ConvertAll(streaks.Top, e => $"{e.Name}/{e.Days}/{e.Rank}/{e.IsMe}")) + streaks.MyRank : "");
                if (rows != _shownRows)
                {
                    _shownRows = rows;
                    _dailyRows.Clear();
                    if (any)
                    {
                        for (int i = 0; i < streaks.Top.Length && i < 5; i++)
                            _dailyRows.Add(StreakRow(streaks.Top[i].Rank, streaks.Top[i].IsMe ? "You" : streaks.Top[i].Name, streaks.Top[i].Days, streaks.Top[i].IsMe));
                        if (streaks.MyRank > 0 && Array.FindIndex(streaks.Top, e => e.IsMe) is int mine && (mine < 0 || mine >= 5))
                            _dailyRows.Add(StreakRow(streaks.MyRank, "You", streaks.MyStreak, true));
                    }
                }
            }
            else
            {
                DailyLeaderboard board = daily.Leaderboard;
                bool hasBoard = solved && board != null && board.Top != null && board.Top.Length > 0;
                _dailyRank.text = "";
                _dailyBoardEmpty.text = solved ? (hasBoard ? "" : "Loading...") : "Solve today's puzzle to see how you compare.";
                rows = hasBoard ? string.Join("|", Array.ConvertAll(board.Top, e => $"{e.Name}/{e.Moves}/{e.Seconds:0}/{e.IsMe}")) + board.MyRank : "";
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
            }
            _dailyRank.style.display = _dailyRank.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _dailyBoardEmpty.style.display = _dailyBoardEmpty.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ShowStrip(loading || error);
            _dailyNext.text = daily.Stage == DailyStage.Playing ? $"Next puzzle in {DailyState.Countdown(daily.UntilNext(now))}" : "";
        }

        private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

        private static VisualElement StreakRow(int place, string name, int days, bool me)
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
            var count = new Label(Days(days));
            count.AddToClassList("lb-streak");
            row.Add(rank);
            row.Add(who);
            row.Add(count);
            return row;
        }

        // ---- Past puzzles ----

        private void BindArchive()
        {
            _screens[Screen.Archive] = _root.Q("screen-archive");
            _archiveSub = _root.Q<Label>("archive-sub");
            _archiveMonth = _root.Q<Label>("archive-month");
            _archiveGrid = _root.Q("archive-grid");
            _archiveDay = _root.Q("archive-day");
            _archiveDayTitle = _root.Q<Label>("archive-day-title");
            _archiveDayHeadline = _root.Q<Label>("archive-day-headline");
            _archiveDayDetail = _root.Q<Label>("archive-day-detail");
            _archiveStats = _root.Q("archive-stats");
            _archiveDayMoves = _root.Q<Label>("archive-day-moves");
            _archiveLaterMoves = _root.Q<Label>("archive-later-moves");
            _archiveShortest = _root.Q<Label>("archive-shortest");
            _archiveNotice = _root.Q<Label>("archive-notice");
            _archiveBoard = _root.Q("archive-board");
            _archiveRows = _root.Q("archive-rows");
            _archiveBoardEmpty = _root.Q<Label>("archive-board-empty");
            _archivePrev = Click("archive-prev", () => _daily.Archive.ShowMonth(-1));
            _archiveNext = Click("archive-next", () => _daily.Archive.ShowMonth(1));
            _archiveWatch = Click("archive-watch", _daily.WatchMine);
            _archiveSolution = Click("archive-solution", _daily.ShowShortest);
            Click("archive-restart", _daily.RestartPast);
            Click("archive-today", OpenDaily);

            VisualElement legend = _root.Q("archive-legend");
            foreach ((string classes, string text) in new[]
                {
                    ("cal-cell--perfect", "Perfect"), ("cal-cell--solved", "Solved"), ("legend-ring cal-ring--solved", "Solved later"),
                    ("cal-cell--none", "Not solved"),
                })
                legend.Add(LegendItem("legend-swatch " + classes, text));
            legend.Add(LegendItem("legend-swatch cal-seen", "Saw the solution"));
        }

        private static VisualElement LegendItem(string classes, string text)
        {
            var item = new VisualElement();
            item.AddToClassList("legend-item");
            var swatch = new VisualElement();
            foreach (string name in classes.Split(' '))
                swatch.AddToClassList(name);
            var label = new Label(text);
            label.AddToClassList("legend-text");
            item.Add(swatch);
            item.Add(label);
            return item;
        }

        // A day's look: today in gold; else filled with the result on the day, ringed by a later play worth showing.
        private static string MarkClass(DailyDay day, int today)
        {
            if (day.Number == today)
                return "cal-cell--today";
            string classes = "cal-cell--" + DailyArchive.DayResult(day).ToString().ToLowerInvariant();
            DayMark ring = DailyArchive.LaterRing(day);
            return ring == DayMark.None ? classes : classes + " cal-ring cal-ring--" + ring.ToString().ToLowerInvariant();
        }

        private static void AddMark(VisualElement cell, DailyDay day, int today)
        {
            foreach (string name in MarkClass(day, today).Split(' '))
                cell.AddToClassList(name);
        }

        // The daily screen's last seven days: a past day opens it on the calendar.
        private void ShowStrip(bool hidden)
        {
            DailyArchive archive = _daily.Archive;
            IReadOnlyList<DailyDay> days = archive.LastDays(7);
            bool show = !hidden && archive.Today > 1 && days.Count > 1;
            _dailyPast.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            string key = show ? string.Join("|", days.Select(d => $"{d.Number}{MarkClass(d, archive.Today)}")) : "";
            if (key == _shownStrip)
                return;
            _shownStrip = key;
            _dailyStrip.Clear();
            if (!show)
                return;
            foreach (DailyDay day in days)
            {
                DailyArchive.TryParse(day.Date, out DateTime date);
                int number = day.Number;
                var cell = new Button(() =>
                {
                    if (number != archive.Today)
                        _daily.OpenArchive(number);
                });
                cell.AddToClassList("cal-cell");
                cell.AddToClassList("strip-day");
                AddMark(cell, day, archive.Today);
                var dow = new Label(date.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant());
                dow.AddToClassList("strip-dow");
                var num = new Label(date.Day.ToString());
                num.AddToClassList("strip-num");
                cell.Add(dow);
                cell.Add(num);
                AddSeen(cell, day);
                _dailyStrip.Add(cell);
            }
        }

        private static void AddSeen(VisualElement cell, DailyDay day)
        {
            if (!day.SawSolution)
                return;
            var dot = new VisualElement();
            dot.AddToClassList("cal-seen");
            dot.pickingMode = PickingMode.Ignore;
            cell.Add(dot);
        }

        private void ShowArchive()
        {
            DailyArchive archive = _daily.Archive;
            _archiveSub.text = archive.HasCalendar
                ? $"{archive.SolvedCount} of {archive.PastCount} solved, {archive.PerfectCount} perfect."
                : archive.Error ?? "Loading...";
            _archiveMonth.text = archive.ShownYear > 0
                ? new DateTime(archive.ShownYear, archive.ShownMonth, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture) : "";
            _archivePrev.SetEnabled(archive.CanShowMonth(-1));
            _archiveNext.SetEnabled(archive.CanShowMonth(1));
            ShowCalendar(archive);

            PastPuzzle past = archive.Past;
            int shown = past?.Number ?? archive.Selected;
            DailyDay day = archive.Day(shown);
            _archiveDay.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ShowPastLeaderboard(past);
            if (shown == 0)
                return;
            DailyArchive.TryParse(day?.Date ?? "", out DateTime date);
            _archiveDayTitle.text = day != null
                ? $"{date.ToString("dddd, MMM d", CultureInfo.InvariantCulture).ToUpperInvariant()}  ·  #{day.Number}" : $"#{shown}";
            _archiveDayHeadline.text = past == null ? (archive.LoadingPast ? "Loading..." : "Couldn't load it")
                : past.IsPerfect ? $"Perfect: {past.BestMoves} moves"
                : past.BestMoves > 0 ? $"Solved in {past.BestMoves} moves"
                : past.Opened || past.SawSolution ? "Not solved yet" : "Not played yet";
            _archiveStats.style.display = past != null ? DisplayStyle.Flex : DisplayStyle.None;
            _archiveWatch.style.display = past != null && past.MySolution.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _archiveSolution.style.display = past != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (past == null)
            {
                _archiveDayDetail.text = archive.LoadingPast ? "" : archive.Error ?? "";
                _archiveNotice.style.display = DisplayStyle.None;
                return;
            }
            _archiveDayMoves.text = past.DayMoves > 0 ? past.DayMoves.ToString() : "-";
            _archiveLaterMoves.text = past.LaterMoves > 0 ? past.LaterMoves.ToString() : "-";
            _archiveShortest.text = past.ShortestLength.ToString();
            string onTheDay = past.DayMoves > 0 ? $"On the day you solved it in {past.DayMoves} moves ({DailyState.Clock(past.DaySeconds)})."
                : past.Opened ? "You opened it on the day but didn't solve it." : "You didn't play it on the day.";
            _archiveDayDetail.text = onTheDay + (past.SawSolution ? " You've seen the solution." : "");
            _archiveSolution.text = past.SawSolution ? "Show the solution again" : "Show the solution";
            string notice = _daily.Notice ?? (past.Submitting ? "Sending..." : null);
            _archiveNotice.text = notice ?? "";
            _archiveNotice.style.display = notice != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // The picked day's own leaderboard: its top 5, and the player's row if they solved it on the day but aren't in them.
        private void ShowPastLeaderboard(PastPuzzle past)
        {
            DailyLeaderboard board = past?.Leaderboard;
            _archiveBoard.style.display = board != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool any = board?.Top != null && board.Top.Length > 0;
            _archiveBoardEmpty.style.display = board != null && !any ? DisplayStyle.Flex : DisplayStyle.None;
            string rows = any ? board.Number + ":" + string.Join("|", Array.ConvertAll(board.Top, e => $"{e.Name}/{e.Moves}/{e.Seconds:0}/{e.IsMe}")) + board.MyRank : "";
            if (rows == _shownPastRows)
                return;
            _shownPastRows = rows;
            _archiveRows.Clear();
            if (!any)
                return;
            for (int i = 0; i < board.Top.Length && i < 5; i++)
                _archiveRows.Add(LeaderboardRow(i + 1, board.Top[i].IsMe ? "You" : board.Top[i].Name, board.Top[i].Moves, board.Top[i].Seconds, board.Top[i].IsMe));
            if (board.MyRank > 5)
                _archiveRows.Add(LeaderboardRow(board.MyRank, "You", past.DayMoves, past.DaySeconds, true));
        }

        // The shown month, Sunday first: a cell per day, coloured by the player's result; rebuilt only when something changes.
        private void ShowCalendar(DailyArchive archive)
        {
            if (archive.ShownYear == 0)
            {
                _archiveGrid.Clear();
                _shownCalendar = null;
                return;
            }
            var first = new DateTime(archive.ShownYear, archive.ShownMonth, 1);
            int count = DateTime.DaysInMonth(first.Year, first.Month);
            var key = new System.Text.StringBuilder($"{first:yyyy-MM}/{archive.Selected}/{archive.Today}/");
            for (int d = 1; d <= count; d++)
            {
                DailyDay day = archive.DayOn(first.AddDays(d - 1));
                key.Append(day == null ? "-" : MarkClass(day, archive.Today) + (day.SawSolution ? "s" : "")).Append(',');
            }
            if (key.ToString() == _shownCalendar)
                return;
            _shownCalendar = key.ToString();
            _archiveGrid.Clear();

            var head = new VisualElement();
            head.AddToClassList("cal-row");
            foreach (string name in new[] { "S", "M", "T", "W", "T", "F", "S" })
            {
                var label = new Label(name);
                label.AddToClassList("cal-dow");
                head.Add(label);
            }
            _archiveGrid.Add(head);

            int offset = (int)first.DayOfWeek; // Sunday first, as North American calendars are
            int cells = (offset + count + 6) / 7 * 7;
            VisualElement row = null;
            for (int i = 0; i < cells; i++)
            {
                if (i % 7 == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("cal-row");
                    _archiveGrid.Add(row);
                }
                int dayOfMonth = i - offset + 1;
                DailyDay day = dayOfMonth >= 1 && dayOfMonth <= count ? archive.DayOn(first.AddDays(dayOfMonth - 1)) : null;
                int number = day?.Number ?? 0;
                var cell = new Button(() =>
                {
                    if (number > 0)
                        _daily.SelectPast(number);
                });
                cell.AddToClassList("cal-cell");
                if (dayOfMonth < 1 || dayOfMonth > count)
                {
                    cell.AddToClassList("cal-cell--empty");
                    cell.SetEnabled(false);
                }
                else
                {
                    cell.text = dayOfMonth.ToString();
                    if (day == null)
                    {
                        cell.AddToClassList("cal-cell--off"); // before the first puzzle, or still to come
                        cell.SetEnabled(false);
                    }
                    else
                    {
                        AddMark(cell, day, archive.Today);
                        cell.EnableInClassList("is-selected", number == archive.Selected);
                        AddSeen(cell, day);
                    }
                }
                row.Add(cell);
            }
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
            // A name set elsewhere (the other box, ?name= in the link) shows in both boxes, except the one being typed in.
            foreach (TextField field in new[] { _nameField, _profileNameField })
                if (field.value != name && !HasFocus(field))
                    field.SetValueWithoutNotify(name);
            _profileAvatar.text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
            _topbarAvatar.text = _profileAvatar.text;
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
            _goalIcon.style.backgroundImage = new StyleBackground(target.Robot.HasValue
                ? ShapeSprites.TargetSymbol(target.Robot) : ShapeSprites.AnyTargetSymbol(theme.RobotColors));
            _goalIcon.style.unityBackgroundImageTintColor = target.Robot.HasValue ? targetColor : Color.white;
            string robot = target.Robot.HasValue ? $"the {target.Robot.Value} robot" : "any robot";
            int best = Scratchpad.PracticeShortest;
            string color = ColorUtility.ToHtmlStringRGB(Readable(targetColor));
            // Always one line, so the board never shrinks: the short form ("Red robot to its target in 6") when the
            // sentence doesn't fit beside the banner (a narrow stage, a replay's longer banner), else an ellipsis.
            string full = $"Get <b><color=#{color}>{robot}</color></b> to its target" + (best > 0 ? $" in {best} moves" : "");
            string brief = $"<b><color=#{color}>{(target.Robot.HasValue ? $"{target.Robot.Value} robot" : "Any robot")}</color></b> to its target" + (best > 0 ? $" in {best}" : "");
            _goalLabel.text = Compact || !GoalFits(full) ? brief : full;

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

            if (BoardHidden || SheetCoversBoard)
            {
                _boardTip.RemoveFromClassList("is-visible");
                _boardOverlay.RemoveFromClassList("is-visible");
                return;
            }
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
                _overlayHint.text = Compact ? "Tap New to stop" : Touch ? "Tap New board to stop" : "N or Esc to stop";
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
            bool show = !seen && !_tutorial.IsActive && Session.AcceptsInput && !Session.IsCountingDown && Scratchpad.SearchStatus == null;
            _boardTip.EnableInClassList("is-visible", show);
            if (!show)
                return;
            Rect board = BoardOnPanel();
            _boardTip.style.left = board.x + board.width * 0.08f;
            _boardTip.style.width = board.width * 0.84f;
            _boardTip.style.top = board.yMax - 64f;
        }

        // ---- The first-time tutorial (TutorialState): a hand-made board with a prompt card over its bottom ----

        private void StartTutorial()
        {
            if (_game.Match.InLobby)
                return;
            PlayerPrefs.SetInt(TutorialDoneKey, 1);
            PlayerPrefs.SetInt(TipSeenKey, 1); // the card says how to move
            PlayerPrefs.Save();
            _tutorial.Start();
            _tutorialShown = null;
            UsageTracker.Track("tutorial_start");
            Scratchpad.ShowTutorial(_tutorial.Round, TutorialState.BestMoves);
            _chosen = Screen.Practice;
            _sheetTarget = 0f; // phones: the panel slides down to show the board (pressed from Practice, it's up)
        }

        // Skip or Start playing: Home (the daily puzzle and Play with friends come next), a normal practice board behind it.
        private void EndTutorial(bool finished)
        {
            UsageTracker.Track(finished ? "tutorial_finish" : "tutorial_skip");
            _tutorial.End();
            _tutorialCard.RemoveFromClassList("is-visible");
            Scratchpad.PlayOffline();
            _chosen = Screen.Home;
        }

        private void ShowTutorial()
        {
            if (!_tutorialChecked)
            {
                // The first frame (the game controller has read the address by now): a new player who didn't come
                // from a lobby link starts here.
                _tutorialChecked = true;
                if (_newPlayer && !_game.LaunchedIntoLobby && !_game.SkipTutorial)
                    StartTutorial();
            }
            if (_tutorial.IsActive && Session.Round != _tutorial.Round)
                _tutorial.End(); // another board took over (the daily, a lobby): the tutorial is over
            if (!_tutorial.IsActive)
            {
                _tutorialCard.RemoveFromClassList("is-visible");
                return;
            }

            int moves = Session.Moves.Count;
            _tutorial.Update(Session.Robots, moves, Session.IsSolved);
            if (_tutorial.Step != _tutorialShown)
            {
                _tutorialShown = _tutorial.Step;
                _tutorialStepAt = Time.time;
                if (_tutorial.RobotToSelect is RobotColor robot && Session.AcceptsInput)
                    Scratchpad.SelectRobot(robot);
            }
            bool solved = _tutorial.Step == TutorialStep.Solved;
            // A solve shows its slide and burst before the card comes back.
            bool show = !BoardHidden && !SheetCoversBoard && (!solved || Time.time - _tutorialStepAt > 1.2f);
            _tutorialCard.EnableInClassList("is-visible", show);
            if (!show)
                return;
            // Where it goes depends on its height (phones), unknown until it has been laid out once: invisible until then,
            // so it never shows in a wrong spot for a frame.
            float measured = _tutorialCard.layout.height;
            _tutorialCard.style.visibility = !float.IsNaN(measured) && measured > 1f ? Visibility.Visible : Visibility.Hidden;

            bool touch = Touch || Compact;
            _tutorialBadge.text = (_tutorial.Progress + 1).ToString();
            _tutorialBadge.EnableInClassList("is-solved", solved);
            _tutorialTitle.text = _tutorial.Title(moves);
            _tutorialBody.text = _tutorial.Body(touch, moves);
            for (int i = 0; i < _tutorialDots.Count; i++)
            {
                _tutorialDots[i].EnableInClassList("is-on", i <= _tutorial.Progress);
                _tutorialDots[i].EnableInClassList("is-current", i == _tutorial.Progress);
            }
            _tutorialSkip.style.display = solved ? DisplayStyle.None : DisplayStyle.Flex;
            _tutorialDone.style.display = solved ? DisplayStyle.Flex : DisplayStyle.None;

            // Over the bottom of the board (the lesson happens in its upper half); on a phone the board is too small for
            // that, so under the Undo / Reset row, over the panel (not needed meanwhile).
            Rect board = BoardOnPanel();
            if (Compact)
            {
                // Under the controls if it fits above the tab bar and the bottom of what the browser shows (its toolbar
                // can cover the page's bottom); else over the board's bottom rows, as on a computer (Undo / Reset stay free).
                float bottom = _nav.worldBound.yMin;
                if (Viewport.VisibleArea(out float topCss, out float heightCss))
                    bottom = Mathf.Min(bottom, (topCss + heightCss) * Viewport.PixelRatio / PanelScale);
                float height = float.IsNaN(_tutorialCard.layout.height) ? 0f : _tutorialCard.layout.height;
                Rect controls = _boardControls.worldBound;
                float top = controls.yMax + 10f + height <= bottom - 8f ? controls.yMax + 10f : controls.yMin - 8f - height;
                _tutorialCard.style.left = board.x;
                _tutorialCard.style.width = board.width;
                _tutorialCard.style.top = top - _root.worldBound.y;
                _tutorialCard.style.bottom = StyleKeyword.Auto;
                return;
            }
            // Anchored by its bottom edge, so it's in place from the first frame (its height isn't known until laid out).
            float inset = board.width * 0.06f;
            _tutorialCard.style.left = board.x + inset;
            _tutorialCard.style.width = board.width - 2f * inset;
            _tutorialCard.style.top = StyleKeyword.Auto;
            _tutorialCard.style.bottom = _root.layout.height - (board.yMax - inset);
        }

        // Whether the goal text fits on one line in the room beside the icon (the goal's width doesn't depend on its text).
        // Measured by the hidden copy, so it lags a frame behind a new text.
        private bool GoalFits(string text)
        {
            _goalMeasure.text = text;
            float room = _goalLabel.parent.layout.width - _goalIcon.layout.width - _goalLabel.resolvedStyle.marginLeft;
            float width = _goalMeasure.layout.width;
            if (float.IsNaN(room) || float.IsNaN(width) || room <= 0f)
                return true;
            return width <= room;
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

        // A robot colour as text: lightened on dark grounds, darkened on light ones (yellow on cream is unreadable otherwise).
        private static Color Readable(Color color) => Palette.Current.IsLight ? Color.Lerp(color, Color.black, 0.4f) : Color.Lerp(color, Color.white, 0.25f);

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

        private bool HasFocus(TextField field)
        {
            var focused = _root?.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == field || focused.GetFirstAncestorOfType<TextField>() == field);
        }

        // Typing in a text field: the board ignores the keys.
        private bool IsTyping()
        {
            Focusable focused = _root?.panel?.focusController?.focusedElement;
            return focused is TextField || (focused is VisualElement element && element.GetFirstAncestorOfType<TextField>() != null);
        }
    }
}
