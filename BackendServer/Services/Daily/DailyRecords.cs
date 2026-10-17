namespace BackendServer.Services
{
    /// <summary>A day's puzzle, generated (and solved) ahead of time. Stored as the database's <c>daily_puzzles</c>.</summary>
    public class DailyPuzzleRecord
    {
        public int Number { get; set; }
        public DateOnly Date { get; set; }

        /// <summary><see cref="SharedLogic.BoardCodec"/> bytes.</summary>
        public byte[] Board { get; set; } = Array.Empty<byte>();

        /// <summary><see cref="SharedLogic.MoveCodec"/> bytes of a shortest solution (never sent to players).</summary>
        public byte[] Shortest { get; set; } = Array.Empty<byte>();

        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>A guest player: the device's secret id and the name shown on leaderboards.</summary>
    public class DailyPlayerRecord
    {
        public string GuestId { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    /// <summary>One player on one day's puzzle: when they opened it and their best solution so far.</summary>
    public class DailyEntryRecord
    {
        public int Number { get; set; }
        public string GuestId { get; set; } = "";

        /// <summary>The player's clock starts here.</summary>
        public DateTime OpenedUtc { get; set; }

        /// <summary>0 until solved.</summary>
        public int BestMoves { get; set; }
        public double BestSeconds { get; set; }
        public byte[]? BestSolution { get; set; }
        public DateTime? SolvedUtc { get; set; }
    }

    /// <summary>A leaderboard line.</summary>
    public sealed record DailyRow(string GuestId, string Name, int Moves, double Seconds);
}
