namespace SharedLogic
{
    /// <summary>
    /// SignalR method names and message types shared by the server hub and the Unity client,
    /// so both sides always agree on the wire contract.
    /// </summary>
    /// <remarks>
    /// Boards travel as <see cref="BoardCodec"/> bytes and solutions as <see cref="MoveCodec"/>
    /// bytes (base64 in JSON), never as <see cref="Move"/> structs: System.Text.Json ignores
    /// fields by default, so a <see cref="Move"/> would arrive as all-default values.
    /// </remarks>
    public static class HubMethods
    {
        public const string HubPath = "/gamehub";

        /// <summary>
        /// Bump whenever the wire contract changes (method names, arguments, DTOs, codecs). The server
        /// refuses joins from any other version with a clear "update your game" message.
        /// </summary>
        public const int ProtocolVersion = 9;

        // Client -> server

        /// <summary>
        /// (string playerName, string lobbyCode, int protocolVersion, string playerId) returns string: the
        /// name as shown in the lobby, made unique (e.g. "Sam 2"). Clients use it to find themselves in
        /// <see cref="LobbyState"/>. Fails if protocolVersion != <see cref="ProtocolVersion"/>.
        /// playerId is a random id the client keeps for its whole run: joining again with it (e.g. after
        /// an automatic reconnect) takes back the player's seat and score. A dropped player's seat is
        /// kept for a while; joining with the same name also reclaims it (e.g. after restarting the game).
        /// </summary>
        public const string JoinLobby = "JoinLobby";

        /// <summary>() Leave the current lobby; same as disconnecting, but the connection stays open.</summary>
        public const string LeaveLobby = "LeaveLobby";

        /// <summary>
        /// () Host only, when no match is running: resets scores and starts a match with the lobby's
        /// <see cref="MatchSettings"/>. Also "Play again" after a match.
        /// </summary>
        public const string StartMatch = "StartMatch";

        /// <summary>(MatchSettings settings) Host only, between matches. Out-of-range values are clamped.</summary>
        public const string UpdateSettings = "UpdateSettings";

        /// <summary>
        /// (int roundNumber, byte[] moves). An attempt at the lead: accepted only if shorter than the current
        /// best. Either way a valid solution is kept as the player's own best for the match history.
        /// </summary>
        public const string SubmitSolution = "SubmitSolution";

        /// <summary>
        /// (int roundNumber, byte[] moves). A valid solution that doesn't beat the current best, kept only as
        /// the player's own best for the match history (<see cref="RoundRecord.Solutions"/>); no reply.
        /// </summary>
        public const string RecordSolution = "RecordSolution";

        /// <summary>
        /// (int roundNumber, bool done). Vote to end the round early; toggleable. The current leader
        /// can't vote and isn't waited on. When every other player is done the round ends now:
        /// skipped (no winner, no point) if nobody has solved it, otherwise as if the timer ran out.
        /// Re-checked whenever the lead changes, so taking the lead can end the round immediately.
        /// After the round ends the same vote means "skip the replay": once everyone except the winner
        /// has voted, the next board comes straight away.
        /// </summary>
        public const string SetDone = "SetDone";

        // Server -> client

        /// <summary>
        /// (LobbyState state). Sent to the whole lobby whenever players, scores, the host, the settings
        /// or the match phase change.
        /// </summary>
        public const string LobbyState = "LobbyState";

        /// <summary>
        /// (double seconds). The next board (the match's first, or the one after a round's replays) is
        /// dealt in this many seconds. Also sent to players who join during the countdown.
        /// </summary>
        public const string RoundCountdown = "RoundCountdown";

        /// <summary>(int roundNumber, byte[] board)</summary>
        public const string RoundStarted = "RoundStarted";

        /// <summary>
        /// (int roundNumber, double secondsRemaining). Sent when the first valid solution starts the
        /// countdown and whenever a better solution extends it. Replaces the round's time limit.
        /// </summary>
        public const string TimerUpdated = "TimerUpdated";

        /// <summary>
        /// (int roundNumber, double secondsRemaining). The round's time limit
        /// (<see cref="MatchSettings.RoundLimitSeconds"/>), sent after <see cref="RoundStarted"/> while
        /// nobody has solved the board. If it runs out first, the board ends unsolved (no point). The
        /// first solution switches to the normal timer (<see cref="TimerUpdated"/>) instead.
        /// </summary>
        public const string RoundTimeLimit = "RoundTimeLimit";

        /// <summary>
        /// (int roundNumber, string playerName, int moveCount, byte[] moves). moves is empty unless the lobby's
        /// <see cref="MatchSettings.ShowBestSolution"/> is on.
        /// </summary>
        public const string NewBestSolution = "NewBestSolution";

        /// <summary>(string reason). Sent only to the player whose submission was rejected.</summary>
        public const string SolutionRejected = "SolutionRejected";

        /// <summary>
        /// (RoundResult result). The round ended: timer ran out, everyone else was done, or someone
        /// matched the shortest possible solution. The next round's countdown starts after the replays
        /// (or sooner if everyone but the winner votes to skip).
        /// </summary>
        public const string RoundEnded = "RoundEnded";

        /// <summary>
        /// (RoundRecord[] rounds). The current (or just finished) match's boards so far, sent to a player who
        /// joins or rejoins mid-match or on the results; later boards arrive with each <see cref="RoundEnded"/>.
        /// </summary>
        public const string MatchHistory = "MatchHistory";
    }

    /// <summary>One player's best solution for a board (<see cref="MoveCodec"/> bytes).</summary>
    public class PlayerSolution
    {
        public string Name { get; set; }
        public byte[] Moves { get; set; }
    }

    /// <summary>A finished board of the match, for the match history: the board and everyone's solutions.</summary>
    public class RoundRecord
    {
        /// <summary>1-based within the match.</summary>
        public int MatchRound { get; set; }

        /// <summary><see cref="BoardCodec"/> bytes.</summary>
        public byte[] Board { get; set; }

        /// <summary>Who took the point; null if nobody solved it.</summary>
        public string WinnerName { get; set; }

        /// <summary>Each player's best solution, shortest first (equal lengths: whoever found it first).</summary>
        public PlayerSolution[] Solutions { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes of a shortest solution; empty if the solver gave up on this board.</summary>
        public byte[] ShortestMoves { get; set; }
    }

    /// <summary>
    /// How a round ended, and what to replay. Clients replay the winner's solution, then the shortest
    /// one if it's shorter (or only the shortest if nobody solved it), each from the round's start.
    /// </summary>
    public class RoundResult
    {
        public int RoundNumber { get; set; }

        /// <summary>The best solver (they only score if still in the lobby); null if nobody solved it.</summary>
        public string WinnerName { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes; empty if nobody solved it.</summary>
        public byte[] WinnerMoves { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes of a shortest solution; empty if the solver gave up on this board.</summary>
        public byte[] ShortestMoves { get; set; }

        public double ReplayStepSeconds { get; set; }
        public double ShortestReplayStepSeconds { get; set; }

        /// <summary>Pause on the starting position before each replay.</summary>
        public double ReplayPauseSeconds { get; set; }

        /// <summary>Nobody solved the board before the round's time limit ran out.</summary>
        public bool OutOfTime { get; set; }

        /// <summary>
        /// Set when this round's point won the match. The lobby switches to
        /// <see cref="MatchPhase.Finished"/> once the replays are over.
        /// </summary>
        public string MatchWinnerName { get; set; }

        /// <summary>This board for the match history, with everyone's solutions.</summary>
        public RoundRecord Record { get; set; }
    }

    public enum GameMode
    {
        /// <summary>Shortest solution when the timer runs out wins the round.</summary>
        Classic = 0,
    }

    public enum MatchPhase
    {
        /// <summary>Lobby just created: waiting for the host to start the first match.</summary>
        Waiting = 0,
        /// <summary>Rounds are being played.</summary>
        Playing = 1,
        /// <summary>Someone reached the points target; results until the host starts again.</summary>
        Finished = 2,
    }

    /// <summary>Chosen by the host between matches.</summary>
    public class MatchSettings
    {
        public const int MinPointsToWin = 1, MaxPointsToWin = 50;
        public const int MinTimerSeconds = 10, MaxTimerSeconds = 600;
        public const int MinRoundLimitSeconds = 60, MaxRoundLimitSeconds = 3600;
        // Boards of 16+ moves are too rare to find while players wait (about 1 in 2400 random boards).
        public const int MinMinMoves = 2, MaxMinMoves = 15, DefaultMinMoves = 3;

        public GameMode Mode { get; set; }
        public int PointsToWin { get; set; }

        /// <summary>Timer started by the first valid solution of a round.</summary>
        public int FirstTimerSeconds { get; set; }

        /// <summary>Each better solution tops the timer up to at least this much.</summary>
        public int ExtensionSeconds { get; set; }

        /// <summary>
        /// Time to find a first solution before the board ends unsolved; 0 = no limit. Once someone
        /// solves it the normal timer takes over, so a late solution still gets its full countdown.
        /// </summary>
        public int RoundLimitSeconds { get; set; }

        /// <summary>
        /// When someone takes the lead, everyone gets the moves too (<see cref="HubMethods.NewBestSolution"/>)
        /// and can watch them on their own board. Only shorter solutions count, so copying gains nothing.
        /// </summary>
        public bool ShowBestSolution { get; set; }

        /// <summary>
        /// Boards are dealt with a shortest solution of at least this many moves. If the server can't find
        /// one in time it deals the longest it found.
        /// </summary>
        public int MinMoves { get; set; }

        /// <param name="maxMinMoves">The server can allow longer boards (a dev server testing them); defaults to <see cref="MaxMinMoves"/>.</param>
        public MatchSettings Clamped(int maxMinMoves = MaxMinMoves)
        {
            return new MatchSettings
            {
                Mode = GameMode.Classic, // the only mode so far
                PointsToWin = Clamp(PointsToWin, MinPointsToWin, MaxPointsToWin),
                FirstTimerSeconds = Clamp(FirstTimerSeconds, MinTimerSeconds, MaxTimerSeconds),
                ExtensionSeconds = Clamp(ExtensionSeconds, MinTimerSeconds, MaxTimerSeconds),
                RoundLimitSeconds = RoundLimitSeconds <= 0 ? 0 : Clamp(RoundLimitSeconds, MinRoundLimitSeconds, MaxRoundLimitSeconds),
                ShowBestSolution = ShowBestSolution,
                MinMoves = Clamp(MinMoves, MinMinMoves, maxMinMoves),
            };
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public int Score { get; set; }

        /// <summary>Has voted to end this round early (see <see cref="HubMethods.SetDone"/>).</summary>
        public bool Done { get; set; }

        /// <summary>Holds the current best solution this round.</summary>
        public bool Leading { get; set; }

        /// <summary>False while their connection is lost; the seat is kept for them to come back.</summary>
        public bool Connected { get; set; }
    }

    public class LobbyState
    {
        public string Code { get; set; }

        /// <summary>In join order.</summary>
        public PlayerInfo[] Players { get; set; }

        /// <summary>Name of the host, who starts matches and picks the settings. Passes to the next player if they leave.</summary>
        public string Host { get; set; }

        public MatchPhase Phase { get; set; }
        public MatchSettings Settings { get; set; }

        /// <summary>Round number within the current match (1-based; 0 before the first board).</summary>
        public int MatchRound { get; set; }

        /// <summary>Set while <see cref="Phase"/> is <see cref="MatchPhase.Finished"/>.</summary>
        public string MatchWinner { get; set; }

        /// <summary>
        /// True from <see cref="HubMethods.RoundEnded"/> until the next board (replays, then the countdown):
        /// the current board can't be solved any more. Tells a player who (re)joins then.
        /// </summary>
        public bool RoundOver { get; set; }
    }
}
