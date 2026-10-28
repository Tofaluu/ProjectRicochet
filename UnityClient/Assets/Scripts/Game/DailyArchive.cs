using System;
using System.Collections.Generic;
using System.Globalization;
using SharedLogic;

namespace Ricochet.Client.Game
{
    /// <summary>How the player did on a day, as the calendar colours it.</summary>
    public enum DayMark
    {
        /// <summary>Never opened.</summary>
        None,
        /// <summary>Opened (on the day or later) but not solved.</summary>
        Tried,
        Solved,
        /// <summary>Solved in the best possible number of moves (on the day or later).</summary>
        Perfect,
    }

    /// <summary>
    /// The daily puzzle's past: the calendar of every puzzle so far with the player's result on each, the past puzzle
    /// being played again, and the streak leaderboard. Past plays never change a day's leaderboard or a streak (the
    /// server keeps them apart). Plain C#: the views read it.
    /// </summary>
    public sealed class DailyArchive
    {
        private readonly Dictionary<DateTime, DailyDay> _byDate = new Dictionary<DateTime, DailyDay>();

        /// <summary>The past-puzzles screen is open (the calendar, with the selected day on the board).</summary>
        public bool IsOpen { get; private set; }

        public DailyCalendar Calendar { get; private set; }
        public bool HasCalendar => Calendar != null && Calendar.Days != null;
        public string Error { get; private set; }

        /// <summary>Today's puzzle number (0 until the calendar has loaded).</summary>
        public int Today => HasCalendar ? Calendar.Today : 0;

        /// <summary>The day picked on the calendar (0 = none) and the month shown.</summary>
        public int Selected { get; private set; }
        public int ShownYear { get; private set; }
        public int ShownMonth { get; private set; }

        /// <summary>
        /// The last picked day's puzzle once loaded: while the next one loads it stays (board and card don't blank for a
        /// moment), so it may be an earlier pick than <see cref="Selected"/> until <see cref="OnPast"/>.
        /// </summary>
        public PastPuzzle Past { get; private set; }
        public bool LoadingPast { get; private set; }

        public StreakLeaderboard Streaks { get; private set; }

        public void Open()
        {
            IsOpen = true;
            Error = null;
        }

        public void Close()
        {
            IsOpen = false;
            Selected = 0;
            Past = null;
            LoadingPast = false;
        }

        public void OnCalendar(DailyCalendar calendar)
        {
            Calendar = calendar;
            Error = null;
            _byDate.Clear();
            if (calendar?.Days == null)
                return;
            foreach (DailyDay day in calendar.Days)
                if (TryParse(day.Date, out DateTime date))
                    _byDate[date] = day;
            if (ShownYear == 0 && Selected == 0)
                ShowMonthOf(Today);
        }

        public void OnFailed(string message)
        {
            Error = message;
            LoadingPast = false;
        }

        public DailyDay Day(int number)
        {
            if (!HasCalendar)
                return null;
            foreach (DailyDay day in Calendar.Days)
                if (day.Number == number)
                    return day;
            return null;
        }

        public DailyDay DayOn(DateTime date) => _byDate.TryGetValue(date.Date, out DailyDay day) ? day : null;

        /// <summary>The last <paramref name="count"/> days up to today, oldest first (fewer just after launch).</summary>
        public IReadOnlyList<DailyDay> LastDays(int count)
        {
            var days = new List<DailyDay>();
            if (!HasCalendar)
                return days;
            for (int number = Math.Max(1, Today - count + 1); number <= Today; number++)
            {
                DailyDay day = Day(number);
                if (day != null)
                    days.Add(day);
            }
            return days;
        }

        public static DayMark Mark(DailyDay day)
        {
            if (day == null)
                return DayMark.None;
            int best = Best(day.BestMoves, day.LaterMoves);
            if (best > 0)
                return best <= day.ShortestLength ? DayMark.Perfect : DayMark.Solved;
            return day.Opened || day.SawSolution ? DayMark.Tried : DayMark.None;
        }

        /// <summary>Past days (today not counted) solved, and of those the perfect ones.</summary>
        public int SolvedCount => Count(DayMark.Solved) + PerfectCount;
        public int PerfectCount => Count(DayMark.Perfect);
        public int PastCount => HasCalendar ? Math.Max(0, Calendar.Days.Length - (Day(Today) != null ? 1 : 0)) : 0;

        private int Count(DayMark mark)
        {
            int count = 0;
            if (HasCalendar)
                foreach (DailyDay day in Calendar.Days)
                    if (day.Number != Today && Mark(day) == mark)
                        count++;
            return count;
        }

        // ---- The calendar's month ----

        public void ShowMonthOf(int number)
        {
            DailyDay day = Day(number);
            if (day != null && TryParse(day.Date, out DateTime date))
            {
                ShownYear = date.Year;
                ShownMonth = date.Month;
            }
        }

        /// <summary>Is there a puzzle in the month <paramref name="delta"/> months from the one shown?</summary>
        public bool CanShowMonth(int delta)
        {
            if (!HasCalendar || ShownYear == 0)
                return false;
            DateTime month = new DateTime(ShownYear, ShownMonth, 1).AddMonths(delta);
            foreach (DateTime date in _byDate.Keys)
                if (date.Year == month.Year && date.Month == month.Month)
                    return true;
            return false;
        }

        public void ShowMonth(int delta)
        {
            if (!CanShowMonth(delta))
                return;
            DateTime month = new DateTime(ShownYear, ShownMonth, 1).AddMonths(delta);
            ShownYear = month.Year;
            ShownMonth = month.Month;
        }

        // ---- The selected past puzzle ----

        /// <summary>Picks a past day (not today): its puzzle is loading until <see cref="OnPast"/>.</summary>
        public void Select(int number)
        {
            Selected = number;
            LoadingPast = true;
            Error = null;
            ShowMonthOf(number);
        }

        public void OnPast(DailyPastInfo info)
        {
            if (info == null || info.Number != Selected)
                return;
            Past = new PastPuzzle(info);
            LoadingPast = false;
        }

        /// <summary>A later play of the selected puzzle was recorded; the calendar shows it too.</summary>
        public void OnSubmitted(DailyPastResult result)
        {
            if (Past == null)
                return;
            Past.OnSubmitted(result);
            DailyDay day = Day(Past.Number);
            if (day != null)
                day.LaterMoves = Past.LaterMoves;
        }

        public void OnSolution(DailySolution solution)
        {
            if (Past == null || solution == null || solution.Number != Past.Number)
                return;
            Past.OnSolution(solution);
            DailyDay day = Day(Past.Number);
            if (day != null)
                day.SawSolution = true;
        }

        public void OnStreaks(StreakLeaderboard streaks) => Streaks = streaks;

        internal static int Best(int a, int b) => a > 0 && b > 0 ? Math.Min(a, b) : Math.Max(a, b);

        internal static bool TryParse(string date, out DateTime day) =>
            DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    /// <summary>A past daily puzzle played again: no clock, nothing on the day's leaderboard or the streak.</summary>
    public sealed class PastPuzzle
    {
        public PastPuzzle(DailyPastInfo info)
        {
            Number = info.Number;
            DailyArchive.TryParse(info.Date, out DateTime date);
            Date = date;
            Round = BoardCodec.Decode(info.Board);
            ShortestLength = info.ShortestLength;
            Opened = info.Opened;
            DayMoves = info.BestMoves;
            DaySeconds = info.BestSeconds;
            DaySolution = Decode(info.BestSolution);
            LaterMoves = info.LaterMoves;
            LaterSolution = Decode(info.LaterSolution);
            SawSolution = info.SawSolution;
            Players = info.Players;
        }

        public int Number { get; }
        public DateTime Date { get; }
        public RoundSetup Round { get; }
        public int ShortestLength { get; }

        /// <summary>The player's result on the day (moves 0 = not solved then).</summary>
        public bool Opened { get; }
        public int DayMoves { get; }
        public double DaySeconds { get; }
        public Move[] DaySolution { get; }

        /// <summary>Their best when playing it later (0 = none yet).</summary>
        public int LaterMoves { get; private set; }
        public Move[] LaterSolution { get; private set; }

        public bool SawSolution { get; private set; }

        /// <summary>The shortest solution, once asked for (null before).</summary>
        public Move[] Shortest { get; private set; }

        /// <summary>How many solved it on its day.</summary>
        public int Players { get; }

        /// <summary>A solution is on its way to the server.</summary>
        public bool Submitting { get; private set; }

        public int BestMoves => DailyArchive.Best(DayMoves, LaterMoves);
        public bool IsPerfect => BestMoves > 0 && BestMoves <= ShortestLength;

        /// <summary>The player's own best solution, on the day or later (empty if never solved).</summary>
        public Move[] MySolution => LaterMoves > 0 && (DayMoves == 0 || LaterMoves < DayMoves) ? LaterSolution : DaySolution;

        /// <summary>Later plays are compared with the player's best later play (worth sending only if shorter).</summary>
        public bool IsImprovement(int moves) => moves > 0 && (LaterMoves == 0 || moves < LaterMoves);

        public void OnSubmitting() => Submitting = true;

        public void OnSubmitted(DailyPastResult result)
        {
            Submitting = false;
            LaterMoves = result.LaterMoves;
            LaterSolution = Decode(result.LaterSolution);
        }

        public void OnSubmitFailed() => Submitting = false;

        public void OnSolution(DailySolution solution)
        {
            Shortest = Decode(solution.Moves);
            SawSolution = true;
        }

        private static Move[] Decode(byte[] moves)
        {
            if (moves == null || moves.Length == 0)
                return new Move[0];
            try
            {
                return MoveCodec.Decode(moves);
            }
            catch (FormatException)
            {
                return new Move[0];
            }
        }
    }
}
