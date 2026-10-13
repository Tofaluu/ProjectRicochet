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
        public const int ProtocolVersion = 5;

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

        /// <summary>(int roundNumber, byte[] moves)</summary>
        public const string SubmitSolution = "SubmitSolution";

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
        /// countdown and whenever a better solution extends it.
        /// </summary>
        public const string TimerUpdated = "TimerUpdated";

        /// <summary>(int roundNumber, string playerName, int moveCount)</summary>
        public const string NewBestSolution = "NewBestSolution";

        /// <summary>(string reason). Sent only to the player whose submission was rejected.</summary>
        public const string SolutionRejected = "SolutionRejected";

        /// <summary>
        /// (RoundResult result). The round ended: timer ran out, everyone else was done, or someone
        /// matched the shortest possible solution. The next round's countdown starts after the replays
        /// (or sooner if everyone but the winner votes to skip).
        /// </summary>
        public const string RoundEnded = "RoundEnded";
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

        /// <summary>
        /// Set when this round's point won the match. The lobby switches to
        /// <see cref="MatchPhase.Finished"/> once the replays are over.
        /// </summary>
        public string MatchWinnerName { get; set; }
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

        public GameMode Mode { get; set; }
        public int PointsToWin { get; set; }

        /// <summary>Timer started by the first valid solution of a round.</summary>
        public int FirstTimerSeconds { get; set; }

        /// <summary>Each better solution tops the timer up to at least this much.</summary>
        public int ExtensionSeconds { get; set; }

        public MatchSettings Clamped()
        {
            return new MatchSettings
            {
                Mode = GameMode.Classic, // the only mode so far
                PointsToWin = Clamp(PointsToWin, MinPointsToWin, MaxPointsToWin),
                FirstTimerSeconds = Clamp(FirstTimerSeconds, MinTimerSeconds, MaxTimerSeconds),
                ExtensionSeconds = Clamp(ExtensionSeconds, MinTimerSeconds, MaxTimerSeconds),
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
    }
}
