using System.Text.RegularExpressions;
using BackendServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SharedLogic;

namespace BackendServer.Services
{
    public class GameOptions
    {
        /// <summary>How long other players get after the first valid solution.</summary>
        public double TimerSeconds { get; set; } = 45;

        /// <summary>Pause between a round ending and the next board.</summary>
        public double IntermissionSeconds { get; set; } = 3;

        public int MaxSolutionMoves { get; set; } = 64;
    }

    public enum RoundPhase
    {
        /// <summary>Nobody has found a solution yet; no timer.</summary>
        Solving,
        /// <summary>Someone solved it; better solutions are accepted until the timer runs out.</summary>
        TimerRunning,
        /// <summary>Round over; waiting for the next board.</summary>
        Intermission,
    }

    public class Player
    {
        public string Name { get; init; } = "";
        public int Score { get; set; }
    }

    public class Lobby
    {
        public string Code { get; init; } = "";
        public Dictionary<string, Player> Players { get; } = new();
        public bool Closed { get; set; }

        public int RoundNumber { get; set; }
        public RoundSetup Round { get; set; } = null!;
        public byte[] Board { get; set; } = Array.Empty<byte>();
        public RoundPhase Phase { get; set; }
        public DateTime TimerEndUtc { get; set; }

        public string? BestConnectionId { get; set; }
        public string? BestPlayerName { get; set; }
        public int BestMoveCount { get; set; }
    }

    /// <summary>
    /// Owns all lobbies and runs the round loop. All state is guarded by one lock; messages
    /// are sent through <see cref="IHubContext{THub}"/> after releasing it, because hub
    /// instances are disposed as soon as each hub call returns.
    /// </summary>
    public class LobbyManager
    {
        private static readonly Regex LobbyCodePattern = new("^[A-Z0-9]{1,8}$");
        private const int MaxNameLength = 20;

        private readonly object _gate = new();
        private readonly Dictionary<string, Lobby> _lobbies = new();
        private readonly Dictionary<string, Lobby> _lobbyByConnection = new();
        private readonly Random _random = new();

        private readonly IHubContext<GameHub> _hub;
        private readonly GameOptions _options;
        private readonly ILogger<LobbyManager> _log;

        public LobbyManager(IHubContext<GameHub> hub, IOptions<GameOptions> options, ILogger<LobbyManager> log)
        {
            _hub = hub;
            _options = options.Value;
            _log = log;
        }

        public async Task JoinAsync(string connectionId, string playerName, string lobbyCode)
        {
            string code = (lobbyCode ?? "").Trim().ToUpperInvariant();
            if (!LobbyCodePattern.IsMatch(code))
                throw new HubException("Lobby code must be 1-8 letters or digits.");

            string name = (playerName ?? "").Trim();
            if (name.Length == 0) name = "Player";
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

            bool switchingLobby;
            lock (_gate)
            {
                switchingLobby = _lobbyByConnection.TryGetValue(connectionId, out Lobby? current) && current.Code != code;
            }
            if (switchingLobby)
                await LeaveAsync(connectionId);

            Lobby lobby;
            LobbyState state;
            int round;
            byte[] board;
            RoundPhase phase;
            double secondsLeft;
            string? bestName;
            int bestMoves;
            lock (_gate)
            {
                if (!_lobbies.TryGetValue(code, out lobby!))
                {
                    lobby = new Lobby { Code = code };
                    _lobbies[code] = lobby;
                    StartNewRoundLocked(lobby);
                    _log.LogInformation("Lobby {Code} created", code);
                }

                // Re-joining the same lobby (e.g. to change name) keeps the score.
                int score = lobby.Players.TryGetValue(connectionId, out Player? existing) ? existing.Score : 0;
                lobby.Players[connectionId] = new Player { Name = name, Score = score };
                _lobbyByConnection[connectionId] = lobby;

                state = SnapshotLocked(lobby);
                round = lobby.RoundNumber;
                board = lobby.Board;
                phase = lobby.Phase;
                secondsLeft = Math.Max(0, (lobby.TimerEndUtc - DateTime.UtcNow).TotalSeconds);
                bestName = lobby.BestPlayerName;
                bestMoves = lobby.BestMoveCount;
            }

            await _hub.Groups.AddToGroupAsync(connectionId, code);
            await _hub.Clients.Group(code).SendAsync(HubMethods.LobbyState, state);

            // Catch the newcomer up on the round in progress.
            IClientProxy caller = _hub.Clients.Client(connectionId);
            await caller.SendAsync(HubMethods.RoundStarted, round, board);
            if (phase == RoundPhase.TimerRunning)
            {
                await caller.SendAsync(HubMethods.TimerStarted, round, secondsLeft);
                await caller.SendAsync(HubMethods.NewBestSolution, round, bestName, bestMoves);
            }
        }

        public async Task LeaveAsync(string connectionId)
        {
            Lobby? lobby;
            LobbyState? state = null;
            lock (_gate)
            {
                if (!_lobbyByConnection.Remove(connectionId, out lobby))
                    return;

                lobby.Players.Remove(connectionId);
                if (lobby.Players.Count == 0)
                {
                    lobby.Closed = true;
                    _lobbies.Remove(lobby.Code);
                    _log.LogInformation("Lobby {Code} closed", lobby.Code);
                }
                else
                {
                    state = SnapshotLocked(lobby);
                }
            }

            await _hub.Groups.RemoveFromGroupAsync(connectionId, lobby.Code);
            if (state != null)
                await _hub.Clients.Group(lobby.Code).SendAsync(HubMethods.LobbyState, state);
        }

        public async Task SubmitAsync(string connectionId, int roundNumber, byte[] moveData)
        {
            string? rejection;
            Lobby? lobby;
            bool timerStarted = false;
            string playerName = "";
            int moveCount = 0;

            lock (_gate)
            {
                rejection = TryAcceptLocked(connectionId, roundNumber, moveData, out lobby, out timerStarted,
                    out playerName, out moveCount);
            }

            if (rejection != null)
            {
                await _hub.Clients.Client(connectionId).SendAsync(HubMethods.SolutionRejected, rejection);
                return;
            }

            IClientProxy group = _hub.Clients.Group(lobby!.Code);
            if (timerStarted)
            {
                await group.SendAsync(HubMethods.TimerStarted, roundNumber, _options.TimerSeconds);
                RunInBackground(() => EndRoundAfterTimerAsync(lobby, roundNumber));
            }
            await group.SendAsync(HubMethods.NewBestSolution, roundNumber, playerName, moveCount);
        }

        /// <returns>Null if the solution was accepted as the new best, else the reason it wasn't.</returns>
        private string? TryAcceptLocked(string connectionId, int roundNumber, byte[] moveData, out Lobby? lobby,
            out bool timerStarted, out string playerName, out int moveCount)
        {
            timerStarted = false;
            playerName = "";
            moveCount = 0;

            if (!_lobbyByConnection.TryGetValue(connectionId, out lobby))
                return "You are not in a lobby.";
            if (roundNumber != lobby.RoundNumber || lobby.Phase == RoundPhase.Intermission)
                return "That round is over.";

            Move[] moves;
            try
            {
                moves = MoveCodec.Decode(moveData ?? Array.Empty<byte>());
            }
            catch (FormatException)
            {
                return "Malformed solution.";
            }
            if (moves.Length > _options.MaxSolutionMoves)
                return $"Solutions are limited to {_options.MaxSolutionMoves} moves.";

            SolutionResult result = GameRules.ValidateSolution(lobby.Round.Grid, lobby.Round.Robots, lobby.Round.Target, moves);
            if (result != SolutionResult.Valid)
                return $"Invalid solution ({result}).";

            if (lobby.Phase == RoundPhase.TimerRunning && moves.Length >= lobby.BestMoveCount)
                return $"Not better than the current best ({lobby.BestMoveCount} moves).";

            if (lobby.Phase == RoundPhase.Solving)
            {
                lobby.Phase = RoundPhase.TimerRunning;
                lobby.TimerEndUtc = DateTime.UtcNow.AddSeconds(_options.TimerSeconds);
                timerStarted = true;
            }

            playerName = lobby.Players[connectionId].Name;
            moveCount = moves.Length;
            lobby.BestConnectionId = connectionId;
            lobby.BestPlayerName = playerName;
            lobby.BestMoveCount = moveCount;
            return null;
        }

        private async Task EndRoundAfterTimerAsync(Lobby lobby, int roundNumber)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.TimerSeconds));

            string? winnerName;
            int moveCount;
            LobbyState state;
            lock (_gate)
            {
                if (lobby.Closed || lobby.RoundNumber != roundNumber || lobby.Phase != RoundPhase.TimerRunning)
                    return;

                lobby.Phase = RoundPhase.Intermission;
                winnerName = null;
                // The point only goes to the best solver if they're still here.
                if (lobby.BestConnectionId != null && lobby.Players.TryGetValue(lobby.BestConnectionId, out Player? winner))
                {
                    winner.Score++;
                    winnerName = winner.Name;
                }
                moveCount = lobby.BestMoveCount;
                state = SnapshotLocked(lobby);
            }

            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.RoundEnded, roundNumber, winnerName, moveCount);
            await group.SendAsync(HubMethods.LobbyState, state);

            await Task.Delay(TimeSpan.FromSeconds(_options.IntermissionSeconds));

            int nextRound;
            byte[] board;
            lock (_gate)
            {
                if (lobby.Closed || lobby.RoundNumber != roundNumber)
                    return;
                StartNewRoundLocked(lobby);
                nextRound = lobby.RoundNumber;
                board = lobby.Board;
            }
            await group.SendAsync(HubMethods.RoundStarted, nextRound, board);
        }

        private void StartNewRoundLocked(Lobby lobby)
        {
            lobby.RoundNumber++;
            lobby.Round = BoardGenerator.GenerateRound(_random);
            lobby.Board = BoardCodec.Encode(lobby.Round);
            lobby.Phase = RoundPhase.Solving;
            lobby.BestConnectionId = null;
            lobby.BestPlayerName = null;
            lobby.BestMoveCount = 0;
        }

        private static LobbyState SnapshotLocked(Lobby lobby)
        {
            return new LobbyState
            {
                Code = lobby.Code,
                Players = lobby.Players.Values
                    .Select(p => new PlayerInfo { Name = p.Name, Score = p.Score })
                    .ToArray(),
            };
        }

        private void RunInBackground(Func<Task> work)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await work();
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Round loop failed");
                }
            });
        }
    }
}
