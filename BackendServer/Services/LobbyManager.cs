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
        // Defaults for a new lobby's MatchSettings; the host can change them between matches.

        /// <summary>Timer started by the first valid solution of a round.</summary>
        public int FirstTimerSeconds { get; set; } = 60;

        /// <summary>
        /// Each strictly better solution after that tops the timer up to at least this much,
        /// so nobody can be sniped in the last seconds. It never shortens the timer.
        /// </summary>
        public int ExtensionSeconds { get; set; } = 45;

        /// <summary>First player to this many points wins the match.</summary>
        public int PointsToWin { get; set; } = 5;

        /// <summary>Countdown before each board: after Start, and after the previous round's replays.</summary>
        public double CountdownSeconds { get; set; } = 3;

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
        /// <summary>Round over (or no board yet); replays, then the next board.</summary>
        Intermission,
    }

    public class Player
    {
        public string Name { get; init; } = "";
        public int Score { get; set; }

        /// <summary>Increases with each join; the host passes to the lowest one left.</summary>
        public long JoinOrder { get; init; }

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

        public string? HostConnectionId { get; set; }
        public MatchSettings Settings { get; set; } = new();
        public MatchPhase Match { get; set; } = MatchPhase.Waiting;
        /// <summary>Bumped on every Start, so a board prepared for an older match is dropped.</summary>
        public int MatchId { get; set; }
        public int MatchRound { get; set; }
        public string? MatchWinner { get; set; }

        /// <summary>Unique across matches (submissions carry it); see <see cref="MatchRound"/> for display.</summary>
        public int RoundNumber { get; set; }
        /// <summary>The current board; null while none has been dealt in this match yet.</summary>
        public RoundSetup? Round { get; set; }
        public byte[] Board { get; set; } = Array.Empty<byte>();
        /// <summary>A shortest solution, or null if the solver gave up on this board.</summary>
        public Move[]? Shortest { get; set; }
        public RoundPhase Phase { get; set; } = RoundPhase.Intermission;
        public DateTime TimerEndUtc { get; set; }

        public string? BestConnectionId { get; set; }
        public string? BestPlayerName { get; set; }
        public int BestMoveCount { get; set; }
        public byte[] BestMoves { get; set; } = Array.Empty<byte>();

        /// <summary>Cancelled when everyone but the winner votes to skip the replay.</summary>
        public CancellationTokenSource? ReplaySkip { get; set; }

        /// <summary>When the next board is dealt; null when no countdown is running.</summary>
        public DateTime? CountdownEndUtc { get; set; }

        /// <summary>A round is being played or replayed (so solutions and votes count).</summary>
        public bool InRound => Match == MatchPhase.Playing && Round != null;
    }

    /// <summary>
    /// Owns all lobbies and runs matches: the host (the creator, passed on in join order) picks the
    /// settings and presses Start; rounds repeat until someone reaches the points target, then the
    /// lobby shows the results until the host starts again. Players can join at any time.
    /// All state is guarded by one lock; messages are sent through <see cref="IHubContext{THub}"/>
    /// after releasing it, because hub instances are disposed as soon as each hub call returns.
    /// Boards are generated and solved outside the lock so a slow solve never stalls other lobbies.
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
        private long _nextJoinOrder;

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
            lock (_gate)
                switchingLobby = _lobbyByConnection.TryGetValue(connectionId, out Lobby? current) && current.Code != code;
            if (switchingLobby)
                await LeaveAsync(connectionId);

            Lobby lobby;
            LobbyState state;
            bool inRound;
            int round;
            byte[] board;
            RoundPhase phase;
            double secondsLeft;
            string? bestName;
            int bestMoves;
            double? countdown;
            lock (_gate)
            {
                if (!_lobbies.TryGetValue(code, out lobby!))
                {
                    // The creator becomes the host (below) and starts the first match when everyone's in.
                    lobby = new Lobby
                    {
                        Code = code,
                        Settings = new MatchSettings
                        {
                            Mode = GameMode.Classic,
                            PointsToWin = _options.PointsToWin,
                            FirstTimerSeconds = _options.FirstTimerSeconds,
                            ExtensionSeconds = _options.ExtensionSeconds,
                        }.Clamped(),
                    };
                    _lobbies[code] = lobby;
                    _log.LogInformation("Lobby {Code} created", code);
                }

                // Re-joining the same lobby (e.g. to change name) keeps the score and place in line.
                lobby.Players.TryGetValue(connectionId, out Player? existing);
                // Names identify players to each other (and each client finds itself by name), so keep them unique.
                name = UniqueNameLocked(lobby, connectionId, name);
                lobby.Players[connectionId] = new Player
                {
                    Name = name,
                    Score = existing?.Score ?? 0,
                    JoinOrder = existing?.JoinOrder ?? ++_nextJoinOrder,
                };
                _lobbyByConnection[connectionId] = lobby;
                lobby.HostConnectionId ??= connectionId;

                state = SnapshotLocked(lobby);
                // Joining mid-match is allowed: the newcomer gets the current board (unless it's still being prepared).
                inRound = lobby.InRound;
                round = lobby.RoundNumber;
                board = lobby.Board;
                phase = lobby.Phase;
                secondsLeft = Math.Max(0, (lobby.TimerEndUtc - DateTime.UtcNow).TotalSeconds);
                bestName = lobby.BestPlayerName;
                bestMoves = lobby.BestMoveCount;
                countdown = lobby.CountdownEndUtc.HasValue
                    ? Math.Max(0, (lobby.CountdownEndUtc.Value - DateTime.UtcNow).TotalSeconds)
                    : null;
            }

            await _hub.Groups.AddToGroupAsync(connectionId, code);
            await _hub.Clients.Group(code).SendAsync(HubMethods.LobbyState, state);

            // Catch the newcomer up on the round in progress and/or the countdown to the next one.
            IClientProxy caller = _hub.Clients.Client(connectionId);
            if (inRound)
            {
                await caller.SendAsync(HubMethods.RoundStarted, round, board);
                if (phase == RoundPhase.TimerRunning)
                {
                    await caller.SendAsync(HubMethods.TimerUpdated, round, secondsLeft);
                    await caller.SendAsync(HubMethods.NewBestSolution, round, bestName, bestMoves);
                }
            }
            if (countdown.HasValue)
                await caller.SendAsync(HubMethods.RoundCountdown, countdown.Value);
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
                    // The host passes to whoever has been here longest.
                    if (lobby.HostConnectionId == connectionId)
                        lobby.HostConnectionId = lobby.Players.MinBy(p => p.Value.JoinOrder).Key;
                    state = SnapshotLocked(lobby);
                    round = lobby.RoundNumber;
                    // The one player everyone was waiting on may have just left.
                    if (lobby.InRound && AllOthersDoneLocked(lobby))
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
        /// Host only, when no match is running: resets scores and deals the first board after the
        /// countdown. The board is prepared outside the lock (during the countdown), so meanwhile the
        /// lobby is already Playing but with no board.
        /// </summary>
        public async Task StartMatchAsync(string connectionId)
        {
            Lobby lobby;
            LobbyState state;
            int matchId;
            lock (_gate)
            {
                lobby = HostLobbyLocked(connectionId, "start the match");
                if (lobby.Match == MatchPhase.Playing)
                    throw new HubException("A match is already running.");

                lobby.Match = MatchPhase.Playing;
                matchId = ++lobby.MatchId;
                lobby.MatchRound = 0;
                lobby.MatchWinner = null;
                // No board yet: solutions and votes are refused until it's dealt.
                lobby.Round = null;
                lobby.Phase = RoundPhase.Intermission;
                ClearBestLocked(lobby);
                foreach (Player player in lobby.Players.Values)
                {
                    player.Score = 0;
                    player.Done = false;
                }
                lobby.CountdownEndUtc = DateTime.UtcNow.AddSeconds(_options.CountdownSeconds);
                state = SnapshotLocked(lobby);
                _log.LogInformation("Lobby {Code}: match started, first to {Points}", lobby.Code, lobby.Settings.PointsToWin);
            }

            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.LobbyState, state);
            await group.SendAsync(HubMethods.RoundCountdown, _options.CountdownSeconds);

            // The board is generated during the countdown; a slow solve just stretches the wait.
            Task<PreparedRound> prepare = Task.Run(PrepareRound);
            await Task.Delay(TimeSpan.FromSeconds(_options.CountdownSeconds));
            PreparedRound prepared = await prepare;
            await DealRoundAsync(lobby, prepared, () => lobby.MatchId == matchId);
        }

        /// <summary>Deals <paramref name="prepared"/> unless the lobby closed or <paramref name="stillWanted"/> (checked under the lock) is false.</summary>
        private async Task DealRoundAsync(Lobby lobby, PreparedRound prepared, Func<bool> stillWanted)
        {
            int round;
            byte[] board;
            LobbyState state;
            lock (_gate)
            {
                if (lobby.Closed || !stillWanted())
                    return;
                StartNewRoundLocked(lobby, prepared);
                round = lobby.RoundNumber;
                board = lobby.Board;
                state = SnapshotLocked(lobby);
            }
            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.RoundStarted, round, board);
            await group.SendAsync(HubMethods.LobbyState, state);
        }

        /// <summary>Host only, between matches. Out-of-range values are clamped.</summary>
        public async Task UpdateSettingsAsync(string connectionId, MatchSettings settings)
        {
            Lobby lobby;
            LobbyState state;
            lock (_gate)
            {
                lobby = HostLobbyLocked(connectionId, "change the settings");
                if (lobby.Match == MatchPhase.Playing)
                    throw new HubException("Settings can't change during a match.");
                lobby.Settings = (settings ?? lobby.Settings).Clamped();
                state = SnapshotLocked(lobby);
            }
            await _hub.Clients.Group(lobby.Code).SendAsync(HubMethods.LobbyState, state);
        }

        private Lobby HostLobbyLocked(string connectionId, string action)
        {
            if (!_lobbyByConnection.TryGetValue(connectionId, out Lobby? lobby))
                throw new HubException("You are not in a lobby.");
            if (lobby.HostConnectionId != connectionId)
                throw new HubException($"Only the host can {action}.");
            return lobby;
        }

        /// <summary>
        /// A player votes (or un-votes). During a round: to end it early - when every player except the
        /// leader has voted, it ends now (skipped with no point if nobody solved it, otherwise as if the
        /// timer ran out). After a round: to skip the replay - when every player except the winner has
        /// voted, the next board (or the match results) comes straight away.
        /// </summary>
        public async Task SetDoneAsync(string connectionId, int roundNumber, bool done)
        {
            Lobby? lobby;
            LobbyState state;
            bool endRound = false;
            lock (_gate)
            {
                if (!_lobbyByConnection.TryGetValue(connectionId, out lobby) || !lobby.InRound
                    || lobby.RoundNumber != roundNumber)
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
            if (!lobby.InRound || roundNumber != lobby.RoundNumber || lobby.Phase == RoundPhase.Intermission)
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

            RoundSetup round = lobby.Round!;
            SolutionResult result = GameRules.ValidateSolution(round.Grid, round.Robots, round.Target, moves);
            if (result != SolutionResult.Valid)
                return $"Invalid solution ({result}).";

            if (lobby.Phase == RoundPhase.TimerRunning && moves.Length >= lobby.BestMoveCount)
                return $"Not better than the current best ({lobby.BestMoveCount} moves).";

            DateTime now = DateTime.UtcNow;
            if (lobby.Phase == RoundPhase.Solving)
            {
                lobby.Phase = RoundPhase.TimerRunning;
                lobby.TimerEndUtc = now.AddSeconds(lobby.Settings.FirstTimerSeconds);
                timerStarted = true;
                secondsLeft = lobby.Settings.FirstTimerSeconds;
            }
            else
            {
                DateTime extended = now.AddSeconds(lobby.Settings.ExtensionSeconds);
                if (extended > lobby.TimerEndUtc)
                {
                    lobby.TimerEndUtc = extended;
                    secondsLeft = lobby.Settings.ExtensionSeconds;
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
        /// the result and replays, then either counts down and deals the next board - prepared while the
        /// replays play - or, if that point won the match, shows the results. Safe to call more than
        /// once; only the first call does anything.
        /// </summary>
        private async Task EndRoundAsync(Lobby lobby, int roundNumber)
        {
            RoundResult result;
            LobbyState state;
            CancellationTokenSource skip;
            string? matchWinner = null;
            lock (_gate)
            {
                if (lobby.Closed || lobby.RoundNumber != roundNumber || lobby.Phase == RoundPhase.Intermission)
                    return;

                lobby.Phase = RoundPhase.Intermission;
                // The point only goes to the best solver if they're still here.
                if (lobby.BestConnectionId != null && lobby.Players.TryGetValue(lobby.BestConnectionId, out Player? winner))
                {
                    winner.Score++;
                    if (winner.Score >= lobby.Settings.PointsToWin)
                        matchWinner = winner.Name;
                }
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
                    MatchWinnerName = matchWinner,
                };
                skip = new CancellationTokenSource();
                lobby.ReplaySkip = skip;
                state = SnapshotLocked(lobby);
            }

            // Prepare the next board while everyone watches the replays (unless the match is over).
            Task<PreparedRound>? next = matchWinner == null ? Task.Run(PrepareRound) : null;

            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.RoundEnded, result);
            await group.SendAsync(HubMethods.LobbyState, state);

            try
            {
                await Task.Delay(ReplayDuration(result), skip.Token);
            }
            catch (OperationCanceledException)
            {
                // Everyone but the winner voted to skip (or the lobby closed).
            }

            lock (_gate)
            {
                if (lobby.ReplaySkip == skip)
                    lobby.ReplaySkip = null;
                if (lobby.Closed || lobby.RoundNumber != roundNumber)
                    return;

                if (next != null)
                    lobby.CountdownEndUtc = DateTime.UtcNow.AddSeconds(_options.CountdownSeconds);
                else
                {
                    // Results until the host starts again; the last board stays as it was.
                    lobby.Match = MatchPhase.Finished;
                    lobby.MatchWinner = matchWinner;
                    ClearBestLocked(lobby);
                    foreach (Player player in lobby.Players.Values)
                        player.Done = false;
                    state = SnapshotLocked(lobby);
                    _log.LogInformation("Lobby {Code}: {Winner} won the match", lobby.Code, matchWinner);
                }
            }
            if (next == null)
            {
                await group.SendAsync(HubMethods.LobbyState, state);
                return;
            }

            await group.SendAsync(HubMethods.RoundCountdown, _options.CountdownSeconds);
            await Task.Delay(TimeSpan.FromSeconds(_options.CountdownSeconds));
            PreparedRound prepared = await next;
            await DealRoundAsync(lobby, prepared, () => lobby.RoundNumber == roundNumber);
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
            lobby.MatchRound++;
            lobby.Round = prepared.Round;
            lobby.Shortest = prepared.Shortest;
            lobby.Board = BoardCodec.Encode(prepared.Round);
            lobby.Phase = RoundPhase.Solving;
            lobby.CountdownEndUtc = null;
            ClearBestLocked(lobby);
            foreach (Player player in lobby.Players.Values)
                player.Done = false;
        }

        private static void ClearBestLocked(Lobby lobby)
        {
            lobby.BestConnectionId = null;
            lobby.BestPlayerName = null;
            lobby.BestMoveCount = 0;
            lobby.BestMoves = Array.Empty<byte>();
        }

        private static LobbyState SnapshotLocked(Lobby lobby)
        {
            return new LobbyState
            {
                Code = lobby.Code,
                Host = lobby.HostConnectionId != null && lobby.Players.TryGetValue(lobby.HostConnectionId, out Player? host)
                    ? host.Name
                    : null,
                Phase = lobby.Match,
                Settings = lobby.Settings,
                MatchRound = lobby.MatchRound,
                MatchWinner = lobby.MatchWinner,
                Players = lobby.Players
                    .OrderBy(p => p.Value.JoinOrder)
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
