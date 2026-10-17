namespace BackendServer.Services
{
    /// <summary>
    /// Where daily puzzles and results live: PostgreSQL in production (<see cref="EfDailyStore"/>), memory when no
    /// database is configured (local runs, tests: <see cref="InMemoryDailyStore"/>). Rules live in <see cref="DailyService"/>.
    /// </summary>
    public interface IDailyStore
    {
        Task<DailyPuzzleRecord?> GetPuzzleAsync(int number);

        /// <summary>Saves a puzzle unless that number already has one (another server got there first).</summary>
        Task SavePuzzleAsync(DailyPuzzleRecord puzzle);

        /// <summary>
        /// Records the player (creating them, or updating their name) and returns their entry for the puzzle,
        /// creating it with <paramref name="now"/> as the opening time if it's their first look.
        /// </summary>
        Task<DailyEntryRecord> OpenAsync(int number, string guestId, string name, DateTime now);

        /// <summary>The player's entry for the puzzle, if they've opened it.</summary>
        Task<DailyEntryRecord?> GetEntryAsync(int number, string guestId);

        /// <summary>Sets the player's best for the puzzle (the caller has checked it's better).</summary>
        Task SaveBestAsync(int number, string guestId, int moves, double seconds, byte[] solution, DateTime now);

        /// <summary>The best <paramref name="count"/> solvers (fewest moves, then fastest) and how many solved it.</summary>
        Task<(IReadOnlyList<DailyRow> Top, int Players)> GetLeaderboardAsync(int number, int count);

        /// <summary>1 + how many solvers did strictly better (fewer moves, or as few in less time).</summary>
        Task<int> GetRankAsync(int number, int moves, double seconds);

        /// <summary>The puzzle numbers in [<paramref name="from"/>, <paramref name="to"/>] the player has solved.</summary>
        Task<IReadOnlyList<int>> GetSolvedNumbersAsync(string guestId, int from, int to);
    }
}
