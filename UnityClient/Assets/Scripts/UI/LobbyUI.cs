using System.Linq;
using System.Text;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using SharedLogic;
using UnityEngine;
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

        private const string NamePrefKey = "Ricochet.PlayerName";

        private int _round;
        private bool _roundOver;
        private float _timerEnd = -1f;
        private string _bestPlayer;
        private int _bestMoves;
        private int _submittedMoveCount;

        private void Awake()
        {
            _joinPanel.SetActive(true);
            _gamePanel.SetActive(false);
            _statusText.text = "";
            _nameInput.text = PlayerPrefs.GetString(NamePrefKey, "");

            _joinButton.onClick.AddListener(OnJoinClicked);
            _lockInButton.onClick.AddListener(OnLockInClicked);
        }

        private void OnEnable()
        {
            _network.StatusChanged += OnStatusChanged;
            _network.LobbyStateChanged += OnLobbyState;
            _network.RoundStarted += OnRoundStarted;
            _network.TimerStarted += OnTimerStarted;
            _network.NewBestSolution += OnNewBest;
            _network.SolutionRejected += OnRejected;
            _network.RoundEnded += OnRoundEnded;
        }

        private void OnDisable()
        {
            _network.StatusChanged -= OnStatusChanged;
            _network.LobbyStateChanged -= OnLobbyState;
            _network.RoundStarted -= OnRoundStarted;
            _network.TimerStarted -= OnTimerStarted;
            _network.NewBestSolution -= OnNewBest;
            _network.SolutionRejected -= OnRejected;
            _network.RoundEnded -= OnRoundEnded;
        }

        private void OnJoinClicked()
        {
            string code = _codeInput.text.Trim();
            if (code.Length == 0)
            {
                _statusText.text = "Enter a lobby code.";
                return;
            }
            PlayerPrefs.SetString(NamePrefKey, _nameInput.text.Trim());
            _network.JoinLobby(_nameInput.text, code);
        }

        private void OnLockInClicked()
        {
            _submittedMoveCount = _scratchpad.Moves.Count;
            _network.SubmitSolution(_scratchpad.Moves.ToArray());
            _feedbackText.text = $"Submitted {_submittedMoveCount} moves...";
        }

        private void OnStatusChanged(string status)
        {
            _statusText.text = status;
            if (!_network.IsInLobby)
            {
                // Lost the connection: back to the join screen.
                _joinPanel.SetActive(true);
                _gamePanel.SetActive(false);
                _scratchpad.AllowNewBoard = true;
            }
        }

        private void OnLobbyState(LobbyState state)
        {
            _joinPanel.SetActive(false);
            _gamePanel.SetActive(true);
            _lobbyText.text = "Lobby " + state.Code;

            var players = new StringBuilder();
            foreach (PlayerInfo player in state.Players.OrderByDescending(p => p.Score))
                players.AppendLine($"{player.Name}  —  {player.Score}");
            _playersText.text = players.ToString();
        }

        private void OnRoundStarted(int round, RoundSetup setup)
        {
            _round = round;
            _roundOver = false;
            _timerEnd = -1f;
            _bestPlayer = null;
            _submittedMoveCount = 0;
            _feedbackText.text = "Find the shortest solution, then Lock In!";
            _scratchpad.AllowNewBoard = false;
            _scratchpad.LoadRound(setup);
        }

        private void OnTimerStarted(int round, float secondsRemaining)
        {
            if (round == _round)
                _timerEnd = Time.time + secondsRemaining;
        }

        private void OnNewBest(int round, string player, int moves)
        {
            if (round != _round)
                return;
            _bestPlayer = player;
            _bestMoves = moves;
            if (_submittedMoveCount == moves)
                _feedbackText.text = $"Accepted! You lead with {moves} moves.";
        }

        private void OnRejected(string reason)
        {
            _feedbackText.text = "Rejected: " + reason;
        }

        private void OnRoundEnded(int round, string winner, int moves)
        {
            if (round != _round)
                return;
            _roundOver = true;
            _timerEnd = -1f;
            _feedbackText.text = winner != null
                ? $"{winner} wins the round with {moves} moves! Next board soon..."
                : "Round over. Next board soon...";
        }

        private void Update()
        {
            if (!_gamePanel.activeSelf)
                return;

            var text = new StringBuilder($"Round {_round}\n");
            if (_roundOver)
                text.Append("Round over");
            else if (_timerEnd < 0f)
                text.Append("No solution yet — timer starts on the first one");
            else
                text.Append($"Time left: {Mathf.Max(0f, _timerEnd - Time.time):0}s");
            if (_bestPlayer != null)
                text.Append($"\nBest: {_bestPlayer} ({_bestMoves} moves)");
            _roundText.text = text.ToString();

            int moveCount = _scratchpad.Moves.Count;
            bool beatsBest = _bestPlayer == null || moveCount < _bestMoves;
            _lockInButton.interactable = !_roundOver && _scratchpad.IsSolved && beatsBest;
            _lockInLabel.text = _scratchpad.IsSolved ? $"Lock In ({moveCount} moves)" : "Lock In";
        }
    }
}
