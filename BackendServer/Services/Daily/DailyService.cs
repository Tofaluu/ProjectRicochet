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
        /// The day's board length (shortest solution), easy on Monday to hard on Sunday like a newspaper crossword.
        /// </summary>
        public static (int Min, int Max) Difficulty(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => (4, 5),
            DayOfWeek.Tuesday => (6, 7),
            DayOfWeek.Wednesday => (8, 9),
            DayOfWeek.Thursday => (10, 11),
            DayOfWeek.Friday => (12, 13),
            DayOfWeek.Saturday => (14, 15),
            _ => (16, 17),
        };

        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        /// <summary>The puzzle day at <paramref name="utc"/> (its date where the puzzle changes).</summary>
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
            var search = new BoardSearch(min, new Random(seed), TimeSpan.FromSeconds(_options.SolverTimeLimitSeconds), max);
            // 16+ move boards (Sunday) take ~10-70 s to find on the dev PC and longer on the server; there's a day to spare.
            var budget = TimeSpan.FromSeconds(min >= DailyOptions.LongBoardMoves ? _options.LongSearchSeconds : _options.SearchSeconds);
            var clock = Stopwatch.StartNew();
            while (!search.Step(noTimeLimit: clock.Elapsed >= budget && search.Longest == null))
            {
                if (clock.Elapsed >= budget && search.Longest != null)
                    break;
            }
            _log.LogInformation("Daily #{Number} ({Day:yyyy-MM-dd}, {Weekday}): shortest {Moves} moves, wanted {Min}-{Max} ({Solves} solves, {Ms} ms)",
                number, day, day.DayOfWeek, search.LongestMoves.Length, min, max, search.Solves, clock.ElapsedMilliseconds);
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
            int day = solved.Contains(today) ? today : today - 1;
            int streak = 0;
            while (solved.Contains(day))
            {
                streak++;
                day--;
            }
            return streak;
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
