using System.Collections.Generic;
using System.Text;
using Ricochet.Client.Game;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// Join panel (name, lobby code) and in-game panel: players, then either the match setup / results
    /// (host settings + Start) between matches, or the round (timer, best solution, Lock In) during one.
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
        [SerializeField] private Button _lockInButton;
        [SerializeField] private Text _lockInLabel;
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

        private const string NamePrefKey = "Ricochet.PlayerName";
        // Renamed when the cloud server came in, so tunnel addresses saved by playtest builds are dropped.
        private const string ServerPrefKey = "Ricochet.ServerAddress";

        private readonly MatchState _match = new MatchState();

        private BoardSession Board => _scratchpad.Session;

        private void Awake()
        {
            UiSkin.Apply(transform, GameTheme.OrDefault(_theme));
            _joinPanel.SetActive(true);
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

            _joinButton.onClick.AddListener(OnJoinClicked);
            _lockInButton.onClick.AddListener(OnLockInClicked);
            _doneButton.onClick.AddListener(OnDoneClicked);
            _leaveButton.onClick.AddListener(OnLeaveClicked);
            _startButton.onClick.AddListener(OnStartClicked);
            _pointsInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _firstTimerInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _extensionInput.onEndEdit.AddListener(_ => OnSettingsEdited());
            _roundLimitInput.onEndEdit.AddListener(_ => OnSettingsEdited());
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
                _codeInput.text = lobby;
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
            _network.SolutionRejected += OnRejected;
            _network.RoundEnded += OnRoundEnded;
            _network.RequestFailed += OnRequestFailed;
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
            _network.SolutionRejected -= OnRejected;
            _network.RoundEnded -= OnRoundEnded;
            _network.RequestFailed -= OnRequestFailed;
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

        private void OnLockInClicked()
        {
            ClearButtonFocus();
            if (_match.TryLockIn(Board.Moves.Count))
                _network.SubmitSolution(new List<Move>(Board.Moves).ToArray());
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
        private void OnDoneClicked()
        {
            ClearButtonFocus();
            _network.SetDone(_match.ToggleDone());
        }

        // Sends all the settings when the host finishes editing any of them. The server clamps them
        // and echoes them back in the next LobbyState, which rewrites the fields (see ShowSettings).
        private void OnSettingsEdited()
        {
            MatchSettings settings = _match.Settings;
            if (!_match.IAmHost || settings == null)
                return;

            var edited = new MatchSettings
            {
                Mode = settings.Mode,
                PointsToWin = ParseOr(_pointsInput.text, settings.PointsToWin),
                FirstTimerSeconds = ParseOr(_firstTimerInput.text, settings.FirstTimerSeconds),
                ExtensionSeconds = ParseOr(_extensionInput.text, settings.ExtensionSeconds),
                // Entered in minutes; 0 = no limit.
                RoundLimitSeconds = ParseOr(_roundLimitInput.text, settings.RoundLimitSeconds / 60) * 60,
            };
            if (edited.PointsToWin == settings.PointsToWin && edited.FirstTimerSeconds == settings.FirstTimerSeconds
                && edited.ExtensionSeconds == settings.ExtensionSeconds && edited.RoundLimitSeconds == settings.RoundLimitSeconds)
            {
                ShowSettings(settings); // undo anything unparseable
                return;
            }
            _network.UpdateSettings(edited);
        }

        private static int ParseOr(string text, int fallback) => int.TryParse(text, out int value) ? value : fallback;

        // A clicked button stays selected, and the UI treats Enter as "press the selected button".
        // Deselect so Enter only ever means Lock In (see Update), never a second click.
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

        private void OnNewBest(int round, string player, int moves)
        {
            int onBoard = Board.IsSolved && !Board.IsReplaying ? Board.Moves.Count : -1;
            if (_match.OnNewBest(round, player, moves, onBoard))
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
            if (replays.Count > 0)
                _scratchpad.PlayReplays(replays, (float)result.ReplayPauseSeconds);
            else
                _scratchpad.EndRound();
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
            _lockInButton.gameObject.SetActive(playing);
            _doneButton.gameObject.SetActive(_match.DoneAction != DoneAction.None);

            if (!playing)
            {
                ShowMatchText();
                return;
            }
            _roundText.text = RoundText(stage);
            _doneLabel.text = DoneLabel();

            // Enabled for any solution; MatchState.TryLockIn explains when it doesn't beat the best.
            _lockInButton.interactable = _match.CanLockIn(Board.IsSolved);
            _lockInLabel.text = _lockInButton.interactable ? $"Lock In ({Board.Moves.Count} moves) [Enter]" : "Lock In";
            Keyboard keyboard = Keyboard.current;
            if (_lockInButton.interactable && keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                OnLockInClicked();
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
                text.Append($"\nBest: {_match.BestPlayer} ({_match.BestMoves} moves)");
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
                text.Append($"Classic: first to {settings.PointsToWin} points, "
                    + $"{settings.FirstTimerSeconds}s timer, +{settings.ExtensionSeconds}s for better solutions, "
                    + (settings.RoundLimitSeconds > 0 ? $"{settings.RoundLimitSeconds / 60} min to find a first solution" : "no round limit"));
            }
            _matchText.text = text.ToString();
        }
    }
}
