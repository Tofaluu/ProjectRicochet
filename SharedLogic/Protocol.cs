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

        // Server -> client

        /// <summary>(LobbyState state). Sent to the whole lobby whenever players or scores change.</summary>
        public const string LobbyState = "LobbyState";

        /// <summary>(int roundNumber, byte[] board)</summary>
        public const string RoundStarted = "RoundStarted";

        /// <summary>(int roundNumber, double secondsRemaining). The first valid solution starts the countdown.</summary>
        public const string TimerStarted = "TimerStarted";

        /// <summary>(int roundNumber, string playerName, int moveCount)</summary>
        public const string NewBestSolution = "NewBestSolution";

        /// <summary>(string reason). Sent only to the player whose submission was rejected.</summary>
        public const string SolutionRejected = "SolutionRejected";

        /// <summary>(int roundNumber, string winnerName, int moveCount). winnerName is null if the winner left.</summary>
        public const string RoundEnded = "RoundEnded";
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public int Score { get; set; }
    }

    public class LobbyState
    {
        public string Code { get; set; }
        public PlayerInfo[] Players { get; set; }
    }
}
