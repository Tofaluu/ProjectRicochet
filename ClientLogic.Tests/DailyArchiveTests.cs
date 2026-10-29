using System;
using System.Linq;
using Ricochet.Client.Game;
using SharedLogic;
using Xunit;

namespace ClientLogic.Tests
{
    public class DailyArchiveTests
    {
        private static readonly RoundSetup Round = BoardGenerator.GenerateRound(new Random(5));

        // Puzzles #1 (Sep 29) to #13 (today, Oct 11): across two months.
        private static DailyCalendar Calendar() => new DailyCalendar
        {
            Today = 13,
            Days = Enumerable.Range(1, 13).Select(n => new DailyDay
            {
                Number = n,
                Date = new DateTime(2026, 9, 28).AddDays(n).ToString("yyyy-MM-dd"),
                ShortestLength = 9,
            }).ToArray(),
        };

        private static Move[] Moves(int length) => Enumerable.Repeat(new Move(RobotColor.Red, Direction.Left), length).ToArray();

        private static DailyPastInfo Past(int number, int dayMoves = 0, int laterMoves = 0) => new DailyPastInfo
        {
            Number = number,
            Date = "2026-10-05",
            Board = BoardCodec.Encode(Round),
            ShortestLength = 9,
            Opened = dayMoves > 0,
            BestMoves = dayMoves,
            BestSolution = MoveCodec.Encode(Moves(dayMoves)),
            LaterMoves = laterMoves,
            LaterSolution = MoveCodec.Encode(Moves(laterMoves)),
            Players = 4,
        };

        [Fact]
        public void Marks_TakeTheBestOfTheDayAndLaterPlays()
        {
            Assert.Equal(DayMark.None, DailyArchive.Mark(new DailyDay { ShortestLength = 9 }));
            Assert.Equal(DayMark.None, DailyArchive.Mark(new DailyDay { ShortestLength = 9, Opened = true, SawSolution = true }));
            Assert.Equal(DayMark.Solved, DailyArchive.Mark(new DailyDay { ShortestLength = 9, Opened = true, BestMoves = 12 }));
            Assert.Equal(DayMark.Perfect, DailyArchive.Mark(new DailyDay { ShortestLength = 9, Opened = true, BestMoves = 12, LaterMoves = 9 }));
            Assert.Equal(DayMark.Solved, DailyArchive.Mark(new DailyDay { ShortestLength = 9, LaterMoves = 10, SawSolution = true }));
        }

        [Fact]
        public void TheCalendar_FillsTheDaysResult_AndRingsLaterPlays()
        {
            // Solved on the day: filled; a later play adds a ring only when it makes the day perfect.
            var solvedThenPerfect = new DailyDay { ShortestLength = 9, Opened = true, BestMoves = 12, LaterMoves = 9 };
            Assert.Equal(DayMark.Solved, DailyArchive.DayResult(solvedThenPerfect));
            Assert.Equal(DayMark.Perfect, DailyArchive.LaterRing(solvedThenPerfect));
            var solvedThenShorter = new DailyDay { ShortestLength = 9, Opened = true, BestMoves = 12, LaterMoves = 10 };
            Assert.Equal(DayMark.None, DailyArchive.LaterRing(solvedThenShorter));
            var perfectOnTheDay = new DailyDay { ShortestLength = 9, Opened = true, BestMoves = 9, LaterMoves = 9 };
            Assert.Equal(DayMark.Perfect, DailyArchive.DayResult(perfectOnTheDay));
            Assert.Equal(DayMark.None, DailyArchive.LaterRing(perfectOnTheDay));

            // Not solved on the day: no fill, a ring in the later result's colour.
            var later = new DailyDay { ShortestLength = 9, Opened = true, LaterMoves = 11 };
            Assert.Equal(DayMark.None, DailyArchive.DayResult(later));
            Assert.Equal(DayMark.Solved, DailyArchive.LaterRing(later));
            Assert.Equal(DayMark.None, DailyArchive.LaterRing(new DailyDay { ShortestLength = 9, Opened = true }));
        }

        [Fact]
        public void Calendar_CountsPastDays_AndShowsTodaysMonth()
        {
            var archive = new DailyArchive();
            DailyCalendar calendar = Calendar();
            calendar.Days[0].BestMoves = 9;
            calendar.Days[1].LaterMoves = 11;
            calendar.Days[12].BestMoves = 9; // today: not counted
            archive.OnCalendar(calendar);

            Assert.Equal(13, archive.Today);
            Assert.Equal(12, archive.PastCount);
            Assert.Equal(2, archive.SolvedCount);
            Assert.Equal(1, archive.PerfectCount);
            Assert.Equal((2026, 10), (archive.ShownYear, archive.ShownMonth));
            Assert.Equal(7, archive.LastDays(7).Count);
            Assert.Equal(13, archive.LastDays(7).Last().Number);
            Assert.Equal(3, archive.DayOn(new DateTime(2026, 10, 1)).Number);
            Assert.Null(archive.DayOn(new DateTime(2026, 10, 20)));

            Assert.True(archive.CanShowMonth(-1));
            Assert.False(archive.CanShowMonth(1));
            archive.ShowMonth(-1);
            Assert.Equal(9, archive.ShownMonth);
            Assert.False(archive.CanShowMonth(-1));
            archive.ShowMonth(-1); // nothing before launch
            Assert.Equal(9, archive.ShownMonth);
        }

        [Fact]
        public void PlayingAPastPuzzle_ImprovesOnLaterPlays_AndUpdatesTheCalendar()
        {
            var archive = new DailyArchive();
            archive.OnCalendar(Calendar());
            archive.Open();
            archive.Select(7);
            Assert.True(archive.LoadingPast);
            archive.OnPast(Past(6)); // an answer for a day no longer selected
            Assert.Null(archive.Past);

            archive.OnPast(Past(7, dayMoves: 12));
            PastPuzzle past = archive.Past;
            Assert.False(archive.LoadingPast);
            Assert.Equal(12, past.BestMoves);
            Assert.Equal(12, past.MySolution.Length);
            Assert.True(past.IsImprovement(13)); // later plays are compared with later plays only
            Assert.False(past.IsPerfect);

            archive.OnSubmitted(new DailyPastResult { Moves = 10, Improved = true, LaterMoves = 10, LaterSolution = MoveCodec.Encode(Moves(10)) });
            Assert.Equal(10, past.BestMoves);
            Assert.Equal(10, past.MySolution.Length);
            Assert.False(past.IsImprovement(10));
            Assert.Equal(10, archive.Day(7).LaterMoves);
            Assert.Equal(DayMark.Solved, DailyArchive.Mark(archive.Day(7)));

            // Picking another day keeps this one up until the next arrives.
            archive.Select(8);
            Assert.Same(past, archive.Past);
            Assert.True(archive.LoadingPast);
            archive.OnPast(Past(8));
            Assert.Equal(8, archive.Past.Number);
            archive.Select(7);
            archive.OnPast(Past(7, dayMoves: 12, laterMoves: 10));
            past = archive.Past;

            archive.OnSolution(new DailySolution { Number = 7, Moves = MoveCodec.Encode(Moves(9)) });
            Assert.Equal(9, past.Shortest.Length);
            Assert.True(past.SawSolution);
            Assert.True(archive.Day(7).SawSolution);

            past.OnLeaderboard(new DailyLeaderboard { Number = 6, Top = new DailyLeaderboardEntry[0] }); // another day's
            Assert.Null(past.Leaderboard);
            past.OnLeaderboard(new DailyLeaderboard { Number = 7, Top = new DailyLeaderboardEntry[0], Players = 0 });
            Assert.Equal(7, past.Leaderboard.Number);

            archive.Close();
            Assert.False(archive.IsOpen);
            Assert.Null(archive.Past);
            Assert.True(archive.HasCalendar); // kept for the daily screen's strip
        }
    }
}
