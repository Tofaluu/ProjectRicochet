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

        // Client -> server

        /// <summary>(string playerName, string lobbyCode)</summary>
        public const string JoinLobby = "JoinLobby";

        /// <summary>(int roundNumber, byte[] moves)</summary>
        public const string SubmitSolution = "SubmitSolution";

        /// <summary>
        /// (int roundNumber, bool done). Vote to end the round early; toggleable. When every player
        /// is done the round ends now: skipped (no winner, no point) if nobody has solved it,
        /// otherwise as if the timer ran out.
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
        /// (int roundNumber, string winnerName, int moveCount, byte[] winningMoves, double replayStepSeconds).
        /// winnerName is the best solver (they only score if still in the lobby); null with
        /// moveCount 0 and no moves if the board was skipped without a solution. Clients replay
        /// winningMoves (<see cref="MoveCodec"/>) from the round's start, one move per step; the
        /// next round starts after the replay.
        /// </summary>
        public const string RoundEnded = "RoundEnded";
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public int Score { get; set; }

        /// <summary>Has voted to end this round early (see <see cref="HubMethods.SetDone"/>).</summary>
        public bool Done { get; set; }
    }

    public class LobbyState
    {
        public string Code { get; set; }
        public PlayerInfo[] Players { get; set; }
    }
}
