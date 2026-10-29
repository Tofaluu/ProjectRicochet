using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SharedLogic;

namespace BackendServer.Services
{
    /// <summary>
    /// The daily puzzle's rules: one board a day for everyone, changing at midnight in <see cref="DailyOptions.TimeZone"/>
    /// and getting harder through the week (<see cref="Difficulty"/>). Players are guests known by a secret id kept on
    /// their device. They may keep improving until the reset; their best is the fewest moves, timed from when they
    /// first opened the puzzle (server time), and every solution is checked here, so results can't be faked.
    /// </summary>
    public class DailyService
    {
        private static readonly Regex GuestIdPattern = new("^[A-Za-z0-9_-]{16,64}$");
        private const int MaxNameLength = 20;
        // Streaks are counted over at most this many days back (a year and a bit).
        private const int StreakWindow = 400;

        private readonly IDailyStore _store;
        private readonly DailyOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<DailyService> _log;
        private readonly TimeZoneInfo _zone;
        private readonly DateOnly _launch;
        private readonly SemaphoreSlim _generating = new(1, 1);

        public DailyService(IDailyStore store, IOptions<DailyOptions> options, TimeProvider time, ILogger<DailyService> log)
        {
            _store = store;
            _options = options.Value;
            _time = time;
            _log = log;
            _zone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
            _launch = DateOnly.ParseExact(_options.LaunchDate, "yyyy-MM-dd");
        }

        /// <summary>
        /// The day's board length (shortest solution), easy on Monday to hard on Sunday like a newspaper crossword. Each
        /// puzzle gets one of these lengths, picked at random with equal odds (<see cref="PickLength"/>): a search for the
        /// whole range would mostly stop at its lower end.
        /// </summary>
        public static (int Min, int Max) Difficulty(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => (9, 9),
            DayOfWeek.Tuesday => (10, 11),
            DayOfWeek.Wednesday => (11, 12),
            DayOfWeek.Thursday => (12, 13),
            DayOfWeek.Friday => (13, 14),
            DayOfWeek.Saturday => (14, 15),
            _ => (16, 17),
        };

        /// <summary>One length from <paramref name="min"/>..<paramref name="max"/>, each equally likely.</summary>
        public static int PickLength(int min, int max, Random random) => min + random.Next(max - min + 1);

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        /// <summary>The puzzle day at <paramref name="utc"/> (its date where the puzzle changes).</summary>
        /// <summary>
        /// How many robots every shortest solution of the day's puzzle must move (user, 2026-10-29): Monday to Thursday not
        /// the target robot alone, Friday and Saturday at least 3, Sunday at least 3 and all 4 if one is found in
        /// <see cref="DailyOptions.PreferSeconds"/> (all-4 boards are rare: 2 of 222 measured).
        /// </summary>
        public static (int Required, int Preferred) Robots(DayOfWeek day) => day switch
        {
            DayOfWeek.Friday or DayOfWeek.Saturday => (3, 3),
            DayOfWeek.Sunday => (3, 4),
            _ => (2, 2),
        };

        public DateOnly DayAt(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, _zone));

        public int NumberOf(DateOnly day) => day.DayNumber - _launch.DayNumber + 1;
        public DateOnly DayOf(int number) => _launch.AddDays(number - 1);
        public int TodaysNumber => NumberOf(DayAt(UtcNow));

        // Midnight at the start of the day after `day`, where the puzzle changes, in UTC.
        private DateTime NextResetUtc(DateOnly day) =>
            TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone);

        public async Task<DailyPuzzleInfo> GetDailyAsync(string guestId, string name)
        {
            CheckGuestId(guestId);
            DateTime now = UtcNow;
            DateOnly today = DayAt(now);
            int number = NumberOf(today);
            if (number < 1)
                throw new HubException($"The daily puzzle starts on {_launch:yyyy-MM-dd}.");

            DailyPuzzleRecord puzzle = await EnsurePuzzleAsync(number);
            DailyEntryRecord entry = await _store.OpenAsync(number, guestId, CleanName(name), now);
            return new DailyPuzzleInfo
            {
                Number = number,
                Date = today.ToString("yyyy-MM-dd"),
                Board = puzzle.Board,
                ShortestLength = puzzle.Shortest.Length, // one byte per move
                SecondsSinceOpened = (now - entry.OpenedUtc).TotalSeconds,
                SecondsUntilNext = (NextResetUtc(today) - now).TotalSeconds,
                BestMoves = entry.BestMoves,
                BestSeconds = entry.BestSeconds,
                BestSolution = entry.BestSolution ?? Array.Empty<byte>(),
                Streak = await StreakAsync(guestId, number),
            };
        }

        public async Task<DailyResult> SubmitAsync(string guestId, int number, byte[] moveData)
        {
            CheckGuestId(guestId);
            DateTime now = UtcNow;
            if (number != NumberOf(DayAt(now)))
                throw new HubException("That daily puzzle is over. Load today's puzzle.");
            if (moveData == null || moveData.Length == 0 || moveData.Length > _options.MaxSolutionMoves)
                throw new HubException("That isn't a valid solution.");
            DailyEntryRecord entry = await _store.GetEntryAsync(number, guestId)
                ?? throw new HubException("Open today's puzzle first.");
            DailyPuzzleRecord puzzle = await _store.GetPuzzleAsync(number)
                ?? throw new HubException("Open today's puzzle first.");
            Move[] moves = CheckSolution(puzzle, moveData);

            double seconds = (now - entry.OpenedUtc).TotalSeconds;
            bool improved = entry.BestMoves == 0 || moves.Length < entry.BestMoves;
            int bestMoves = entry.BestMoves;
            double bestSeconds = entry.BestSeconds;
            byte[] bestSolution = entry.BestSolution ?? Array.Empty<byte>();
            if (improved)
            {
                await _store.SaveBestAsync(number, guestId, moves.Length, seconds, moveData, now);
                bestMoves = moves.Length;
                bestSeconds = seconds;
                bestSolution = moveData;
            }

            (_, int players) = await _store.GetLeaderboardAsync(number, 0);
            return new DailyResult
            {
                Moves = moves.Length,
                Seconds = seconds,
                Improved = improved,
                BestMoves = bestMoves,
                BestSeconds = bestSeconds,
                BestSolution = bestSolution,
                Rank = await _store.GetRankAsync(number, bestMoves, bestSeconds),
                Players = players,
                Streak = await StreakAsync(guestId, number),
                ShortestLength = puzzle.Shortest.Length,
            };
        }

        public async Task<DailyLeaderboard> GetLeaderboardAsync(string guestId, int number)
        {
            CheckGuestId(guestId);
            if (number < 1 || number > TodaysNumber)
                throw new HubException("There's no such daily puzzle.");
            (IReadOnlyList<DailyRow> top, int players) = await _store.GetLeaderboardAsync(number, _options.LeaderboardSize);
            DailyEntryRecord? mine = await _store.GetEntryAsync(number, guestId);
            return new DailyLeaderboard
            {
                Number = number,
                Top = top.Select(r => new DailyLeaderboardEntry { Name = r.Name, Moves = r.Moves, Seconds = r.Seconds, IsMe = r.GuestId == guestId })
                    .ToArray(),
                MyRank = mine != null && mine.BestMoves > 0 ? await _store.GetRankAsync(number, mine.BestMoves, mine.BestSeconds) : 0,
                Players = players,
            };
        }

        // ---- Past puzzles: played again after their day, never changing its leaderboard or a streak ----

        /// <summary>Every puzzle so far (today's included, which the calendar shows but doesn't open) and the player's results.</summary>
        public async Task<DailyCalendar> GetCalendarAsync(string guestId)
        {
            CheckGuestId(guestId);
            int today = TodaysNumber;
            IReadOnlyList<DailySummary> puzzles = await _store.GetSummariesAsync(today);
            Dictionary<int, DailyEntryRecord> entries = (await _store.GetEntriesAsync(guestId)).ToDictionary(e => e.Number);
            Dictionary<int, DailyArchiveRecord> later = (await _store.GetArchiveAsync(guestId)).ToDictionary(e => e.Number);
            return new DailyCalendar
            {
                Today = today,
                Days = puzzles.Select(p =>
                {
                    entries.TryGetValue(p.Number, out DailyEntryRecord? entry);
                    later.TryGetValue(p.Number, out DailyArchiveRecord? play);
                    return new DailyDay
                    {
                        Number = p.Number,
                        Date = p.Date.ToString("yyyy-MM-dd"),
                        ShortestLength = p.ShortestLength,
                        Opened = entry != null,
                        BestMoves = entry?.BestMoves ?? 0,
                        LaterMoves = play?.BestMoves ?? 0,
                        SawSolution = play?.SawSolutionUtc != null,
                    };
                }).ToArray(),
            };
        }

        /// <summary>A puzzle from before today, to play again.</summary>
        public async Task<DailyPastInfo> GetPastAsync(string guestId, int number)
        {
            CheckGuestId(guestId);
            DailyPuzzleRecord puzzle = await PastPuzzleAsync(number);
            DailyEntryRecord? entry = await _store.GetEntryAsync(number, guestId);
            DailyArchiveRecord? play = await _store.GetArchiveEntryAsync(number, guestId);
            (_, int players) = await _store.GetLeaderboardAsync(number, 0);
            return new DailyPastInfo
            {
                Number = number,
                Date = puzzle.Date.ToString("yyyy-MM-dd"),
                Board = puzzle.Board,
                ShortestLength = puzzle.Shortest.Length,
                Opened = entry != null,
                BestMoves = entry?.BestMoves ?? 0,
                BestSeconds = entry?.BestSeconds ?? 0,
                BestSolution = entry?.BestSolution ?? Array.Empty<byte>(),
                LaterMoves = play?.BestMoves ?? 0,
                LaterSolution = play?.BestSolution ?? Array.Empty<byte>(),
                SawSolution = play?.SawSolutionUtc != null,
                Players = players,
            };
        }

        /// <summary>A solution to a past puzzle: the player's best later play is kept (fewest moves).</summary>
        public async Task<DailyPastResult> SubmitPastAsync(string guestId, int number, byte[] moveData)
        {
            CheckGuestId(guestId);
            DailyPuzzleRecord puzzle = await PastPuzzleAsync(number);
            Move[] moves = CheckSolution(puzzle, moveData);
            DateTime now = UtcNow;
            DailyArchiveRecord play = await _store.GetArchiveEntryAsync(number, guestId)
                ?? new DailyArchiveRecord { Number = number, GuestId = guestId, OpenedUtc = now };
            bool improved = play.BestMoves == 0 || moves.Length < play.BestMoves;
            if (improved)
            {
                play.BestMoves = moves.Length;
                play.BestSolution = moveData;
                play.SolvedUtc = now;
                await _store.SaveArchiveEntryAsync(play);
            }
            return new DailyPastResult
            {
                Moves = moves.Length,
                Improved = improved,
                LaterMoves = play.BestMoves,
                LaterSolution = play.BestSolution ?? Array.Empty<byte>(),
            };
        }

        /// <summary>A past puzzle's shortest solution; the player's calendar marks the day as seen from now on.</summary>
        public async Task<DailySolution> ShowSolutionAsync(string guestId, int number)
        {
            CheckGuestId(guestId);
            DailyPuzzleRecord puzzle = await PastPuzzleAsync(number);
            DateTime now = UtcNow;
            DailyArchiveRecord play = await _store.GetArchiveEntryAsync(number, guestId)
                ?? new DailyArchiveRecord { Number = number, GuestId = guestId, OpenedUtc = now };
            if (play.SawSolutionUtc == null)
            {
                play.SawSolutionUtc = now;
                await _store.SaveArchiveEntryAsync(play);
            }
            return new DailySolution { Number = number, Moves = puzzle.Shortest };
        }

        /// <summary>
        /// Every player's longest streak ever (equal ones share a place; among them, those still going first) with the streak
        /// each has going now, and the asking player's.
        /// </summary>
        public async Task<StreakLeaderboard> GetStreakLeaderboardAsync(string guestId)
        {
            CheckGuestId(guestId);
            int today = TodaysNumber;
            IReadOnlyList<DailySolve> solves = await _store.GetSolvesAsync(Math.Max(1, today - StreakWindow), today);
            var streaks = solves.GroupBy(s => s.GuestId)
                .Select(g =>
                {
                    HashSet<int> solved = g.Select(s => s.Number).ToHashSet();
                    return (GuestId: g.Key, Name: g.First().Name, Days: LongestStreak(solved), Current: CountStreak(solved, today));
                })
                .OrderByDescending(s => s.Days).ThenByDescending(s => s.Current).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int RankOf(int days) => 1 + streaks.Count(s => s.Days > days);
            var mine = streaks.FirstOrDefault(s => s.GuestId == guestId);
            return new StreakLeaderboard
            {
                Top = streaks.Take(_options.LeaderboardSize)
                    .Select(s => new StreakEntry { Name = s.Name, Days = s.Days, Current = s.Current, Rank = RankOf(s.Days), IsMe = s.GuestId == guestId })
                    .ToArray(),
                MyBest = mine.Days,
                MyCurrent = mine.Current,
                MyRank = mine.Days > 0 ? RankOf(mine.Days) : 0,
                Players = streaks.Count,
            };
        }

        // Only puzzles whose day is over: today's (and tomorrow's, made ahead) never go out through the archive.
        private async Task<DailyPuzzleRecord> PastPuzzleAsync(int number)
        {
            if (number < 1 || number >= TodaysNumber)
                throw new HubException("That puzzle isn't in the archive.");
            return await _store.GetPuzzleAsync(number) ?? throw new HubException("There's no puzzle for that day.");
        }

        private Move[] CheckSolution(DailyPuzzleRecord puzzle, byte[] moveData)
        {
            if (moveData == null || moveData.Length == 0 || moveData.Length > _options.MaxSolutionMoves)
                throw new HubException("That isn't a valid solution.");
            Move[] moves;
            try
            {
                moves = MoveCodec.Decode(moveData);
            }
            catch (FormatException)
            {
                throw new HubException("That isn't a valid solution.");
            }
            RoundSetup round = BoardCodec.Decode(puzzle.Board);
            if (GameRules.ValidateSolution(round.Grid, round.Robots, round.Target, moves) != SolutionResult.Valid)
                throw new HubException("That isn't a valid solution.");
            return moves;
        }

        /// <summary>
        /// The puzzle for <paramref name="number"/>, generating (and solving) it if nobody has yet. Called ahead of time by
        /// <see cref="DailyPrepareService"/>, so players rarely wait for a board.
        /// </summary>
        public async Task<DailyPuzzleRecord> EnsurePuzzleAsync(int number)
        {
            DailyPuzzleRecord? puzzle = await _store.GetPuzzleAsync(number);
            if (puzzle != null)
                return puzzle;
            await _generating.WaitAsync();
            try
            {
                puzzle = await _store.GetPuzzleAsync(number);
                if (puzzle != null)
                    return puzzle;
                DailyPuzzleRecord made = await Task.Run(() => Generate(number));
                await _store.SavePuzzleAsync(made);
                return await _store.GetPuzzleAsync(number) ?? made;
            }
            finally
            {
                _generating.Release();
            }
        }

        // A board whose shortest solution has the day's length (BoardSearch, like lobby boards); out of time, the
        // longest found that isn't too long. Every board found has a known shortest solution.
        private DailyPuzzleRecord Generate(int number)
        {
            DateOnly day = DayOf(number);
            (int min, int max) = Difficulty(day.DayOfWeek);
            min = _options.MinMoves ?? min;
            max = Math.Max(min, _options.MaxMoves ?? max);
            int seed = _options.Seed.HasValue ? _options.Seed.Value * 10007 + number : Random.Shared.Next();
            var random = new Random(seed);
            int wanted = PickLength(min, max, random);
            // A length override (testing) drops the robots rule unless it's given too: tiny boards rarely need 3 robots.
            (int needRobots, int preferRobots) = _options.MinRobots.HasValue ? (_options.MinRobots.Value, _options.MinRobots.Value)
                : _options.MinMoves.HasValue ? (1, 1) : Robots(day.DayOfWeek);
            var solverLimit = TimeSpan.FromSeconds(_options.SolverTimeLimitSeconds);
            // 15+ move boards (Saturday, Sunday) can take minutes to find on the server; there's a day to spare.
            var budget = TimeSpan.FromSeconds(wanted >= DailyOptions.LongBoardMoves ? _options.LongSearchSeconds : _options.SearchSeconds);
            var clock = Stopwatch.StartNew();
            BoardSearch search = null;
            if (preferRobots > needRobots)
            {
                // First try for the preferred number of robots for part of the budget.
                var preferred = new BoardSearch(wanted, random, solverLimit, wanted, preferRobots);
                var preferBudget = TimeSpan.FromSeconds(Math.Min(_options.PreferSeconds, budget.TotalSeconds));
                while (!preferred.Step() && clock.Elapsed < preferBudget) { }
                if (preferred.Found)
                    search = preferred;
            }
            if (search == null)
            {
                search = new BoardSearch(wanted, random, solverLimit, wanted, needRobots);
                // Out of time: the longest board found, even if fewer robots can solve it (never no puzzle).
                while (!search.Step(noTimeLimit: clock.Elapsed >= budget && search.Longest == null))
                {
                    if (clock.Elapsed >= budget && search.Longest != null)
                        break;
                }
            }
            int robots = search.Found && search.FoundRobots > 0 ? search.FoundRobots
                : Solver.FewestRobots(search.Longest, search.LongestMoves.Length, solverLimit);
            _log.LogInformation("Daily #{Number} ({Day:yyyy-MM-dd}, {Weekday}): shortest {Moves} moves needing {Robots} robots, wanted {Wanted} of {Min}-{Max} and {Need}+ robots ({Solves} solves, {Ms} ms)",
                number, day, day.DayOfWeek, search.LongestMoves.Length, robots, wanted, min, max, needRobots, search.Solves, clock.ElapsedMilliseconds);
            return new DailyPuzzleRecord
            {
                Number = number,
                Date = day,
                Board = BoardCodec.Encode(search.Longest),
                Shortest = MoveCodec.Encode(search.LongestMoves),
                CreatedUtc = UtcNow,
            };
        }

        // Days in a row solved, ending today if today is solved (else yesterday: today can still be saved).
        private async Task<int> StreakAsync(string guestId, int today)
        {
            var solved = new HashSet<int>(await _store.GetSolvedNumbersAsync(guestId, Math.Max(1, today - StreakWindow), today));
            return CountStreak(solved, today);
        }

        private static int CountStreak(HashSet<int> solved, int today)
        {
            int day = solved.Contains(today) ? today : today - 1;
            int streak = 0;
            while (solved.Contains(day))
            {
                streak++;
                day--;
            }
            return streak;
        }

        // The longest run of consecutive numbers.
        private static int LongestStreak(HashSet<int> solved)
        {
            int longest = 0;
            foreach (int day in solved)
            {
                if (solved.Contains(day - 1))
                    continue; // not a run's first day
                int length = 1;
                while (solved.Contains(day + length))
                    length++;
                longest = Math.Max(longest, length);
            }
            return longest;
        }

        private static void CheckGuestId(string guestId)
        {
            if (guestId == null || !GuestIdPattern.IsMatch(guestId))
                throw new HubException("Invalid player id.");
        }

        private static string CleanName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > MaxNameLength)
                name = name.Substring(0, MaxNameLength);
            return name.Length == 0 ? "Player" : name;
        }
    }
}
