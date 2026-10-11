using System.Diagnostics;
using System.Text.RegularExpressions;
using BackendServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SharedLogic;

namespace BackendServer.Services
{
    public class GameOptions
    {
        /// <summary>Timer started by the first valid solution of a round.</summary>
        public double FirstTimerSeconds { get; set; } = 60;

        /// <summary>
        /// Each strictly better solution after that tops the timer up to at least this much,
        /// so nobody can be sniped in the last seconds. It never shortens the timer.
        /// </summary>
        public double ExtensionSeconds { get; set; } = 45;

        /// <summary>Pause after the replays before the next board.</summary>
        public double IntermissionSeconds { get; set; } = 3;

        /// <summary>Seconds per move when replaying the winner's solution.</summary>
        public double ReplayStepSeconds { get; set; } = 1;

        /// <summary>Seconds per move when replaying the shortest solution (shown after a longer winning one).</summary>
        public double ShortestReplayStepSeconds { get; set; } = 0.6;

        /// <summary>Pause on the starting position before each replay.</summary>
        public double ReplayPauseSeconds { get; set; } = 1;

        /// <summary>Boards whose shortest solution is shorter than this are thrown away. Hard boards are always kept.</summary>
        public int MinBoardMoves { get; set; } = 3;

        /// <summary>Time the solver gets per board. If it gives up, the board is kept with no known shortest solution.</summary>
        public double SolverTimeLimitSeconds { get; set; } = 5;

        public int MaxSolutionMoves { get; set; } = 64;
    }

    public enum RoundPhase
    {
        /// <summary>Nobody has found a solution yet; no timer.</summary>
        Solving,
        /// <summary>Someone solved it; better solutions are accepted until the timer runs out.</summary>
        TimerRunning,
        /// <summary>Round over; replays, then the next board.</summary>
        Intermission,
    }

    public class Player
    {
        public string Name { get; init; } = "";
        public int Score { get; set; }

        /// <summary>
        /// During a round: voted to end it early ("Skip board" / "I'm done"). After it: voted to skip
        /// the replay. Cleared when the round ends and when the next one starts.
        /// </summary>
        public bool Done { get; set; }
    }

    /// <summary>A generated board plus its shortest solution, if the solver found one in time.</summary>
    public sealed record PreparedRound(RoundSetup Round, Move[]? Shortest);

    public class Lobby
    {
        public string Code { get; init; } = "";
        public Dictionary<string, Player> Players { get; } = new();
        public bool Closed { get; set; }

        public int RoundNumber { get; set; }
        public RoundSetup Round { get; set; } = null!;
        public byte[] Board { get; set; } = Array.Empty<byte>();
        /// <summary>A shortest solution, or null if the solver gave up on this board.</summary>
        public Move[]? Shortest { get; set; }
        public RoundPhase Phase { get; set; }
        public DateTime TimerEndUtc { get; set; }

        public string? BestConnectionId { get; set; }
        public string? BestPlayerName { get; set; }
        public int BestMoveCount { get; set; }
        public byte[] BestMoves { get; set; } = Array.Empty<byte>();

        /// <summary>Cancelled when everyone but the winner votes to skip the replay.</summary>
        public CancellationTokenSource? ReplaySkip { get; set; }
    }

    /// <summary>
    /// Owns all lobbies and runs the round loop. All state is guarded by one lock; messages
    /// are sent through <see cref="IHubContext{THub}"/> after releasing it, because hub
    /// instances are disposed as soon as each hub call returns. Boards are generated and solved
    /// outside the lock so a slow solve never stalls other lobbies.
    /// </summary>
    public class LobbyManager
    {
        private static readonly Regex LobbyCodePattern = new("^[A-Z0-9]{1,8}$");
        private const int MaxNameLength = 20;
        private const int MaxBoardAttempts = 50;

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

        /// <returns>The player's name as shown in the lobby (made unique, e.g. "Sam 2").</returns>
        public async Task<string> JoinAsync(string connectionId, string playerName, string lobbyCode)
        {
            string code = (lobbyCode ?? "").Trim().ToUpperInvariant();
            if (!LobbyCodePattern.IsMatch(code))
                throw new HubException("Lobby code must be 1-8 letters or digits.");

            string name = (playerName ?? "").Trim();
            if (name.Length == 0) name = "Player";
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

            bool switchingLobby;
            bool lobbyExists;
            lock (_gate)
            {
                switchingLobby = _lobbyByConnection.TryGetValue(connectionId, out Lobby? current) && current.Code != code;
                lobbyExists = _lobbies.ContainsKey(code);
            }
            if (switchingLobby)
                await LeaveAsync(connectionId);

            // A new lobby needs its first board; prepare it before taking the lock.
            PreparedRound? firstRound = lobbyExists ? null : await Task.Run(PrepareRound);

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
                    // Someone may have closed the lobby between the two lock sections; prepare inline then.
                    StartNewRoundLocked(lobby, firstRound ?? PrepareRound());
                    _log.LogInformation("Lobby {Code} created", code);
                }

                // Re-joining the same lobby (e.g. to change name) keeps the score.
                int score = lobby.Players.TryGetValue(connectionId, out Player? existing) ? existing.Score : 0;
                // Names identify players to each other (and each client finds itself by name), so keep them unique.
                name = UniqueNameLocked(lobby, connectionId, name);
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
                await caller.SendAsync(HubMethods.TimerUpdated, round, secondsLeft);
                await caller.SendAsync(HubMethods.NewBestSolution, round, bestName, bestMoves);
            }
            return name;
        }

        private static string UniqueNameLocked(Lobby lobby, string connectionId, string name)
        {
            bool Taken(string candidate) => lobby.Players.Any(p =>
                p.Key != connectionId && string.Equals(p.Value.Name, candidate, StringComparison.OrdinalIgnoreCase));

            string unique = name;
            for (int n = 2; Taken(unique); n++)
                unique = $"{name} {n}";
            return unique;
        }

        public async Task LeaveAsync(string connectionId)
        {
            Lobby? lobby;
            LobbyState? state = null;
            bool endRound = false;
            int round = 0;
            lock (_gate)
            {
                if (!_lobbyByConnection.Remove(connectionId, out lobby))
                    return;

                lobby.Players.Remove(connectionId);
                if (lobby.Players.Count == 0)
                {
                    lobby.Closed = true;
                    lobby.ReplaySkip?.Cancel();
                    _lobbies.Remove(lobby.Code);
                    _log.LogInformation("Lobby {Code} closed", lobby.Code);
                }
                else
                {
                    state = SnapshotLocked(lobby);
                    round = lobby.RoundNumber;
                    // The one player everyone was waiting on may have just left.
                    if (AllOthersDoneLocked(lobby))
                    {
                        if (lobby.Phase == RoundPhase.Intermission)
                            lobby.ReplaySkip?.Cancel();
                        else
                            endRound = true;
                    }
                }
            }

            await _hub.Groups.RemoveFromGroupAsync(connectionId, lobby.Code);
            if (state != null)
                await _hub.Clients.Group(lobby.Code).SendAsync(HubMethods.LobbyState, state);
            if (endRound)
                RunInBackground(() => EndRoundAsync(lobby, round));
        }

        /// <summary>
        /// A player votes (or un-votes). During a round: to end it early - when every player except the
        /// leader has voted, it ends now (skipped with no point if nobody solved it, otherwise as if the
        /// timer ran out). After a round: to skip the replay - when every player except the winner has
        /// voted, the next board comes straight away.
        /// </summary>
        public async Task SetDoneAsync(string connectionId, int roundNumber, bool done)
        {
            Lobby? lobby;
            LobbyState state;
            bool endRound = false;
            lock (_gate)
            {
                if (!_lobbyByConnection.TryGetValue(connectionId, out lobby) || lobby.RoundNumber != roundNumber)
                    return;
                // The leader / winner isn't part of either vote.
                if (connectionId == lobby.BestConnectionId)
                    return;

                lobby.Players[connectionId].Done = done;
                state = SnapshotLocked(lobby);
                if (AllOthersDoneLocked(lobby))
                {
                    if (lobby.Phase == RoundPhase.Intermission)
                        lobby.ReplaySkip?.Cancel();
                    else
                        endRound = true;
                }
            }

            await _hub.Clients.Group(lobby.Code).SendAsync(HubMethods.LobbyState, state);
            // Not awaited: ending a round includes the replay and intermission delays.
            if (endRound)
                RunInBackground(() => EndRoundAsync(lobby, roundNumber));
        }

        /// <summary>True when every player except the current leader / winner (if any) has voted.</summary>
        private static bool AllOthersDoneLocked(Lobby lobby) =>
            lobby.Players.Count > 0
            && lobby.Players.Where(p => p.Key != lobby.BestConnectionId).All(p => p.Value.Done);

        public async Task SubmitAsync(string connectionId, int roundNumber, byte[] moveData)
        {
            string? rejection;
            Lobby? lobby;
            bool timerStarted = false;
            double? secondsLeft = null;
            string playerName = "";
            int moveCount = 0;
            LobbyState? state = null;
            bool endRound = false;

            lock (_gate)
            {
                rejection = TryAcceptLocked(connectionId, roundNumber, moveData, out lobby, out timerStarted,
                    out secondsLeft, out playerName, out moveCount);
                if (rejection == null)
                {
                    state = SnapshotLocked(lobby!);
                    // Nobody can beat the shortest possible solution, and everyone else may already have
                    // given up (or there's nobody else): either way, no need to wait for the timer.
                    bool unbeatable = lobby!.Shortest != null && moveCount <= lobby.Shortest.Length;
                    endRound = unbeatable || AllOthersDoneLocked(lobby);
                }
            }

            if (rejection != null)
            {
                await _hub.Clients.Client(connectionId).SendAsync(HubMethods.SolutionRejected, rejection);
                return;
            }

            IClientProxy group = _hub.Clients.Group(lobby!.Code);
            if (!endRound)
            {
                if (secondsLeft.HasValue)
                    await group.SendAsync(HubMethods.TimerUpdated, roundNumber, secondsLeft.Value);
                if (timerStarted)
                    RunInBackground(() => EndRoundAfterTimerAsync(lobby, roundNumber));
            }
            await group.SendAsync(HubMethods.NewBestSolution, roundNumber, playerName, moveCount);
            await group.SendAsync(HubMethods.LobbyState, state);
            if (endRound)
                RunInBackground(() => EndRoundAsync(lobby, roundNumber));
        }

        /// <returns>Null if the solution was accepted as the new best, else the reason it wasn't.</returns>
        /// <param name="secondsLeft">Set when the timer started or was extended.</param>
        private string? TryAcceptLocked(string connectionId, int roundNumber, byte[] moveData, out Lobby? lobby,
            out bool timerStarted, out double? secondsLeft, out string playerName, out int moveCount)
        {
            timerStarted = false;
            secondsLeft = null;
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

            DateTime now = DateTime.UtcNow;
            if (lobby.Phase == RoundPhase.Solving)
            {
                lobby.Phase = RoundPhase.TimerRunning;
                lobby.TimerEndUtc = now.AddSeconds(_options.FirstTimerSeconds);
                timerStarted = true;
                secondsLeft = _options.FirstTimerSeconds;
            }
            else
            {
                DateTime extended = now.AddSeconds(_options.ExtensionSeconds);
                if (extended > lobby.TimerEndUtc)
                {
                    lobby.TimerEndUtc = extended;
                    secondsLeft = _options.ExtensionSeconds;
                }
            }

            Player player = lobby.Players[connectionId];
            playerName = player.Name;
            moveCount = moves.Length;
            // The leader isn't part of the vote; drop any "skip" they pressed before solving it.
            player.Done = false;
            lobby.BestConnectionId = connectionId;
            lobby.BestPlayerName = playerName;
            lobby.BestMoveCount = moveCount;
            lobby.BestMoves = MoveCodec.Encode(moves);
            return null;
        }

        private async Task EndRoundAfterTimerAsync(Lobby lobby, int roundNumber)
        {
            while (true)
            {
                // Better solutions can push the deadline back while we wait, so re-check it each time.
                TimeSpan wait;
                lock (_gate)
                {
                    if (lobby.Closed || lobby.RoundNumber != roundNumber || lobby.Phase != RoundPhase.TimerRunning)
                        return;
                    wait = lobby.TimerEndUtc - DateTime.UtcNow;
                }
                if (wait <= TimeSpan.Zero)
                    break;
                await Task.Delay(wait);
            }

            await EndRoundAsync(lobby, roundNumber);
        }

        /// <summary>
        /// Ends the round (timer ran out, everyone else done, or the shortest solution was found), sends
        /// the result and replays, then deals the next board - prepared while the replays play. Safe to
        /// call more than once; only the first call does anything.
        /// </summary>
        private async Task EndRoundAsync(Lobby lobby, int roundNumber)
        {
            RoundResult result;
            LobbyState state;
            CancellationTokenSource skip;
            lock (_gate)
            {
                if (lobby.Closed || lobby.RoundNumber != roundNumber || lobby.Phase == RoundPhase.Intermission)
                    return;

                lobby.Phase = RoundPhase.Intermission;
                // The point only goes to the best solver if they're still here.
                if (lobby.BestConnectionId != null && lobby.Players.TryGetValue(lobby.BestConnectionId, out Player? winner))
                    winner.Score++;
                // Votes from here on mean "skip the replay", so start counting afresh.
                foreach (Player player in lobby.Players.Values)
                    player.Done = false;

                result = new RoundResult
                {
                    RoundNumber = roundNumber,
                    WinnerName = lobby.BestPlayerName, // null when the board was skipped without a solution
                    WinnerMoves = lobby.BestMoves,
                    ShortestMoves = lobby.Shortest != null ? MoveCodec.Encode(lobby.Shortest) : Array.Empty<byte>(),
                    ReplayStepSeconds = _options.ReplayStepSeconds,
                    ShortestReplayStepSeconds = _options.ShortestReplayStepSeconds,
                    ReplayPauseSeconds = _options.ReplayPauseSeconds,
                };
                skip = new CancellationTokenSource();
                lobby.ReplaySkip = skip;
                state = SnapshotLocked(lobby);
            }

            // Prepare the next board while everyone watches the replays.
            Task<PreparedRound> next = Task.Run(PrepareRound);

            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.RoundEnded, result);
            await group.SendAsync(HubMethods.LobbyState, state);

            try
            {
                await Task.Delay(ReplayDuration(result) + TimeSpan.FromSeconds(_options.IntermissionSeconds), skip.Token);
            }
            catch (OperationCanceledException)
            {
                // Everyone but the winner voted to skip (or the lobby closed).
            }

            PreparedRound prepared = await next;
            int nextRound;
            byte[] board;
            lock (_gate)
            {
                if (lobby.ReplaySkip == skip)
                    lobby.ReplaySkip = null;
                if (lobby.Closed || lobby.RoundNumber != roundNumber)
                    return;
                StartNewRoundLocked(lobby, prepared);
                nextRound = lobby.RoundNumber;
                board = lobby.Board;
                state = SnapshotLocked(lobby);
            }
            await group.SendAsync(HubMethods.RoundStarted, nextRound, board);
            await group.SendAsync(HubMethods.LobbyState, state);
        }

        /// <summary>
        /// How long the clients' replays take: the winner's solution, then the shortest one if it's
        /// shorter (or only the shortest when nobody solved it), each after a pause.
        /// </summary>
        private static TimeSpan ReplayDuration(RoundResult result)
        {
            int winner = result.WinnerMoves.Length;
            int shortest = result.ShortestMoves.Length;
            double seconds = 0;
            if (winner > 0)
                seconds += result.ReplayPauseSeconds + winner * result.ReplayStepSeconds;
            if (shortest > 0 && (winner == 0 || winner > shortest))
                seconds += result.ReplayPauseSeconds + shortest * result.ShortestReplayStepSeconds;
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// Generates a board and solves it, discarding trivial ones (shortest solution below
        /// <see cref="GameOptions.MinBoardMoves"/>). Hard boards are kept even if the solver gives up.
        /// Runs outside the lock.
        /// </summary>
        private PreparedRound PrepareRound()
        {
            int seed;
            lock (_gate)
                seed = _random.Next();
            var random = new Random(seed);
            var timeLimit = TimeSpan.FromSeconds(_options.SolverTimeLimitSeconds);

            var clock = Stopwatch.StartNew();
            for (int attempt = 1; ; attempt++)
            {
                RoundSetup round = BoardGenerator.GenerateRound(random);
                SolverResult solved = Solver.Solve(round, timeLimit: timeLimit);
                bool trivial = solved.Solved && solved.Moves.Length < _options.MinBoardMoves;
                if (!trivial || attempt >= MaxBoardAttempts)
                {
                    _log.LogInformation("Board ready: shortest {Moves} moves (attempt {Attempt}, {Ms} ms)",
                        solved.Solved ? solved.Moves.Length.ToString() : "unknown", attempt, clock.ElapsedMilliseconds);
                    return new PreparedRound(round, solved.Moves);
                }
            }
        }

        private static void StartNewRoundLocked(Lobby lobby, PreparedRound prepared)
        {
            lobby.RoundNumber++;
            lobby.Round = prepared.Round;
            lobby.Shortest = prepared.Shortest;
            lobby.Board = BoardCodec.Encode(lobby.Round);
            lobby.Phase = RoundPhase.Solving;
            lobby.BestConnectionId = null;
            lobby.BestPlayerName = null;
            lobby.BestMoveCount = 0;
            lobby.BestMoves = Array.Empty<byte>();
            foreach (Player player in lobby.Players.Values)
                player.Done = false;
        }

        private static LobbyState SnapshotLocked(Lobby lobby)
        {
            return new LobbyState
            {
                Code = lobby.Code,
                Players = lobby.Players
                    .Select(p => new PlayerInfo
                    {
                        Name = p.Value.Name,
                        Score = p.Value.Score,
                        Done = p.Value.Done,
                        Leading = p.Key == lobby.BestConnectionId,
                    })
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
