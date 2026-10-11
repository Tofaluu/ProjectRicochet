using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using SharedLogic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// Join panel (name, lobby code) and in-game panel (players, timer, best solution, Lock In).
    /// Glue between <see cref="NetworkManager"/> and the <see cref="ScratchpadController"/> board.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private ScratchpadController _scratchpad;

        [Header("Join panel")]
        [SerializeField] private GameObject _joinPanel;
        [SerializeField] private InputField _nameInput;
        [SerializeField] private InputField _codeInput;
        [SerializeField] private InputField _serverInput;
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

        private const string NamePrefKey = "Ricochet.PlayerName";
        private const string ServerPrefKey = "Ricochet.Server";

        private int _round;
        private bool _roundOver;
        private float _timerEnd = -1f;
        private float _timerExtendedUntil = -1f;
        private string _bestPlayer;
        private int _bestMoves;
        // From the latest LobbyState: this player's vote/lead, and the vote among non-leaders.
        private bool _iAmDone;
        private bool _iAmLeading;
        private int _doneCount;
        private int _voterCount;
        private HashSet<string> _playerNames = new HashSet<string>();

        private void Awake()
        {
            _joinPanel.SetActive(true);
            _gamePanel.SetActive(false);
            _statusText.text = "";
            _nameInput.text = PlayerPrefs.GetString(NamePrefKey, "");
            _serverInput.text = PlayerPrefs.GetString(ServerPrefKey, NetworkManager.DefaultServer);

            _joinButton.onClick.AddListener(OnJoinClicked);
            _lockInButton.onClick.AddListener(OnLockInClicked);
            _doneButton.onClick.AddListener(OnDoneClicked);
            _leaveButton.onClick.AddListener(OnLeaveClicked);
        }

        // Optional command-line overrides for quick testing, e.g.
        //   Ricochet.exe -name Alice -lobby TEST -server http://localhost:5022
        // Passing -lobby joins straight away.
        private void Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            string name = ArgValue(args, "-name");
            string server = ArgValue(args, "-server");
            string lobby = ArgValue(args, "-lobby");

            if (name != null) _nameInput.text = name;
            if (server != null) _serverInput.text = server;
            if (lobby != null)
            {
                _codeInput.text = lobby;
                Join(save: false);
            }
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
            _network.LobbyStateChanged += OnLobbyState;
            _network.RoundStarted += OnRoundStarted;
            _network.TimerUpdated += OnTimerUpdated;
            _network.NewBestSolution += OnNewBest;
            _network.SolutionRejected += OnRejected;
            _network.RoundEnded += OnRoundEnded;
        }

        private void OnDisable()
        {
            _network.StatusChanged -= OnStatusChanged;
            _network.LobbyStateChanged -= OnLobbyState;
            _network.RoundStarted -= OnRoundStarted;
            _network.TimerUpdated -= OnTimerUpdated;
            _network.NewBestSolution -= OnNewBest;
            _network.SolutionRejected -= OnRejected;
            _network.RoundEnded -= OnRoundEnded;
        }

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
                PlayerPrefs.SetString(ServerPrefKey, _serverInput.text.Trim());
            }
            _network.ServerUrl = _serverInput.text;
            _network.JoinLobby(_nameInput.text, code);
        }

        private void OnLockInClicked()
        {
            ClearButtonFocus();
            int moveCount = _scratchpad.Moves.Count;
            // Only strictly shorter solutions can take the lead; say so instead of sending a sure rejection.
            if (_bestPlayer != null && moveCount >= _bestMoves)
            {
                _feedbackText.text = _bestPlayer == _network.PlayerName
                    ? $"You already lead with {_bestMoves} moves."
                    : $"Your {moveCount} moves don't beat {_bestPlayer}'s {_bestMoves}. Find a shorter one!";
                return;
            }
            _network.SubmitSolution(_scratchpad.Moves.ToArray());
            _feedbackText.text = $"Submitted {moveCount} moves...";
        }

        private void OnLeaveClicked()
        {
            ClearButtonFocus();
            _network.LeaveLobby();
        }

        // Toggles this player's vote to end the round early; the server ends it once everyone has voted.
        private void OnDoneClicked()
        {
            ClearButtonFocus();
            // Flip locally for instant feedback; the next LobbyState confirms it.
            _iAmDone = !_iAmDone;
            _network.SetDone(_iAmDone);
        }

        // A clicked button stays selected, and the UI treats Enter as "press the selected button".
        // Deselect so Enter only ever means Lock In (see Update), never a second click.
        private static void ClearButtonFocus()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void OnStatusChanged(string status)
        {
            _statusText.text = status;
            // Left the lobby or lost the connection: back to the join screen with an offline practice board.
            if (!_network.IsInLobby && _gamePanel.activeSelf)
            {
                _joinPanel.SetActive(true);
                _gamePanel.SetActive(false);
                _scratchpad.PlayOffline();
            }
        }

        private void OnLobbyState(LobbyState state)
        {
            _joinPanel.SetActive(false);
            _gamePanel.SetActive(true);
            _lobbyText.text = "Lobby " + state.Code;

            var players = new StringBuilder();
            foreach (PlayerInfo player in state.Players.OrderByDescending(p => p.Score))
                players.AppendLine($"{player.Name}  —  {player.Score}"
                    + (player.Leading ? "   (best)" : player.Done ? "   (done)" : ""));
            _playersText.text = players.ToString();

            PlayerInfo me = state.Players.FirstOrDefault(p => p.Name == _network.PlayerName);
            _iAmDone = me != null && me.Done;
            _iAmLeading = me != null && me.Leading;
            PlayerInfo[] voters = state.Players.Where(p => !p.Leading).ToArray();
            _doneCount = voters.Count(p => p.Done);
            _voterCount = voters.Length;
            _playerNames = new HashSet<string>(state.Players.Select(p => p.Name));
        }

        private void OnRoundStarted(int round, RoundSetup setup)
        {
            _round = round;
            _roundOver = false;
            _timerEnd = -1f;
            _bestPlayer = null;
            _iAmDone = false;
            _iAmLeading = false;
            _feedbackText.text = "Find the shortest solution, then Lock In!";
            _scratchpad.AllowNewBoard = false;
            _scratchpad.LoadRound(setup);
        }

        private void OnTimerUpdated(int round, float secondsRemaining)
        {
            if (round != _round)
                return;
            // A later update means a better solution bought everyone more time.
            if (_timerEnd >= 0f)
                _timerExtendedUntil = Time.time + 2f;
            _timerEnd = Time.time + secondsRemaining;
        }

        private void OnNewBest(int round, string player, int moves)
        {
            if (round != _round)
                return;
            _bestPlayer = player;
            _bestMoves = moves;
            if (player == _network.PlayerName)
            {
                _feedbackText.text = $"Accepted! You lead with {moves} moves.";
                return;
            }

            // Someone beat (or matched) the solution still sitting on this board: put the robots back so
            // the player can go for a shorter one. Boards mid-attempt, or holding an even shorter
            // solution, are left alone.
            if (_scratchpad.IsSolved && !_scratchpad.IsReplaying && _scratchpad.Moves.Count >= moves)
            {
                _scratchpad.ResetToStart();
                _feedbackText.text = $"{player} found {moves} moves — your board was reset, try to beat it!";
            }
        }

        private void OnRejected(string reason)
        {
            _feedbackText.text = "Rejected: " + reason;
        }

        // Replays the winner's solution, then the shortest one if it's shorter (or only the shortest when
        // nobody solved it). The server waits for these before dealing the next board.
        private void OnRoundEnded(RoundResult result, Move[] winnerMoves, Move[] shortest)
        {
            if (result.RoundNumber != _round)
                return;
            _roundOver = true;
            _timerEnd = -1f;

            string winner = result.WinnerName;
            var replays = new List<ReplaySegment>();
            if (winnerMoves.Length > 0)
            {
                bool wasShortest = shortest != null && winnerMoves.Length <= shortest.Length;
                // The best solver only scores if they're still here.
                if (!_playerNames.Contains(winner))
                    _feedbackText.text = $"{winner} had the best solution ({winnerMoves.Length} moves) but left, so no point this round.";
                else if (wasShortest)
                    _feedbackText.text = $"{winner} found the shortest possible solution! ({winnerMoves.Length} moves)";
                else if (shortest != null)
                    _feedbackText.text = $"{winner} wins with {winnerMoves.Length} moves. Shortest possible: {shortest.Length}.";
                else
                    _feedbackText.text = $"{winner} wins the round with {winnerMoves.Length} moves!";

                replays.Add(new ReplaySegment($"{winner}'s solution", winnerMoves, (float)result.ReplayStepSeconds));
                if (shortest != null && !wasShortest)
                    replays.Add(new ReplaySegment("Shortest possible", shortest, (float)result.ShortestReplayStepSeconds));
            }
            else if (shortest != null)
            {
                _feedbackText.text = $"Board skipped. Here's how it could be done in {shortest.Length} moves:";
                replays.Add(new ReplaySegment("Shortest possible", shortest, (float)result.ShortestReplayStepSeconds));
            }
            else
            {
                _feedbackText.text = "Board skipped. Next board soon...";
            }

            if (replays.Count > 0)
                _scratchpad.PlayReplays(replays, (float)result.ReplayPauseSeconds);
        }

        private void Update()
        {
            if (!_gamePanel.activeSelf)
                return;

            var text = new StringBuilder($"Round {_round}\n");
            ReplaySegment replay = _scratchpad.CurrentReplay;
            if (_roundOver && _scratchpad.IsReplaying && replay != null)
            {
                int step = _scratchpad.ReplayStep;
                IReadOnlyList<Move> moves = replay.Moves;
                text.Append(step == 0 ? $"{replay.Label}:"
                    : $"{replay.Label} — step {step}/{moves.Count}: {moves[step - 1].Robot} {moves[step - 1].Direction}");
            }
            else if (_roundOver)
                text.Append("Round over");
            else if (_timerEnd < 0f)
                text.Append("No solution yet — timer starts on the first one");
            else
            {
                text.Append($"Time left: {Mathf.Max(0f, _timerEnd - Time.time):0}s");
                if (Time.time < _timerExtendedUntil)
                    text.Append("  (+time!)");
            }
            if (_bestPlayer != null && !_roundOver)
                text.Append($"\nBest: {_bestPlayer} ({_bestMoves} moves)");
            _roundText.text = text.ToString();

            int moveCount = _scratchpad.Moves.Count;
            // Enabled for any solution; OnLockInClicked explains when it doesn't beat the best.
            _lockInButton.interactable = !_roundOver && _scratchpad.IsSolved;
            _lockInLabel.text = _scratchpad.IsSolved ? $"Lock In ({moveCount} moves) [Enter]" : "Lock In";

            Keyboard keyboard = Keyboard.current;
            if (_lockInButton.interactable && keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                OnLockInClicked();

            // The leader (or, after the round, the winner) isn't part of the vote, so they don't get the
            // button. During a round it ends the round early; after it, it skips the replay.
            _doneButton.gameObject.SetActive(!_iAmLeading);
            if (_iAmDone)
                _doneLabel.text = $"Waiting for others ({_doneCount}/{_voterCount}) — undo";
            else if (_roundOver)
                _doneLabel.text = "Skip replay";
            else
                _doneLabel.text = _timerEnd < 0f ? "Skip board" : "I'm done";
        }
    }
}
