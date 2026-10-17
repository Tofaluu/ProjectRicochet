namespace BackendServer.Services
{
    /// <summary>Daily puzzle settings (config section <c>Daily</c>).</summary>
    public class DailyOptions
    {
        /// <summary>Puzzle #1's day ("yyyy-MM-dd", in <see cref="TimeZone"/>); each day after is the next number.</summary>
        public string LaunchDate { get; set; } = "2026-10-17";

        /// <summary>The puzzle changes at midnight here (IANA id).</summary>
        public string TimeZone { get; set; } = "America/Toronto";

        /// <summary>How many players <see cref="SharedLogic.HubMethods.GetDailyLeaderboard"/> lists.</summary>
        public int LeaderboardSize { get; set; } = 50;

        /// <summary>Time to find a board of the day's length; after it, the longest found that isn't too long.</summary>
        public double SearchSeconds { get; set; } = 60;

        /// <summary>Time the solver gets per candidate board (boards it gives up on are skipped).</summary>
        public double SolverTimeLimitSeconds { get; set; } = 5;

        public int MaxSolutionMoves { get; set; } = 64;

        /// <summary>For testing (e.g. a dev server): every puzzle's length range instead of the weekday's.</summary>
        public int? MinMoves { get; set; }
        public int? MaxMoves { get; set; }

        /// <summary>Seeds board generation (for tests); null picks a random seed per puzzle.</summary>
        public int? Seed { get; set; }
    }
}
