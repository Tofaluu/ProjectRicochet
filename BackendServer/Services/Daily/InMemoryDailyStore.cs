namespace BackendServer.Services
{
    /// <summary>Daily puzzles and results in memory: used when no database is configured (local runs, tests).</summary>
    public sealed class InMemoryDailyStore : IDailyStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, DailyPuzzleRecord> _puzzles = new();
        private readonly Dictionary<string, DailyPlayerRecord> _players = new();
        private readonly Dictionary<(int, string), DailyEntryRecord> _entries = new();

        public Task<DailyPuzzleRecord?> GetPuzzleAsync(int number)
        {
            lock (_gate)
                return Task.FromResult(_puzzles.GetValueOrDefault(number));
        }

        public Task SavePuzzleAsync(DailyPuzzleRecord puzzle)
        {
            lock (_gate)
                _puzzles.TryAdd(puzzle.Number, puzzle);
            return Task.CompletedTask;
        }

        public Task<DailyEntryRecord> OpenAsync(int number, string guestId, string name, DateTime now)
        {
            lock (_gate)
            {
                if (_players.TryGetValue(guestId, out DailyPlayerRecord? player))
                {
                    player.Name = name;
                    player.LastSeenUtc = now;
                }
                else
                {
                    _players[guestId] = new DailyPlayerRecord { GuestId = guestId, Name = name, CreatedUtc = now, LastSeenUtc = now };
                }
                if (!_entries.TryGetValue((number, guestId), out DailyEntryRecord? entry))
                {
                    entry = new DailyEntryRecord { Number = number, GuestId = guestId, OpenedUtc = now };
                    _entries[(number, guestId)] = entry;
                }
                return Task.FromResult(Copy(entry));
            }
        }

        public Task<DailyEntryRecord?> GetEntryAsync(int number, string guestId)
        {
            lock (_gate)
                return Task.FromResult(_entries.TryGetValue((number, guestId), out DailyEntryRecord? e) ? Copy(e) : null);
        }

        public Task SaveBestAsync(int number, string guestId, int moves, double seconds, byte[] solution, DateTime now)
        {
            lock (_gate)
            {
                DailyEntryRecord entry = _entries[(number, guestId)];
                entry.BestMoves = moves;
                entry.BestSeconds = seconds;
                entry.BestSolution = solution;
                entry.SolvedUtc = now;
            }
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<DailyRow> Top, int Players)> GetLeaderboardAsync(int number, int count)
        {
            lock (_gate)
            {
                List<DailyRow> solved = Solved(number).ToList();
                IReadOnlyList<DailyRow> top = solved.Take(count).ToList();
                return Task.FromResult((top, solved.Count));
            }
        }

        public Task<int> GetRankAsync(int number, int moves, double seconds)
        {
            lock (_gate)
                return Task.FromResult(1 + Solved(number).Count(r => r.Moves < moves || (r.Moves == moves && r.Seconds < seconds)));
        }

        public Task<IReadOnlyList<int>> GetSolvedNumbersAsync(string guestId, int from, int to)
        {
            lock (_gate)
            {
                IReadOnlyList<int> numbers = _entries.Values
                    .Where(e => e.GuestId == guestId && e.BestMoves > 0 && e.Number >= from && e.Number <= to)
                    .Select(e => e.Number).OrderBy(n => n).ToList();
                return Task.FromResult(numbers);
            }
        }

        private IEnumerable<DailyRow> Solved(int number) => _entries.Values
            .Where(e => e.Number == number && e.BestMoves > 0)
            .OrderBy(e => e.BestMoves).ThenBy(e => e.BestSeconds)
            .Select(e => new DailyRow(e.GuestId, _players[e.GuestId].Name, e.BestMoves, e.BestSeconds));

        // Callers get a snapshot, like a row read from a database.
        private static DailyEntryRecord Copy(DailyEntryRecord e) => new()
        {
            Number = e.Number, GuestId = e.GuestId, OpenedUtc = e.OpenedUtc, BestMoves = e.BestMoves,
            BestSeconds = e.BestSeconds, BestSolution = e.BestSolution, SolvedUtc = e.SolvedUtc,
        };
    }
}
