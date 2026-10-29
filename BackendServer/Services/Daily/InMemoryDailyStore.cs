namespace BackendServer.Services
{
    /// <summary>Daily puzzles and results in memory: used when no database is configured (local runs, tests).</summary>
    public sealed class InMemoryDailyStore : IDailyStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, DailyPuzzleRecord> _puzzles = new();
        private readonly Dictionary<string, DailyPlayerRecord> _players = new();
        private readonly Dictionary<(int, string), DailyEntryRecord> _entries = new();
        private readonly Dictionary<(int, string), DailyArchiveRecord> _archive = new();

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

        public Task<IReadOnlyList<DailySummary>> GetSummariesAsync(int to)
        {
            lock (_gate)
            {
                IReadOnlyList<DailySummary> list = _puzzles.Values.Where(p => p.Number <= to).OrderBy(p => p.Number)
                    .Select(p => new DailySummary(p.Number, p.Date, p.Shortest.Length)).ToList();
                return Task.FromResult(list);
            }
        }

        public Task<IReadOnlyList<DailyEntryRecord>> GetEntriesAsync(string guestId)
        {
            lock (_gate)
            {
                IReadOnlyList<DailyEntryRecord> list = _entries.Values.Where(e => e.GuestId == guestId).Select(Copy).ToList();
                return Task.FromResult(list);
            }
        }

        public Task<IReadOnlyList<DailyArchiveRecord>> GetArchiveAsync(string guestId)
        {
            lock (_gate)
            {
                IReadOnlyList<DailyArchiveRecord> list = _archive.Values.Where(e => e.GuestId == guestId).Select(Copy).ToList();
                return Task.FromResult(list);
            }
        }

        public Task<DailyArchiveRecord?> GetArchiveEntryAsync(int number, string guestId)
        {
            lock (_gate)
                return Task.FromResult(_archive.TryGetValue((number, guestId), out DailyArchiveRecord? e) ? Copy(e) : null);
        }

        public Task SaveArchiveEntryAsync(DailyArchiveRecord entry)
        {
            lock (_gate)
                _archive[(entry.Number, entry.GuestId)] = Copy(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DailySolve>> GetSolvesAsync(int from, int to)
        {
            lock (_gate)
            {
                IReadOnlyList<DailySolve> list = _entries.Values
                    .Where(e => e.BestMoves > 0 && e.Number >= from && e.Number <= to)
                    .Select(e => new DailySolve(e.GuestId, _players[e.GuestId].Name, e.Number)).ToList();
                return Task.FromResult(list);
            }
        }

        public Task<int> MergeGuestAsync(string from, string into)
        {
            lock (_gate)
            {
                int moved = 0;
                foreach (DailyEntryRecord entry in _entries.Values.Where(e => e.GuestId == from).ToList())
                {
                    _entries.Remove((entry.Number, from));
                    if (!_entries.TryGetValue((entry.Number, into), out DailyEntryRecord? kept) || DailyMerge.IsBetter(entry, kept))
                    {
                        DailyEntryRecord copy = Copy(entry);
                        copy.GuestId = into;
                        _entries[(entry.Number, into)] = copy;
                    }
                    moved++;
                }
                foreach (DailyArchiveRecord play in _archive.Values.Where(e => e.GuestId == from).ToList())
                {
                    _archive.Remove((play.Number, from));
                    _archive[(play.Number, into)] = _archive.TryGetValue((play.Number, into), out DailyArchiveRecord? kept)
                        ? DailyMerge.Combine(kept, play, into)
                        : DailyMerge.Combine(play, play, into);
                    moved++;
                }
                if (_players.Remove(from, out DailyPlayerRecord? player) && !_players.ContainsKey(into))
                    _players[into] = new DailyPlayerRecord { GuestId = into, Name = player.Name, CreatedUtc = player.CreatedUtc, LastSeenUtc = player.LastSeenUtc };
                return Task.FromResult(moved);
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

        private static DailyArchiveRecord Copy(DailyArchiveRecord e) => new()
        {
            Number = e.Number, GuestId = e.GuestId, OpenedUtc = e.OpenedUtc, BestMoves = e.BestMoves,
            BestSolution = e.BestSolution, SolvedUtc = e.SolvedUtc, SawSolutionUtc = e.SawSolutionUtc,
        };
    }
}
