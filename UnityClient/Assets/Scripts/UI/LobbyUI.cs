using System.Collections;
using System.Collections.Generic;
using System.Text;
using Ricochet.Client.Game;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// Join panel (name, lobby code) and in-game panel: players, then either the match setup / results
    /// (host settings + Start) between matches, or the round (timer, best solution) during one.
    /// Reaching the target during a round sends the solution if it beats the best (no button).
    /// Feeds the <see cref="NetworkManager"/>'s messages into <see cref="MatchState"/> (which decides
    /// what's going on), keeps the <see cref="ScratchpadController"/> board in step, and shows the state
    /// with placeholder uGUI widgets.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private ScratchpadController _scratchpad;
        [Tooltip("Fonts and colours for the panel (see UiSkin).")]
        [SerializeField] private GameTheme _theme;

        [Header("Join panel")]
        [SerializeField] private GameObject _joinPanel;
        [SerializeField] private InputField _nameInput;
        [SerializeField] private InputField _codeInput;
        [SerializeField] private InputField _serverInput;
        [Tooltip("Editor only: show the Server field, e.g. to test against a local server (localhost:5022).")]
        [SerializeField] private bool _showServerField;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Text _statusText;

        [Header("Game panel")]
        [SerializeField] private GameObject _gamePanel;
        [SerializeField] private Text _lobbyText;
        [SerializeField] private Text _playersText;
        [SerializeField] private Text _roundText;
        [SerializeField] private Text _feedbackText;
        [Tooltip("The old Lock In button (solutions are sent on reaching the target), reused at runtime as "
            + "\"Watch X's best\" when the lobby shows the best solution.")]
        [SerializeField] private Button _lockInButton;
        [SerializeField] private Button _doneButton;
        [SerializeField] private Text _doneLabel;
        [SerializeField] private Button _leaveButton;

        [Header("Match setup / results")]
        [SerializeField] private Text _playersHeader;
        [SerializeField] private Text _matchText;
        [SerializeField] private GameObject _settingsGroup;
        [SerializeField] private InputField _pointsInput;
        [SerializeField] private InputField _firstTimerInput;
        [SerializeField] private InputField _extensionInput;
        [SerializeField] private InputField _roundLimitInput;
        [SerializeField] private Button _startButton;
        [SerializeField] private Text _startLabel;

        // Time to see the robot land before a solved board goes back to the start.
        private const float SolvedResetDelay = 0.9f;

        private const string NamePrefKey = "Ricochet.PlayerName";
        // Renamed when the cloud server came in, so tunnel addresses saved by playtest builds are dropped.
        private const string ServerPrefKey = "Ricochet.ServerAddress";

        private readonly MatchState _match = new MatchState();
        private Text _watchLabel;
        private Button _showBestButton;
        private Text _showBestLabel;
        private InputField _minMovesInput;
        private InputField _maxMovesInput;

        private BoardSession Board => _scratchpad.Session;

        private void Awake()
        {
            // Widgets the scene doesn't have yet, made before the skin styles everything.
            _lockInButton.gameObject.name = "Watch Button";
            _watchLabel = _lockInButton.GetComponentInChildren<Text>(true);
            CreateBoardMovesRow();
            CreateShowBestRow();
            UiSkin.Apply(transform, GameTheme.OrDefault(_theme));
            gameObject.AddComponent<MatchHistoryOverlay>().Init(_match, _scratchpad, _theme);
            _joinPanel.SetActive(true);
            _lockInButton.gameObject.SetActive(false);
            _gamePanel.SetActive(false);
            _statusText.text = "";
            _nameInput.text = PlayerPrefs.GetString(NamePrefKey, "");
            // Players always use the cloud server; the Server field is only for testing a local server
            // from the Editor when ticked (builds can still be pointed elsewhere with -server / ?server=).
            if (Application.isEditor && _showServerField)
            {
                _serverInput.text = PlayerPrefs.GetString(ServerPrefKey, NetworkManager.DefaultServer);
            }
            else
            {
                _serverInput.text = NetworkManager.DefaultServer;
                _serverInput.gameObject.SetActive(false);
                Transform label = _serverInput.transform.parent.Find("Server Label");
                if (label != null)
                    label.gameObject.SetActive(false);
            }

            // Lobby codes aren't case-sensitive; show them the way they're displayed in the lobby.
            _codeInput.onValidateInput += (text, index, c) => char.ToUpperInvariant(c);
            // Keep the board clear of the panel.
            _scratchpad.SidePanel = _joinPanel.transform.parent as RectTransform;

            _joinButton.onClick.AddListener(OnJoinClicked);
            _lockInButton.onClick.AddListener(OnWatchClicked);
            _showBestButton.onClick.AddListener(OnShowBestClicked);
            _doneButton.onClick.AddListener(OnDoneClicked);
            _leaveButton.onClick.AddListener(OnLeaveClicked);
            _startButton.onClick.AddListener(OnStartClicked);
            _pointsInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _firstTimerInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _extensionInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _roundLimitInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _minMovesInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _maxMovesInput.onEndEdit.AddListener(_ => OnSettingsEdited());
        }

        // A copy of the round limit row (label + number), placed after it; made at runtime so the scene
        // needn't change (placeholder until the UI pass).
        private GameObject CopyRoundLimitRow(string name, string label, int after)
        {
            GameObject source = _roundLimitInput.transform.parent.gameObject;
            GameObject row = Instantiate(source, source.transform.parent);
            row.name = name;
            row.transform.SetSiblingIndex(after + 1);
            foreach (Text text in row.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponentInParent<InputField>(true) == null)
                    text.text = label;
            }
            return row;
        }

        // "Board moves  [3] to [15]": the range of the dealt boards' shortest solution. Two narrower boxes so the
        // pair takes about the room of one.
        private void CreateBoardMovesRow()
        {
            const float boxWidth = 52f;
            int after = _roundLimitInput.transform.parent.GetSiblingIndex();
            GameObject row = CopyRoundLimitRow("Board Moves Row", "Board moves", after);
            _minMovesInput = row.GetComponentInChildren<InputField>(true);
            _minMovesInput.name = "Min Moves Input";
            _minMovesInput.onEndEdit = new InputField.EndEditEvent(); // no copied listeners
            SetPreferredWidth(_minMovesInput.gameObject, boxWidth);

            Text label = null;
            foreach (Text text in row.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponentInParent<InputField>(true) == null)
                    label = text;
            }
            GameObject to = Instantiate(label.gameObject, row.transform);
            to.name = "To";
            to.GetComponent<Text>().text = "to";
            to.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
            LayoutElement toLayout = to.GetComponent<LayoutElement>();
            if (toLayout != null)
            {
                toLayout.flexibleWidth = 0f;
                toLayout.preferredWidth = -1f;
                toLayout.minWidth = -1f;
            }
            to.transform.SetSiblingIndex(_minMovesInput.transform.GetSiblingIndex() + 1);

            _maxMovesInput = Instantiate(_minMovesInput.gameObject, row.transform).GetComponent<InputField>();
            _maxMovesInput.name = "Max Moves Input";
            _maxMovesInput.onEndEdit = new InputField.EndEditEvent();
            _maxMovesInput.transform.SetSiblingIndex(to.transform.GetSiblingIndex() + 1);
        }

        private static void SetPreferredWidth(GameObject widget, float width)
        {
            // Not ??: Unity's "missing component" objects only compare equal to null with ==.
            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null)
                layout = widget.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = width;
        }

        // A host setting row like the others, with an On/Off button instead of a number: a copy of the round
        // limit row, made at runtime so the scene needn't change (placeholder until the UI pass).
        private void CreateShowBestRow()
        {
            GameObject row = CopyRoundLimitRow("Show Best Row", "Show the best solution", _minMovesInput.transform.parent.GetSiblingIndex());

            InputField input = row.GetComponentInChildren<InputField>(true);
            GameObject button = Instantiate(_startButton.gameObject, row.transform);
            button.name = "Toggle Show Best";
            button.transform.SetSiblingIndex(input.transform.GetSiblingIndex());
            // Not ??: Unity's "missing component" objects only compare equal to null with ==.
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout == null)
                layout = button.AddComponent<LayoutElement>();
            var inputLayout = input.GetComponent<LayoutElement>();
            if (inputLayout != null)
            {
                layout.preferredWidth = inputLayout.preferredWidth;
                layout.preferredHeight = inputLayout.preferredHeight;
                layout.minWidth = inputLayout.minWidth;
                layout.minHeight = inputLayout.minHeight;
                layout.flexibleWidth = inputLayout.flexibleWidth;
            }
            DestroyImmediate(input.gameObject);
            _showBestButton = button.GetComponent<Button>();
            _showBestButton.onClick = new Button.ButtonClickedEvent(); // drop the copied Start listener
            _showBestLabel = button.GetComponentInChildren<Text>(true);
        }

        // Optional overrides, e.g. for quick testing or a link to a lobby. Desktop command line:
        //   Ricochet.exe -name Alice -lobby TEST -server http://localhost:5022
        // Browser: the page's query string, e.g. https://ricochet.thomasliu.dev/?lobby=TEST&name=Alice
        // Passing a lobby joins straight away.
        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string name = QueryValue(Application.absoluteURL, "name");
            string server = QueryValue(Application.absoluteURL, "server");
            string lobby = QueryValue(Application.absoluteURL, "lobby");
#else
            string[] args = System.Environment.GetCommandLineArgs();
            string name = ArgValue(args, "-name");
            string server = ArgValue(args, "-server");
            string lobby = ArgValue(args, "-lobby");
#endif

            if (name != null) _nameInput.text = name;
            if (server != null) _serverInput.text = server;
            if (lobby != null)
            {
                _codeInput.text = lobby.ToUpperInvariant();
                Join(save: false);
            }
        }

        private static string QueryValue(string url, string key)
        {
            int start = url.IndexOf('?');
            if (start < 0)
                return null;
            int end = url.IndexOf('#');
            string query = end > start ? url.Substring(start + 1, end - start - 1) : url.Substring(start + 1);
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && string.Equals(pair.Substring(0, eq), key, System.StringComparison.OrdinalIgnoreCase))
                {
                    string value = System.Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')).Trim();
                    return value.Length > 0 ? value : null;
                }
            }
            return null;
        }

        private static string ArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private void OnEnable()
        {
            _network.StatusChanged += OnStatusChanged;
            _network.Joined += OnJoined;
            _network.LobbyStateChanged += OnLobbyState;
            _network.RoundStarted += OnRoundStarted;
            _network.RoundCountdown += OnRoundCountdown;
            _network.TimerUpdated += OnTimerUpdated;
            _network.RoundTimeLimit += OnRoundTimeLimit;
            _network.NewBestSolution += OnNewBest;
            _network.MatchHistory += OnMatchHistory;
            _network.SolutionRejected += OnRejected;
            _network.RoundEnded += OnRoundEnded;
            _network.RequestFailed += OnRequestFailed;
            _scratchpad.Solved += OnBoardSolved;
        }

        private void OnDisable()
        {
            _network.StatusChanged -= OnStatusChanged;
            _network.Joined -= OnJoined;
            _network.LobbyStateChanged -= OnLobbyState;
            _network.RoundStarted -= OnRoundStarted;
            _network.RoundCountdown -= OnRoundCountdown;
            _network.TimerUpdated -= OnTimerUpdated;
            _network.RoundTimeLimit -= OnRoundTimeLimit;
            _network.NewBestSolution -= OnNewBest;
            _network.MatchHistory -= OnMatchHistory;
            _network.SolutionRejected -= OnRejected;
            _network.RoundEnded -= OnRoundEnded;
            _network.RequestFailed -= OnRequestFailed;
            _scratchpad.Solved -= OnBoardSolved;
        }

        // ---- Player actions ----

        private void OnJoinClicked() => Join(save: true);

        private void Join(bool save)
        {
            string code = _codeInput.text.Trim();
            if (code.Length == 0)
            {
                _statusText.text = "Enter a lobby code.";
                return;
            }
            // Command-line test joins don't overwrite what the player typed last time.
            if (save)
            {
                PlayerPrefs.SetString(NamePrefKey, _nameInput.text.Trim());
                if (Application.isEditor && _showServerField)
                    PlayerPrefs.SetString(ServerPrefKey, _serverInput.text.Trim());
            }
            _network.ServerUrl = _serverInput.text;
            _network.JoinLobby(_nameInput.text, code);
        }

        // Reaching the target sends the solution if it can take the lead; either way the board then goes
        // back to the start (a longer solution is a dead end: any continuation of it is longer still).
        private void OnBoardSolved(int moves)
        {
            if (!_match.InLobby || _match.Stage != MatchStage.Solving)
                return; // practice: just "Solved in N"
            Move[] solution = new List<Move>(Board.Moves).ToArray();
            if (_match.OnSolved(moves))
            {
                _network.SubmitSolution(solution);
            }
            else
            {
                _network.RecordSolution(solution); // still the player's own best for the match history
                _scratchpad.View.MarkNotShorter();
            }
            // Not once the round is over (this solve may have ended it): the solved board stays until the replays.
            _scratchpad.ResetAfter(SolvedResetDelay, () => _match.InLobby && _match.Stage == MatchStage.Solving);
        }

        private void OnLeaveClicked()
        {
            ClearButtonFocus();
            _network.LeaveLobby();
        }

        private void OnStartClicked()
        {
            ClearButtonFocus();
            _network.StartMatch();
            _match.OnStartRequested();
        }

        // Toggles this player's vote to end the round early (or skip the replay); the server acts once
        // everyone has voted.
        // Plays the current best (shown by the lobby) on this player's board, then puts their attempt back.
        private void OnWatchClicked()
        {
            ClearButtonFocus();
            if (_match.CanWatchBest)
                _scratchpad.WatchSolution(new ReplaySegment($"{_match.BestPlayer}'s best", _match.BestSolution, 0.6f));
        }

        private void OnShowBestClicked()
        {
            ClearButtonFocus();
            MatchSettings settings = _match.Settings;
            if (!_match.IAmHost || settings == null)
                return;
            MatchSettings edited = Copy(settings);
            edited.ShowBestSolution = !settings.ShowBestSolution;
            _network.UpdateSettings(edited);
        }

        private static MatchSettings Copy(MatchSettings settings) => new MatchSettings
        {
            Mode = settings.Mode,
            PointsToWin = settings.PointsToWin,
            FirstTimerSeconds = settings.FirstTimerSeconds,
            ExtensionSeconds = settings.ExtensionSeconds,
            RoundLimitSeconds = settings.RoundLimitSeconds,
            ShowBestSolution = settings.ShowBestSolution,
            MinMoves = settings.MinMoves,
            MaxMoves = settings.MaxMoves,
        };

        private void OnDoneClicked()
        {
            ClearButtonFocus();
            bool done = _match.ToggleDone();
            if (done)
                _scratchpad.View.PlaySkip();
            _network.SetDone(done);
        }

        // Sends all the settings when the host finishes editing any of them. The server clamps them
        // and echoes them back in the next LobbyState, which rewrites the fields (see ShowSettings).
        private void OnSettingsEdited()
        {
            MatchSettings settings = _match.Settings;
            if (!_match.IAmHost || settings == null)
                return;

            MatchSettings edited = Copy(settings);
            edited.PointsToWin = ParseOr(_pointsInput.text, settings.PointsToWin);
            edited.FirstTimerSeconds = ParseOr(_firstTimerInput.text, settings.FirstTimerSeconds);
            edited.ExtensionSeconds = ParseOr(_extensionInput.text, settings.ExtensionSeconds);
            // Entered in minutes; 0 = no limit.
            edited.RoundLimitSeconds = ParseOr(_roundLimitInput.text, settings.RoundLimitSeconds / 60) * 60;
            edited.MinMoves = ParseOr(_minMovesInput.text, settings.MinMoves);
            edited.MaxMoves = ParseOr(_maxMovesInput.text, settings.MaxMoves);
            // A minimum above the maximum pulls the maximum up with it, and a maximum below the minimum the other way.
            if (edited.MinMoves > edited.MaxMoves)
            {
                if (edited.MinMoves != settings.MinMoves)
                    edited.MaxMoves = edited.MinMoves;
                else
                    edited.MinMoves = edited.MaxMoves;
            }
            if (edited.PointsToWin == settings.PointsToWin && edited.FirstTimerSeconds == settings.FirstTimerSeconds
                && edited.ExtensionSeconds == settings.ExtensionSeconds && edited.RoundLimitSeconds == settings.RoundLimitSeconds
                && edited.MinMoves == settings.MinMoves && edited.MaxMoves == settings.MaxMoves)
            {
                ShowSettings(settings); // undo anything unparseable
                return;
            }
            _network.UpdateSettings(edited);
        }

        private static int ParseOr(string text, int fallback) => int.TryParse(text, out int value) ? value : fallback;

        // A clicked button stays selected, and the UI treats Enter as "press the selected button".
        // Deselect so Enter never clicks a button again by accident.
        private static void ClearButtonFocus()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        // ---- Server messages: update the state, keep the board in step ----

        private void OnStatusChanged(string status)
        {
            _statusText.text = status;
            // Left the lobby or lost the connection: back to the join screen with an offline practice board.
            if (_match.OnConnectionStatus(status, _network.IsInLobby, _network.IsReconnecting))
                _scratchpad.PlayOffline();
        }

        // The lobby usually arrives before the join's answer, so only now do we know if we're the host.
        private void OnJoined(string myName)
        {
            _match.OnJoined(myName);
            ShowSettings(_match.Settings);
        }

        private void OnLobbyState(LobbyState state)
        {
            LobbyChange change = _match.OnLobbyState(state, _network.PlayerName);
            ShowSettings(state.Settings);
            switch (change)
            {
                case LobbyChange.BetweenMatches:
                    // Free practice until the next Start (keeps a practice board the player is on).
                    if (!Board.IsPractice)
                        _scratchpad.PlayOffline();
                    break;
                case LobbyChange.RoundOver:
                    _scratchpad.EndRound();
                    break;
            }
        }

        // The next board is dealt in a few seconds: after Start, or after the previous round's replays.
        private void OnRoundCountdown(float seconds)
        {
            _match.OnCountdown(seconds, Time.time);
            _scratchpad.ShowCountdown(seconds, $"Round {_match.MatchRound + 1} starts in");
        }

        private void OnRoundStarted(int round, RoundSetup setup)
        {
            _match.OnRoundStarted(round);
            _scratchpad.LoadRound(setup);
        }

        private void OnTimerUpdated(int round, float secondsRemaining) =>
            _match.OnTimerUpdated(round, secondsRemaining, Time.time);

        private void OnRoundTimeLimit(int round, float secondsRemaining) =>
            _match.OnRoundTimeLimit(round, secondsRemaining, Time.time);

        private void OnMatchHistory(RoundRecord[] rounds) => _match.OnMatchHistory(rounds);

        private void OnNewBest(int round, string player, int moves, Move[] shown)
        {
            if (player != _match.MyName && round == _network.CurrentRound)
                _scratchpad.View.PlayOpponentSolved();
            int onBoard = Board.IsSolved && !Board.IsReplaying ? Board.Moves.Count : -1;
            if (_match.OnNewBest(round, player, moves, onBoard, shown))
                _scratchpad.ResetToStart();
        }

        private void OnRejected(string reason) => _match.OnRejected(reason);

        private void OnRequestFailed(string message) => _match.OnRequestFailed(message, _network.IsReconnecting);

        // The server waits for these replays before dealing the next board.
        private void OnRoundEnded(RoundResult result, Move[] winnerMoves, Move[] shortest)
        {
            IReadOnlyList<ReplaySegment> replays = _match.OnRoundEnded(result, winnerMoves, shortest);
            if (replays == null)
                return;
            if (result.WinnerName == null && !result.OutOfTime)
                _scratchpad.View.PlaySkip(); // everyone skipped the board
            if (replays.Count > 0)
                StartCoroutine(PlayReplaysAfter((float)result.SolvedPauseSeconds, replays, (float)result.ReplayPauseSeconds));
            else
                _scratchpad.EndRound();
        }

        // A solution that ended the round at once gets a moment for its slide and burst before the replays.
        private IEnumerator PlayReplaysAfter(float delay, IReadOnlyList<ReplaySegment> replays, float pause)
        {
            if (delay > 0f)
            {
                int round = _match.Round;
                yield return new WaitForSeconds(delay);
                if (!_match.InLobby || _match.Round != round || _match.Stage != MatchStage.RoundOver)
                    yield break; // left, or moved on meanwhile
            }
            _scratchpad.PlayReplays(replays, pause);
        }

        // ---- Showing the state ----

        private void Update()
        {
            _joinPanel.SetActive(!_match.InLobby);
            _gamePanel.SetActive(_match.InLobby);
            if (!_match.InLobby)
                return;

            _feedbackText.text = _match.Feedback;
            _lobbyText.text = "Lobby " + _match.LobbyCode;
            ShowPlayers();

            // Between matches: setup / results (settings + Start for the host). During one: the round.
            MatchStage stage = _match.Stage;
            bool playing = stage != MatchStage.BetweenMatches;
            _matchText.gameObject.SetActive(!playing);
            _settingsGroup.SetActive(!playing && _match.IAmHost);
            _startButton.gameObject.SetActive(!playing && _match.IAmHost);
            _roundText.gameObject.SetActive(playing);
            _doneButton.gameObject.SetActive(_match.DoneAction != DoneAction.None);
            _lockInButton.gameObject.SetActive(_match.CanWatchBest);
            if (_match.CanWatchBest)
            {
                _lockInButton.interactable = !Board.IsReplaying;
                _watchLabel.text = $"Watch {_match.BestPlayer}'s {_match.BestMoves}";
            }

            if (!playing)
            {
                ShowMatchText();
                return;
            }
            _roundText.text = RoundText(stage);
            _doneLabel.text = DoneLabel();
        }

        private void ShowPlayers()
        {
            _playersHeader.text = _match.Phase == MatchPhase.Finished
                ? "Final standings"
                : $"Players — first to {_match.Settings.PointsToWin} wins";
            var players = new StringBuilder();
            foreach (PlayerInfo player in _match.Standings)
                players.AppendLine($"{player.Name}  —  {player.Score}"
                    + (player.Name == _match.Host ? "  (host)" : "")
                    + (!player.Connected ? "   (reconnecting...)"
                        : player.Leading ? "   (best)" : player.Done ? "   (done)" : ""));
            _playersText.text = players.ToString();
        }

        // From each LobbyState (not every frame, so a field the host just left keeps the new value
        // until the server echoes it back).
        private void ShowSettings(MatchSettings settings)
        {
            // Don't overwrite a field the host is typing in.
            if (!_pointsInput.isFocused) _pointsInput.text = settings.PointsToWin.ToString();
            if (!_firstTimerInput.isFocused) _firstTimerInput.text = settings.FirstTimerSeconds.ToString();
            if (!_extensionInput.isFocused) _extensionInput.text = settings.ExtensionSeconds.ToString();
            if (!_roundLimitInput.isFocused) _roundLimitInput.text = (settings.RoundLimitSeconds / 60).ToString();
            if (!_minMovesInput.isFocused) _minMovesInput.text = settings.MinMoves.ToString();
            if (!_maxMovesInput.isFocused) _maxMovesInput.text = settings.MaxMoves.ToString();
            _showBestLabel.text = settings.ShowBestSolution ? "On" : "Off";
        }

        private string RoundText(MatchStage stage)
        {
            float now = Time.time;
            int next = _match.MatchRound + 1;
            switch (stage)
            {
                case MatchStage.Dealing:
                    return "Dealing the first board...";
                case MatchStage.Countdown:
                    float left = _match.CountdownLeft(now);
                    return left > 0f ? $"Round {next} starts in {Mathf.CeilToInt(left)}..." : $"Round {next} — dealing...";
            }

            var text = new StringBuilder($"Round {_match.MatchRound}\n");
            ReplaySegment replay = Board.CurrentReplay;
            if (stage == MatchStage.RoundOver && replay != null)
            {
                int step = Board.ReplayStep;
                IReadOnlyList<Move> moves = replay.Moves;
                text.Append(step == 0 ? $"{replay.Label}:"
                    : $"{replay.Label} — step {step}/{moves.Count}: {moves[step - 1].Robot} {moves[step - 1].Direction}");
            }
            else if (stage == MatchStage.RoundOver)
                text.Append("Round over");
            else if (_match.HasTimeLimit)
                text.Append($"Time limit: {Clock(_match.TimeLimitLeft(now))} — the timer starts on the first solution");
            else if (!_match.TimerRunning)
                text.Append("No solution yet — timer starts on the first one");
            else
            {
                text.Append($"Time left: {_match.TimeLeft(now):0}s");
                if (_match.TimeJustAdded(now))
                    text.Append("  (+time!)");
            }
            if (_match.BestPlayer != null && stage == MatchStage.Solving)
                text.Append(_match.BestPlayer == _match.MyName
                    ? $"\nBest: you, {_match.BestMoves} moves"
                    : $"\nBest: {_match.BestPlayer}, {_match.BestMoves} moves. Beat it!");
            return text.ToString();
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        private string DoneLabel()
        {
            if (_match.IAmDone)
                return $"Waiting for others ({_match.DoneCount}/{_match.VoterCount}) — undo";
            switch (_match.DoneAction)
            {
                case DoneAction.SkipReplay: return "Skip replay";
                case DoneAction.ImDone: return "I'm done";
                default: return "Skip board";
            }
        }

        private void ShowMatchText()
        {
            MatchSettings settings = _match.Settings;
            var text = new StringBuilder();
            if (_match.Phase == MatchPhase.Finished)
                text.AppendLine(_match.MatchWinner == _match.MyName ? "You win the match!" : $"{_match.MatchWinner} wins the match!");
            if (_match.IAmHost)
            {
                text.Append(_match.Phase == MatchPhase.Finished
                    ? "Change the settings or play again."
                    : "You're the host: pick the settings, then Start when everyone's in.");
                _startLabel.text = _match.Phase == MatchPhase.Finished ? "Play again" : "Start match";
            }
            else
            {
                text.AppendLine(_match.Phase == MatchPhase.Finished
                    ? $"Waiting for {_match.Host} to start a new match."
                    : $"Waiting for {_match.Host} to start the match.");
                // Kept to about three lines: the feedback text sits right under it.
                text.Append($"First to {settings.PointsToWin}, {settings.FirstTimerSeconds}s timer (+{settings.ExtensionSeconds}s), "
                    + (settings.RoundLimitSeconds > 0 ? $"{settings.RoundLimitSeconds / 60} min limit" : "no limit")
                    + (settings.MinMoves == settings.MaxMoves ? $", {settings.MinMoves}-move boards"
                        : settings.MinMoves != MatchSettings.DefaultMinMoves || settings.MaxMoves != MatchSettings.DefaultMaxMoves
                            ? $", boards {settings.MinMoves}-{settings.MaxMoves} moves" : "")
                    + (settings.ShowBestSolution ? ", best solution shown" : ""));
            }
            _matchText.text = text.ToString();
        }
    }
}
