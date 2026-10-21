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

        /// <summary>
        /// Time to find a first solution before the board ends unsolved (no point); 0 = no limit. The
        /// first solution starts the normal timer instead, so it never cuts a race short.
        /// </summary>
        public int RoundLimitSeconds { get; set; } = 300;

        /// <summary>First player to this many points wins the match.</summary>
        public int PointsToWin { get; set; } = 5;

        /// <summary>Countdown before each board: after Start, and after the previous round's replays.</summary>
        public double CountdownSeconds { get; set; } = 3;

        /// <summary>Seconds per move when replaying the winner's solution.</summary>
        public double ReplayStepSeconds { get; set; } = 0.8;

        /// <summary>Seconds per move when replaying the shortest solution after a longer winning one. Alone (nobody solved it) it uses ReplayStepSeconds.</summary>
        public double ShortestReplayStepSeconds { get; set; } = 0.6;

        /// <summary>Pause on the starting position before each replay.</summary>
        public double ReplayPauseSeconds { get; set; } = 1;

        /// <summary>Pause before the replays when a solution ends the round at once (<see cref="RoundResult.SolvedPauseSeconds"/>).</summary>
        public double SolvedPauseSeconds { get; set; } = 1.5;

        /// <summary>Pause on the replay's final position before the next countdown (not after a skipped replay).</summary>
        public double AfterReplaySeconds { get; set; } = 1;

        /// <summary>
        /// How long a disconnected player's seat (score, host role, place in line) is kept for them to
        /// reconnect. Leaving on purpose frees it at once.
        /// </summary>
        public double ReconnectGraceSeconds { get; set; } = 60;

        /// <summary>New lobbies' <see cref="MatchSettings.MinMoves"/>: boards whose shortest solution is shorter are thrown away.</summary>
        public int MinBoardMoves { get; set; } = MatchSettings.DefaultMinMoves;

        /// <summary>
        /// Time to find a board meeting the lobby's minimum (it runs during the countdown/replays); after
        /// that the longest board found so far is dealt.
        /// </summary>
        public double BoardSearchSeconds { get; set; } = 20;

        /// <summary>Highest <see cref="MatchSettings.MinMoves"/> / <see cref="MatchSettings.MaxMoves"/> a host may pick. Raise it only on a dev server (with a longer search).</summary>
        public int MaxMinMoves { get; set; } = MatchSettings.MaxMinMoves;

        /// <summary>Time the solver gets per board. If it gives up, the board is kept with no known shortest solution.</summary>
        public double SolverTimeLimitSeconds { get; set; } = 5;

        public int MaxSolutionMoves { get; set; } = 64;

        /// <summary>Seeds board generation (for tests); null picks a random seed.</summary>
        public int? Seed { get; set; }
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

    /// <summary>
    /// A seat in a lobby. It outlives the connection: if the player drops, the seat (score, host role,
    /// place in line) waits <see cref="GameOptions.ReconnectGraceSeconds"/> for them to come back.
    /// </summary>
    public class Player
    {
        /// <summary>Stable id the client sends with each join (falls back to the first connection id).</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Score { get; set; }

        /// <summary>Null while disconnected.</summary>
        public string? ConnectionId { get; set; }
        public DateTime? DisconnectedAtUtc { get; set; }
        public bool Connected => ConnectionId != null;

        /// <summary>Increases with each join; the host passes to the lowest one left.</summary>
        public long JoinOrder { get; init; }

        /// <summary>
        /// During a round: voted to end it early ("Skip board" / "I'm done"). After it: voted to skip
        /// the replay. Cleared when the round ends and when the next one starts.
        /// </summary>
        public bool Done { get; set; }
    }

    /// <summary>A player's best solution for the current board, for the match history.</summary>
    public sealed class OwnBest
    {
        public string PlayerId { get; init; } = "";
        public string Name { get; set; } = "";
        public byte[] Moves { get; set; } = Array.Empty<byte>();
        public int Length { get; set; }
        /// <summary>Order found (lower = earlier), so equal lengths list whoever found theirs first.</summary>
        public long Found { get; set; }
    }

    /// <summary>A generated board plus its shortest solution, if the solver found one in time.</summary>
    public sealed record PreparedRound(RoundSetup Round, Move[]? Shortest);

    public class Lobby
    {
        public string Code { get; init; } = "";
        /// <summary>Seats by <see cref="Player.Id"/>, connected or not.</summary>
        public Dictionary<string, Player> Players { get; } = new();
        public bool Closed { get; set; }

        public string? HostId { get; set; }
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
        /// <summary>When an unsolved board ends (<see cref="MatchSettings.RoundLimitSeconds"/>); null if no limit or solved.</summary>
        public DateTime? LimitEndUtc { get; set; }

        public string? BestPlayerId { get; set; }
        public string? BestPlayerName { get; set; }
        public int BestMoveCount { get; set; }
        public byte[] BestMoves { get; set; } = Array.Empty<byte>();

        /// <summary>Each player's best solution for the current board (lead or not), for the match history.</summary>
        public List<OwnBest> OwnBests { get; } = new();
        public long NextFound { get; set; }
        /// <summary>The current (or just finished) match's boards, kept until the next match starts.</summary>
        public List<RoundRecord> History { get; } = new();

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
    /// lobby shows the results until the host starts again. Players can join at any time, and a player
    /// who drops keeps their seat for a while to reconnect.
    /// All state is guarded by one lock; messages are sent through <see cref="IHubContext{THub}"/>
    /// after releasing it, because hub instances are disposed as soon as each hub call returns.
    /// Boards are generated and solved outside the lock so a slow solve never stalls other lobbies.
    /// All waiting goes through <see cref="TimeProvider"/> so tests can fast-forward it.
    /// </summary>
    public class LobbyManager
    {
        private static readonly Regex LobbyCodePattern = new("^[A-Z0-9]{1,8}$");
        private const int MaxNameLength = 20;
        private const int MaxPlayerIdLength = 64;

        private sealed record Seat(Lobby Lobby, Player Player);

        private readonly object _gate = new();
        private readonly Dictionary<string, Lobby> _lobbies = new();
        private readonly Dictionary<string, Seat> _seatByConnection = new();
        private readonly Random _random;
        private long _nextJoinOrder;

        private readonly IHubContext<GameHub> _hub;
        private readonly GameOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<LobbyManager> _log;

        public LobbyManager(IHubContext<GameHub> hub, IOptions<GameOptions> options, TimeProvider time,
            ILogger<LobbyManager> log)
        {
            _hub = hub;
            _options = options.Value;
            _time = time;
            _log = log;
            _random = _options.Seed.HasValue ? new Random(_options.Seed.Value) : new Random();
        }

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        /// <summary>
        /// Joins (or creates) a lobby. A <paramref name="playerId"/> that already has a seat there takes
        /// it back with its score (reconnecting, or re-joining to change name); so does a name matching
        /// a disconnected seat (e.g. after restarting the game).
        /// </summary>
        /// <returns>The player's name as shown in the lobby (made unique, e.g. "Sam 2").</returns>
        public async Task<string> JoinAsync(string connectionId, string playerName, string lobbyCode, string? playerId)
        {
            string code = (lobbyCode ?? "").Trim().ToUpperInvariant();
            if (!LobbyCodePattern.IsMatch(code))
                throw new HubException("Lobby code must be 1-8 letters or digits.");

            string name = (playerName ?? "").Trim();
            if (name.Length == 0) name = "Player";
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

            string id = string.IsNullOrWhiteSpace(playerId) ? connectionId : playerId.Trim();
            if (id.Length > MaxPlayerIdLength) id = id.Substring(0, MaxPlayerIdLength);

            bool switchingLobby;
            lock (_gate)
                switchingLobby = _seatByConnection.TryGetValue(connectionId, out Seat? current) && current.Lobby.Code != code;
            if (switchingLobby)
                await LeaveAsync(connectionId);

            Lobby lobby;
            LobbyState state;
            string? displaced = null;
            bool inRound;
            int round;
            byte[] board;
            RoundPhase phase;
            double secondsLeft;
            double? limitLeft;
            string? bestName;
            int bestMoves;
            byte[] bestShown;
            RoundRecord[] history;
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
                            RoundLimitSeconds = _options.RoundLimitSeconds,
                            MinMoves = _options.MinBoardMoves,
                            MaxMoves = MatchSettings.DefaultMaxMoves,
                        }.Clamped(_options.MaxMinMoves),
                    };
                    _lobbies[code] = lobby;
                    _log.LogInformation("Lobby {Code} created", code);
                }

                Player? player = FindSeatLocked(lobby, connectionId, id, name);
                if (player == null)
                {
                    player = new Player { Id = id, JoinOrder = ++_nextJoinOrder };
                    lobby.Players[id] = player;
                }
                else if (player.ConnectionId != null && player.ConnectionId != connectionId)
                {
                    // The same player on a new connection (e.g. reconnected before the server noticed
                    // the old one drop): the new connection takes over.
                    displaced = player.ConnectionId;
                    _seatByConnection.Remove(displaced);
                }
                else if (player.ConnectionId == null)
                    _log.LogInformation("Lobby {Code}: {Name} reconnected", code, player.Name);

                // Names identify players to each other (and each client finds itself by name), so keep them unique.
                player.Name = UniqueNameLocked(lobby, player.Id, name);
                player.ConnectionId = connectionId;
                player.DisconnectedAtUtc = null;
                _seatByConnection[connectionId] = new Seat(lobby, player);
                lobby.HostId ??= player.Id;
                name = player.Name;

                state = SnapshotLocked(lobby);
                // Joining mid-match is allowed: the newcomer gets the current board (unless it's still being prepared).
                inRound = lobby.InRound;
                round = lobby.RoundNumber;
                board = lobby.Board;
                phase = lobby.Phase;
                secondsLeft = Math.Max(0, (lobby.TimerEndUtc - UtcNow).TotalSeconds);
                limitLeft = lobby.Phase == RoundPhase.Solving ? SecondsUntil(lobby.LimitEndUtc) : null;
                bestName = lobby.BestPlayerName;
                bestMoves = lobby.BestMoveCount;
                bestShown = ShownBestLocked(lobby);
                history = lobby.History.ToArray();
                countdown = lobby.CountdownEndUtc.HasValue
                    ? Math.Max(0, (lobby.CountdownEndUtc.Value - UtcNow).TotalSeconds)
                    : null;
            }

            if (displaced != null)
                await _hub.Groups.RemoveFromGroupAsync(displaced, code);
            await _hub.Groups.AddToGroupAsync(connectionId, code);
            await _hub.Clients.Group(code).SendAsync(HubMethods.LobbyState, state);

            // Catch the newcomer up on the match's boards so far, the round in progress and/or the
            // countdown to the next one.
            IClientProxy caller = _hub.Clients.Client(connectionId);
            if (history.Length > 0)
                await caller.SendAsync(HubMethods.MatchHistory, history);
            if (inRound)
            {
                await caller.SendAsync(HubMethods.RoundStarted, round, board);
                if (limitLeft.HasValue)
                    await caller.SendAsync(HubMethods.RoundTimeLimit, round, limitLeft.Value);
                if (phase == RoundPhase.TimerRunning)
                {
                    await caller.SendAsync(HubMethods.TimerUpdated, round, secondsLeft);
                    await caller.SendAsync(HubMethods.NewBestSolution, round, bestName, bestMoves, bestShown);
                }
                // Again after the board, which resets the client's view of its own lead / vote.
                await caller.SendAsync(HubMethods.LobbyState, state);
            }
            if (countdown.HasValue)
                await caller.SendAsync(HubMethods.RoundCountdown, countdown.Value);
            return name;
        }

        // This connection's seat if it's re-joining, else the seat with this player id, else a
        // disconnected seat with this name (the player restarted the game). Null for a newcomer.
        private Player? FindSeatLocked(Lobby lobby, string connectionId, string id, string name)
        {
            if (_seatByConnection.TryGetValue(connectionId, out Seat? seat) && seat.Lobby == lobby)
                return seat.Player;
            if (lobby.Players.TryGetValue(id, out Player? player))
                return player;

            Player? byName = lobby.Players.Values.FirstOrDefault(p =>
                !p.Connected && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                // Re-key the seat under the new id (the host / best references follow it).
                lobby.Players.Remove(byName.Id);
                if (lobby.HostId == byName.Id) lobby.HostId = id;
                if (lobby.BestPlayerId == byName.Id) lobby.BestPlayerId = id;
                byName.Id = id;
                lobby.Players[id] = byName;
            }
            return byName;
        }

        private static string UniqueNameLocked(Lobby lobby, string playerId, string name)
        {
            bool Taken(string candidate) => lobby.Players.Values.Any(p =>
                p.Id != playerId && string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase));

            string unique = name;
            for (int n = 2; Taken(unique); n++)
                unique = $"{name} {n}";
            return unique;
        }

        /// <summary>Leaving on purpose: the seat is freed at once.</summary>
        public async Task LeaveAsync(string connectionId)
        {
            Seat? seat;
            LobbyState? state;
            bool endRound;
            int round;
            lock (_gate)
            {
                if (!_seatByConnection.Remove(connectionId, out seat))
                    return;
                state = RemoveSeatLocked(seat.Lobby, seat.Player, out endRound, out round);
            }

            await _hub.Groups.RemoveFromGroupAsync(connectionId, seat.Lobby.Code);
            await AfterSeatChangeAsync(seat.Lobby, state, endRound, round);
        }

        /// <summary>
        /// The connection dropped: the seat stays (shown as disconnected, not waited on in votes) for
        /// <see cref="GameOptions.ReconnectGraceSeconds"/>, then it's freed as if they'd left.
        /// </summary>
        public async Task DisconnectAsync(string connectionId)
        {
            Seat? seat;
            LobbyState state;
            bool endRound;
            int round;
            DateTime stamp;
            lock (_gate)
            {
                if (!_seatByConnection.Remove(connectionId, out seat) || seat.Player.ConnectionId != connectionId)
                    return;
                Lobby lobby = seat.Lobby;
                stamp = UtcNow;
                seat.Player.ConnectionId = null;
                seat.Player.DisconnectedAtUtc = stamp;
                _log.LogInformation("Lobby {Code}: {Name} disconnected", lobby.Code, seat.Player.Name);
                state = SnapshotLocked(lobby);
                round = lobby.RoundNumber;
                // The one player everyone was waiting on may have just dropped.
                endRound = CheckAllOthersDoneLocked(lobby);
            }

            await AfterSeatChangeAsync(seat.Lobby, state, endRound, round);
            RunInBackground(() => ExpireSeatAsync(seat.Lobby, seat.Player, stamp));
        }

        private async Task ExpireSeatAsync(Lobby lobby, Player player, DateTime disconnectedAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectGraceSeconds), _time);
            LobbyState? state;
            bool endRound;
            int round;
            lock (_gate)
            {
                // Back already (or gone, or dropped again later, which has its own expiry).
                if (player.DisconnectedAtUtc != disconnectedAt || !lobby.Players.TryGetValue(player.Id, out Player? seat)
                    || seat != player)
                    return;
                _log.LogInformation("Lobby {Code}: {Name} didn't reconnect", lobby.Code, player.Name);
                state = RemoveSeatLocked(lobby, player, out endRound, out round);
            }
            await AfterSeatChangeAsync(lobby, state, endRound, round);
        }

        /// <returns>The new state to broadcast, or null if the lobby closed.</returns>
        private LobbyState? RemoveSeatLocked(Lobby lobby, Player player, out bool endRound, out int round)
        {
            endRound = false;
            round = lobby.RoundNumber;
            lobby.Players.Remove(player.Id);
            if (lobby.Players.Count == 0)
            {
                lobby.Closed = true;
                lobby.ReplaySkip?.Cancel();
                _lobbies.Remove(lobby.Code);
                _log.LogInformation("Lobby {Code} closed", lobby.Code);
                return null;
            }

            // The host passes to whoever has been here longest.
            if (lobby.HostId == player.Id)
                lobby.HostId = lobby.Players.Values.MinBy(p => p.JoinOrder)!.Id;
            endRound = CheckAllOthersDoneLocked(lobby);
            return SnapshotLocked(lobby);
        }

        private async Task AfterSeatChangeAsync(Lobby lobby, LobbyState? state, bool endRound, int round)
        {
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
                lobby.History.Clear();
                lobby.OwnBests.Clear();
                // No board yet: solutions and votes are refused until it's dealt.
                lobby.Round = null;
                lobby.Phase = RoundPhase.Intermission;
                ClearBestLocked(lobby);
                foreach (Player player in lobby.Players.Values)
                {
                    player.Score = 0;
                    player.Done = false;
                }
                lobby.CountdownEndUtc = UtcNow.AddSeconds(_options.CountdownSeconds);
                state = SnapshotLocked(lobby);
                _log.LogInformation("Lobby {Code}: match started, first to {Points}", lobby.Code, lobby.Settings.PointsToWin);
            }

            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.LobbyState, state);
            await group.SendAsync(HubMethods.RoundCountdown, _options.CountdownSeconds);

            // The board is generated during the countdown (a slow solve just stretches the wait). Not
            // awaited, so the host's StartMatch call returns straight away.
            MatchSettings settings = lobby.Settings;
            Task<PreparedRound> prepare = Task.Run(() => PrepareRound(settings.MinMoves, settings.MaxMoves));
            RunInBackground(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CountdownSeconds), _time);
                PreparedRound prepared = await prepare;
                await DealRoundAsync(lobby, prepared, () => lobby.MatchId == matchId);
            });
        }

        /// <summary>Deals <paramref name="prepared"/> unless the lobby closed or <paramref name="stillWanted"/> (checked under the lock) is false.</summary>
        private async Task DealRoundAsync(Lobby lobby, PreparedRound prepared, Func<bool> stillWanted)
        {
            int round;
            byte[] board;
            LobbyState state;
            double? limit;
            lock (_gate)
            {
                if (lobby.Closed || !stillWanted())
                    return;
                StartNewRoundLocked(lobby, prepared, UtcNow);
                round = lobby.RoundNumber;
                board = lobby.Board;
                state = SnapshotLocked(lobby);
                limit = SecondsUntil(lobby.LimitEndUtc);
            }
            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.RoundStarted, round, board);
            if (limit.HasValue)
            {
                await group.SendAsync(HubMethods.RoundTimeLimit, round, limit.Value);
                RunInBackground(() => EndRoundAfterLimitAsync(lobby, round));
            }
            await group.SendAsync(HubMethods.LobbyState, state);
        }

        private double? SecondsUntil(DateTime? utc) =>
            utc.HasValue ? Math.Max(0, (utc.Value - UtcNow).TotalSeconds) : null;

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
                lobby.Settings = (settings ?? lobby.Settings).Clamped(_options.MaxMinMoves);
                state = SnapshotLocked(lobby);
            }
            await _hub.Clients.Group(lobby.Code).SendAsync(HubMethods.LobbyState, state);
        }

        private Lobby HostLobbyLocked(string connectionId, string action)
        {
            if (!_seatByConnection.TryGetValue(connectionId, out Seat? seat))
                throw new HubException("You are not in a lobby.");
            if (seat.Lobby.HostId != seat.Player.Id)
                throw new HubException($"Only the host can {action}.");
            return seat.Lobby;
        }

        /// <summary>
        /// A player votes (or un-votes). During a round: to end it early - when every player except the
        /// leader has voted, it ends now (skipped with no point if nobody solved it, otherwise as if the
        /// timer ran out). After a round: to skip the replays - when every player, the winner too, has voted, the
        /// next board (or the match results) comes straight away (a lone player skips with their own vote).
        /// </summary>
        public async Task SetDoneAsync(string connectionId, int roundNumber, bool done)
        {
            Lobby lobby;
            LobbyState state;
            bool endRound;
            lock (_gate)
            {
                if (!_seatByConnection.TryGetValue(connectionId, out Seat? seat) || !seat.Lobby.InRound
                    || seat.Lobby.RoundNumber != roundNumber)
                    return;
                lobby = seat.Lobby;
                // The leader isn't part of the vote to end the round early; the replays are everyone's to skip.
                if (seat.Player.Id == lobby.BestPlayerId && lobby.Phase != RoundPhase.Intermission)
                    return;

                seat.Player.Done = done;
                state = SnapshotLocked(lobby);
                endRound = CheckAllOthersDoneLocked(lobby);
            }

            await _hub.Clients.Group(lobby.Code).SendAsync(HubMethods.LobbyState, state);
            // Not awaited: ending a round includes the replay and countdown delays.
            if (endRound)
                RunInBackground(() => EndRoundAsync(lobby, roundNumber));
        }

        /// <summary>
        /// True when at least one player is connected and every connected voter has voted: during a round everyone
        /// but the current leader, after it (skipping the replays) everyone, the winner too. Disconnected players
        /// aren't waited on.
        /// </summary>
        private static bool AllOthersDoneLocked(Lobby lobby)
        {
            string? excluded = lobby.Phase == RoundPhase.Intermission ? null : lobby.BestPlayerId;
            return lobby.Players.Values.Any(p => p.Connected)
                && lobby.Players.Values.Where(p => p.Connected && p.Id != excluded).All(p => p.Done);
        }

        /// <summary>
        /// Acts on a completed vote: after a round it skips the replay (here); during one it returns
        /// true so the caller ends the round once it has released the lock.
        /// </summary>
        private static bool CheckAllOthersDoneLocked(Lobby lobby)
        {
            if (!lobby.InRound || !AllOthersDoneLocked(lobby))
                return false;
            if (lobby.Phase != RoundPhase.Intermission)
                return true;
            lobby.ReplaySkip?.Cancel();
            return false;
        }

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
            byte[] bestShown = Array.Empty<byte>();

            lock (_gate)
            {
                rejection = TryAcceptLocked(connectionId, roundNumber, moveData, out lobby, out timerStarted,
                    out secondsLeft, out playerName, out moveCount);
                if (rejection == null)
                {
                    state = SnapshotLocked(lobby!);
                    bestShown = ShownBestLocked(lobby!);
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
            await group.SendAsync(HubMethods.NewBestSolution, roundNumber, playerName, moveCount, bestShown);
            await group.SendAsync(HubMethods.LobbyState, state);
            if (endRound)
                RunInBackground(() => EndRoundAsync(lobby, roundNumber, endedBySolution: true));
        }

        /// <summary>
        /// A valid solution that doesn't beat the best: kept only as the player's own best for the match
        /// history. Invalid or late ones are ignored (the client only sends solutions it checked).
        /// </summary>
        public Task RecordAsync(string connectionId, int roundNumber, byte[] moveData)
        {
            lock (_gate)
                ValidateLocked(connectionId, roundNumber, moveData, out _, out _, out _);
            return Task.CompletedTask;
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

            string? invalid = ValidateLocked(connectionId, roundNumber, moveData, out lobby, out Seat? seat, out Move[] moves);
            if (invalid != null)
                return invalid;

            if (lobby!.Phase == RoundPhase.TimerRunning && moves.Length >= lobby.BestMoveCount)
                return $"Not better than the current best ({lobby.BestMoveCount} moves).";

            DateTime now = UtcNow;
            if (lobby.Phase == RoundPhase.Solving)
            {
                lobby.Phase = RoundPhase.TimerRunning;
                lobby.LimitEndUtc = null; // the normal timer takes over, even past the limit
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

            Player player = seat!.Player;
            playerName = player.Name;
            moveCount = moves.Length;
            // The leader isn't part of the vote; drop any "skip" they pressed before solving it.
            player.Done = false;
            lobby.BestPlayerId = player.Id;
            lobby.BestPlayerName = playerName;
            lobby.BestMoveCount = moveCount;
            lobby.BestMoves = MoveCodec.Encode(moves);
            return null;
        }

        /// <summary>
        /// Checks a solution for the current board and keeps it as the player's own best (match history) if it's
        /// their shortest yet. Returns null if valid, else why not.
        /// </summary>
        private string? ValidateLocked(string connectionId, int roundNumber, byte[] moveData, out Lobby? lobby,
            out Seat? seat, out Move[] moves)
        {
            lobby = null;
            moves = Array.Empty<Move>();
            if (!_seatByConnection.TryGetValue(connectionId, out seat))
                return "You are not in a lobby.";
            lobby = seat.Lobby;
            if (!lobby.InRound || roundNumber != lobby.RoundNumber || lobby.Phase == RoundPhase.Intermission)
                return "That round is over.";

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

            RecordOwnBestLocked(lobby, seat.Player, moves);
            return null;
        }

        private static void RecordOwnBestLocked(Lobby lobby, Player player, Move[] moves)
        {
            OwnBest? own = lobby.OwnBests.Find(b => b.PlayerId == player.Id);
            if (own != null && own.Length <= moves.Length)
                return;
            if (own == null)
                lobby.OwnBests.Add(own = new OwnBest { PlayerId = player.Id });
            own.Name = player.Name;
            own.Moves = MoveCodec.Encode(moves);
            own.Length = moves.Length;
            own.Found = lobby.NextFound++;
        }

        // The best's moves for everyone, if the lobby shows them (MatchSettings.ShowBestSolution).
        private static byte[] ShownBestLocked(Lobby lobby) =>
            lobby.Settings.ShowBestSolution ? lobby.BestMoves : Array.Empty<byte>();

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
                    wait = lobby.TimerEndUtc - UtcNow;
                }
                if (wait <= TimeSpan.Zero)
                    break;
                await Task.Delay(wait, _time);
            }

            await EndRoundAsync(lobby, roundNumber);
        }

        /// <summary>Ends the board unsolved if nobody has solved it when its time limit runs out.</summary>
        private async Task EndRoundAfterLimitAsync(Lobby lobby, int roundNumber)
        {
            TimeSpan wait;
            lock (_gate)
            {
                if (lobby.LimitEndUtc == null)
                    return;
                wait = lobby.LimitEndUtc.Value - UtcNow;
            }
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, _time);
            await EndRoundAsync(lobby, roundNumber, outOfTime: true);
        }

        /// <summary>
        /// Ends the round (timer ran out, everyone else done, or the shortest solution was found), sends
        /// the result and replays, then either counts down and deals the next board - prepared while the
        /// replays play - or, if that point won the match, shows the results. Safe to call more than
        /// once; only the first call does anything.
        /// </summary>
        /// <param name="outOfTime">The time limit ran out: only ends the round if it's still unsolved.</param>
        private async Task EndRoundAsync(Lobby lobby, int roundNumber, bool outOfTime = false, bool endedBySolution = false)
        {
            RoundResult result;
            LobbyState state;
            CancellationTokenSource skip;
            string? matchWinner = null;
            lock (_gate)
            {
                if (lobby.Closed || lobby.RoundNumber != roundNumber || lobby.Phase == RoundPhase.Intermission)
                    return;
                if (outOfTime && lobby.Phase != RoundPhase.Solving)
                    return; // solved just in time: the normal timer runs

                lobby.Phase = RoundPhase.Intermission;
                lobby.LimitEndUtc = null;
                // The point goes to the best solver if they still have a seat (even while reconnecting).
                if (lobby.BestPlayerId != null && lobby.Players.TryGetValue(lobby.BestPlayerId, out Player? winner))
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
                    // Sped up only when it follows the winner's replay; on its own it's the first
                    // solution anyone sees, so it plays at the normal pace.
                    ShortestReplayStepSeconds = lobby.BestMoves.Length > 0
                        ? _options.ShortestReplayStepSeconds
                        : _options.ReplayStepSeconds,
                    ReplayPauseSeconds = _options.ReplayPauseSeconds,
                    SolvedPauseSeconds = endedBySolution ? _options.SolvedPauseSeconds : 0,
                    MatchWinnerName = matchWinner,
                    OutOfTime = outOfTime,
                    Record = new RoundRecord
                    {
                        MatchRound = lobby.MatchRound,
                        Board = lobby.Board,
                        WinnerName = lobby.BestPlayerName,
                        Solutions = lobby.OwnBests
                            .OrderBy(b => b.Length).ThenBy(b => b.Found)
                            .Select(b => new PlayerSolution { Name = b.Name, Moves = b.Moves })
                            .ToArray(),
                        ShortestMoves = lobby.Shortest != null ? MoveCodec.Encode(lobby.Shortest) : Array.Empty<byte>(),
                    },
                };
                lobby.History.Add(result.Record);
                skip = new CancellationTokenSource();
                lobby.ReplaySkip = skip;
                state = SnapshotLocked(lobby);
            }

            // Prepare the next board while everyone watches the replays (unless the match is over).
            MatchSettings settings = lobby.Settings; // locked during a match
            Task<PreparedRound>? next = matchWinner == null ? Task.Run(() => PrepareRound(settings.MinMoves, settings.MaxMoves)) : null;

            IClientProxy group = _hub.Clients.Group(lobby.Code);
            await group.SendAsync(HubMethods.RoundEnded, result);
            await group.SendAsync(HubMethods.LobbyState, state);

            try
            {
                await Task.Delay(ReplayDuration(result) + TimeSpan.FromSeconds(_options.AfterReplaySeconds), _time, skip.Token);
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
                    lobby.CountdownEndUtc = UtcNow.AddSeconds(_options.CountdownSeconds);
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
            await Task.Delay(TimeSpan.FromSeconds(_options.CountdownSeconds), _time);
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
            double seconds = result.SolvedPauseSeconds;
            if (winner > 0)
                seconds += result.ReplayPauseSeconds + winner * result.ReplayStepSeconds;
            if (shortest > 0 && (winner == 0 || winner > shortest))
                seconds += result.ReplayPauseSeconds + shortest * result.ShortestReplayStepSeconds;
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// A board whose shortest solution has <paramref name="minMoves"/> to <paramref name="maxMoves"/> moves
        /// (<see cref="BoardSearch"/>, shared with practice boards). Once <see cref="GameOptions.BoardSearchSeconds"/> is
        /// up, the longest board found that isn't past the maximum is dealt (if none was solved in time yet, the solver's time limit is dropped), so every dealt
        /// board has a known shortest solution (instant end on it, its replay, the history). Runs outside the lock.
        /// </summary>
        private PreparedRound PrepareRound(int minMoves, int maxMoves)
        {
            int seed;
            lock (_gate)
                seed = _random.Next();
            var search = new BoardSearch(minMoves, new Random(seed), TimeSpan.FromSeconds(_options.SolverTimeLimitSeconds), maxMoves);
            var budget = TimeSpan.FromSeconds(_options.BoardSearchSeconds);

            var clock = Stopwatch.StartNew();
            while (!search.Step(noTimeLimit: clock.Elapsed >= budget && search.Longest == null))
            {
                if (clock.Elapsed >= budget && search.Longest != null)
                {
                    _log.LogInformation("Board ready: shortest {Moves} moves, wanted {Min}-{Max} (out of time; {Solves} solves, {Ms} ms)",
                        search.LongestMoves.Length, minMoves, maxMoves, search.Solves, clock.ElapsedMilliseconds);
                    return new PreparedRound(search.Longest, search.LongestMoves);
                }
            }
            _log.LogInformation("Board ready: shortest {Moves} moves ({Solves} solves, {Ms} ms)",
                search.LongestMoves.Length, search.Solves, clock.ElapsedMilliseconds);
            return new PreparedRound(search.Longest, search.LongestMoves);
        }

        private static void StartNewRoundLocked(Lobby lobby, PreparedRound prepared, DateTime now)
        {
            lobby.RoundNumber++;
            lobby.MatchRound++;
            lobby.Round = prepared.Round;
            lobby.Shortest = prepared.Shortest;
            lobby.Board = BoardCodec.Encode(prepared.Round);
            lobby.Phase = RoundPhase.Solving;
            int limit = lobby.Settings.RoundLimitSeconds;
            lobby.LimitEndUtc = limit > 0 ? now.AddSeconds(limit) : null;
            lobby.CountdownEndUtc = null;
            ClearBestLocked(lobby);
            lobby.OwnBests.Clear();
            foreach (Player player in lobby.Players.Values)
                player.Done = false;
        }

        private static void ClearBestLocked(Lobby lobby)
        {
            lobby.BestPlayerId = null;
            lobby.BestPlayerName = null;
            lobby.BestMoveCount = 0;
            lobby.BestMoves = Array.Empty<byte>();
        }

        private static LobbyState SnapshotLocked(Lobby lobby)
        {
            return new LobbyState
            {
                Code = lobby.Code,
                Host = lobby.HostId != null && lobby.Players.TryGetValue(lobby.HostId, out Player? host)
                    ? host.Name
                    : null,
                Phase = lobby.Match,
                Settings = lobby.Settings,
                MatchRound = lobby.MatchRound,
                MatchWinner = lobby.MatchWinner,
                RoundOver = lobby.InRound && lobby.Phase == RoundPhase.Intermission,
                Players = lobby.Players.Values
                    .OrderBy(p => p.JoinOrder)
                    .Select(p => new PlayerInfo
                    {
                        Name = p.Name,
                        Score = p.Score,
                        Done = p.Done,
                        Leading = p.Id == lobby.BestPlayerId,
                        Connected = p.Connected,
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
