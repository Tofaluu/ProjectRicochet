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

    /// <summary>
    /// One player on a past puzzle played after its day (the <c>daily_archive</c> table): kept apart from
    /// <see cref="DailyEntryRecord"/> so it never changes that day's leaderboard or a streak.
    /// </summary>
    public class DailyArchiveRecord
    {
        public int Number { get; set; }
        public string GuestId { get; set; } = "";
        public DateTime OpenedUtc { get; set; }

        /// <summary>0 until solved.</summary>
        public int BestMoves { get; set; }
        public byte[]? BestSolution { get; set; }
        public DateTime? SolvedUtc { get; set; }

        /// <summary>When the player looked at the shortest solution (null = never).</summary>
        public DateTime? SawSolutionUtc { get; set; }
    }

    /// <summary>Which of two results for the same puzzle stays when one guest's results merge into another's.</summary>
    public static class DailyMerge
    {
        /// <summary>Solved beats unsolved, then fewer moves, then faster, then opened earlier.</summary>
        public static bool IsBetter(DailyEntryRecord a, DailyEntryRecord b)
        {
            if ((a.BestMoves > 0) != (b.BestMoves > 0))
                return a.BestMoves > 0;
            if (a.BestMoves != b.BestMoves)
                return a.BestMoves < b.BestMoves;
            if (a.BestSeconds != b.BestSeconds)
                return a.BestSeconds < b.BestSeconds;
            return a.OpenedUtc < b.OpenedUtc;
        }

        /// <summary>The better past play (solved, then fewer moves), keeping the earlier look at the solution of either.</summary>
        public static DailyArchiveRecord Combine(DailyArchiveRecord kept, DailyArchiveRecord other, string guestId)
        {
            bool otherBetter = (other.BestMoves > 0) != (kept.BestMoves > 0) ? other.BestMoves > 0
                : other.BestMoves > 0 && other.BestMoves < kept.BestMoves;
            DailyArchiveRecord best = otherBetter ? other : kept;
            DateTime? saw = kept.SawSolutionUtc == null ? other.SawSolutionUtc
                : other.SawSolutionUtc == null || kept.SawSolutionUtc < other.SawSolutionUtc ? kept.SawSolutionUtc : other.SawSolutionUtc;
            return new DailyArchiveRecord
            {
                Number = best.Number, GuestId = guestId, OpenedUtc = kept.OpenedUtc < other.OpenedUtc ? kept.OpenedUtc : other.OpenedUtc,
                BestMoves = best.BestMoves, BestSolution = best.BestSolution, SolvedUtc = best.SolvedUtc, SawSolutionUtc = saw,
            };
        }
    }

    /// <summary>A leaderboard line.</summary>
    public sealed record DailyRow(string GuestId, string Name, int Moves, double Seconds);

    /// <summary>A puzzle without its board, for the calendar.</summary>
    public sealed record DailySummary(int Number, DateOnly Date, int ShortestLength);

    /// <summary>A solved day of a player who has a streak going, for the streak leaderboard.</summary>
    public sealed record DailySolve(string GuestId, string Name, int Number);
}
