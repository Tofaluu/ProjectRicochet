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
        public const int ProtocolVersion = 2;

        // Client -> server

        /// <summary>
        /// (string playerName, string lobbyCode, int protocolVersion) returns string: the name as shown
        /// in the lobby, made unique (e.g. "Sam 2"). Clients use it to find themselves in
        /// <see cref="LobbyState"/>. Fails if protocolVersion != <see cref="ProtocolVersion"/>.
        /// </summary>
        public const string JoinLobby = "JoinLobby";

        /// <summary>() Leave the current lobby; same as disconnecting, but the connection stays open.</summary>
        public const string LeaveLobby = "LeaveLobby";

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

        /// <summary>(LobbyState state). Sent to the whole lobby whenever players or scores change.</summary>
        public const string LobbyState = "LobbyState";

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
        /// matched the shortest possible solution. The next round starts after the replays (or sooner
        /// if everyone but the winner votes to skip).
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
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public int Score { get; set; }

        /// <summary>Has voted to end this round early (see <see cref="HubMethods.SetDone"/>).</summary>
        public bool Done { get; set; }

        /// <summary>Holds the current best solution this round.</summary>
        public bool Leading { get; set; }
    }

    public class LobbyState
    {
        public string Code { get; set; }
        public PlayerInfo[] Players { get; set; }
    }
}
