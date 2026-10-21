using System;
using Ricochet.Client.Game;
using SharedLogic;
using Xunit;

namespace ClientLogic.Tests
{
    public class DailyStateTests
    {
        private static readonly RoundSetup Round = BoardGenerator.GenerateRound(new Random(3));

        private static DailyPuzzleInfo Info(int bestMoves = 0, double opened = 30, int streak = 2) => new DailyPuzzleInfo
        {
            Number = 12,
            Date = "2026-10-18",
            Board = BoardCodec.Encode(Round),
            ShortestLength = 8,
            SecondsSinceOpened = opened,
            SecondsUntilNext = 3600,
            BestMoves = bestMoves,
            BestSeconds = bestMoves > 0 ? 102 : 0,
            BestSolution = bestMoves > 0 ? MoveCodec.Encode(Solution(bestMoves)) : new byte[0],
            Streak = streak,
        };

        // Blue, Green, Red, Yellow, Blue, ... (only the robots matter for the share squares).
        private static Move[] Solution(int length)
        {
            var moves = new Move[length];
            RobotColor[] order = { RobotColor.Blue, RobotColor.Green, RobotColor.Red, RobotColor.Yellow };
            for (int i = 0; i < length; i++)
                moves[i] = new Move(order[i % 4], Direction.Up);
            return moves;
        }

        [Fact]
        public void Loading_ShowsTheBoardAndRunsTheClockFromFirstOpening()
        {
            var daily = new DailyState();
            Assert.False(daily.IsActive);
            daily.BeginLoading();
            Assert.Equal(DailyStage.Loading, daily.Stage);

            daily.OnLoaded(Info(), now: 100f);
            Assert.Equal(DailyStage.Playing, daily.Stage);
            Assert.Equal(12, daily.Number);
            Assert.Equal("Sunday", daily.Weekday);
            Assert.Equal(BoardCodec.Encode(Round), BoardCodec.Encode(daily.Round));
            Assert.Equal(30, daily.Elapsed(100f), 3); // opened 30 s ago on another visit
            Assert.Equal(40, daily.Elapsed(110f), 3);
            Assert.Equal(3590, daily.UntilNext(110f), 3);
            Assert.False(daily.HasSolved);
        }

        [Fact]
        public void OnlyFewerMovesAreAnImprovement()
        {
            var daily = new DailyState();
            daily.OnLoaded(Info(), 0f);
            Assert.True(daily.IsImprovement(12));

            daily.OnSubmitted(new DailyResult { Moves = 10, Improved = true, BestMoves = 10, BestSeconds = 50, Rank = 3, Players = 9, Streak = 3, ShortestLength = 8 });
            Assert.True(daily.HasSolved);
            Assert.Equal(3, daily.Rank);
            Assert.Equal(3, daily.Streak);
            Assert.False(daily.IsPerfect); // 10 > the best possible 8
            Assert.False(daily.IsImprovement(10));
            Assert.False(daily.IsImprovement(11));
            Assert.True(daily.IsImprovement(9));

            daily.OnSubmitted(new DailyResult { Moves = 8, Improved = true, BestMoves = 8, BestSeconds = 70, ShortestLength = 8 });
            Assert.True(daily.IsPerfect);
        }

        [Fact]
        public void TheLeaderboard_OnlyCountsForTodaysPuzzle()
        {
            var daily = new DailyState();
            daily.OnLoaded(Info(bestMoves: 9), 0f);
            daily.OnLeaderboard(new DailyLeaderboard { Number = 11, Top = new DailyLeaderboardEntry[0], MyRank = 1, Players = 5 });
            Assert.Null(daily.Leaderboard);

            daily.OnLeaderboard(new DailyLeaderboard { Number = 12, Top = new DailyLeaderboardEntry[0], MyRank = 4, Players = 20 });
            Assert.Equal(4, daily.Rank);
            Assert.Equal(20, daily.Players);
        }

        [Fact]
        public void ShareText_ASquarePerMove_InTheRobotsColour()
        {
            var daily = new DailyState();
            daily.OnLoaded(Info(bestMoves: 9), 0f);
            string[] lines = daily.ShareText("ricochet.thomasliu.dev").Split('\n');
            Assert.Equal("Ricochet Daily #12: 9/8 moves, 1:42, 2-day streak", lines[0]);
            // blue, green, red, yellow, blue, green, red, yellow, blue
            Assert.Equal("\U0001F7E6\U0001F7E9\U0001F7E5\U0001F7E8\U0001F7E6\U0001F7E9\U0001F7E5\U0001F7E8\U0001F7E6", lines[1]);
            Assert.Equal("ricochet.thomasliu.dev", lines[2]);

            // A new best brings its own moves.
            daily.OnSubmitted(new DailyResult
            {
                BestMoves = 2, BestSeconds = 20, ShortestLength = 8,
                BestSolution = MoveCodec.Encode(new[] { new Move(RobotColor.Red, Direction.Up), new Move(RobotColor.Red, Direction.Left) }),
            });
            Assert.Equal("\U0001F7E5\U0001F7E5", daily.ShareText("x").Split('\n')[1]);

            daily.OnLoaded(Info(bestMoves: 8, streak: 1), 0f);
            Assert.StartsWith("Ricochet Daily #12: 8/8 moves, 1:42\n", daily.ShareText("x")); // no streak for 1 day
        }

        [Fact]
        public void Clock_ReadsLikeAStopwatch()
        {
            Assert.Equal("0:00", DailyState.Clock(0));
            Assert.Equal("0:42", DailyState.Clock(42.9));
            Assert.Equal("12:05", DailyState.Clock(725));
            Assert.Equal("1:02:03", DailyState.Clock(3723));
        }

        [Fact]
        public void Failures_AndClosing()
        {
            var daily = new DailyState();
            daily.BeginLoading();
            daily.OnFailed("That daily puzzle is over.");
            Assert.Equal(DailyStage.Error, daily.Stage);
            Assert.True(daily.IsActive);
            daily.Close();
            Assert.False(daily.IsActive);
        }

        [Fact]
        public void Reopening_TodaysLoadedPuzzle_UntilMidnight()
        {
            var daily = new DailyState();
            Assert.False(daily.CanReopen(0f)); // nothing loaded yet
            daily.OnLoaded(Info(bestMoves: 9), now: 100f);
            daily.Close();
            Assert.True(daily.CanReopen(200f));
            daily.Reopen();
            Assert.Equal(DailyStage.Playing, daily.Stage);
            Assert.Equal(9, daily.BestMoves);
            Assert.Equal(130, daily.Elapsed(200f), 3); // the clock carried on
            Assert.False(daily.CanReopen(100f + 3600f)); // midnight: fetch the new one
        }
    }
}
