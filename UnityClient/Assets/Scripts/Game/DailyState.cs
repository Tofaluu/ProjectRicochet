using System;
using System.Globalization;
using System.Text;
using SharedLogic;

namespace Ricochet.Client.Game
{
    public enum DailyStage
    {
        /// <summary>Not playing the daily puzzle.</summary>
        Off,
        Loading,
        Playing,
        /// <summary>Loading or a submission failed (<see cref="DailyState.Error"/>).</summary>
        Error,
    }

    /// <summary>
    /// Today's daily puzzle as this player sees it: the board, their clock (counting from the server's
    /// "first opened"), their best, streak, rank and the leaderboard, plus the share text. Plain C#: the views
    /// read it; times are passed in as <c>now</c> (seconds on any steady clock).
    /// </summary>
    public sealed class DailyState
    {
        private float _loadedAt;

        public DailyStage Stage { get; private set; }
        public string Error { get; private set; }

        public int Number { get; private set; }
        /// <summary>The puzzle's weekday (it sets the difficulty), e.g. "Sunday".</summary>
        public string Weekday { get; private set; }
        public RoundSetup Round { get; private set; }
        public int ShortestLength { get; private set; }

        public int BestMoves { get; private set; }
        public double BestSeconds { get; private set; }
        public bool HasSolved => BestMoves > 0;
        public int Streak { get; private set; }
        /// <summary>Place among today's solvers (0 = unknown / not solved) and how many solved it.</summary>
        public int Rank { get; private set; }
        public int Players { get; private set; }
        public DailyLeaderboard Leaderboard { get; private set; }

        /// <summary>The last solution sent was a new best (false: it didn't beat the best).</summary>
        public bool LastImproved { get; private set; }
        /// <summary>A solution is on its way to the server.</summary>
        public bool Submitting { get; private set; }

        private double _openedSeconds;
        private double _untilNext;

        public bool IsActive => Stage != DailyStage.Off;

        public void BeginLoading()
        {
            Stage = DailyStage.Loading;
            Error = null;
        }

        public void OnLoaded(DailyPuzzleInfo info, float now)
        {
            Stage = DailyStage.Playing;
            Error = null;
            Number = info.Number;
            Weekday = DateTime.TryParseExact(info.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
                ? day.DayOfWeek.ToString() : "";
            Round = BoardCodec.Decode(info.Board);
            ShortestLength = info.ShortestLength;
            BestMoves = info.BestMoves;
            BestSeconds = info.BestSeconds;
            Streak = info.Streak;
            _openedSeconds = info.SecondsSinceOpened;
            _untilNext = info.SecondsUntilNext;
            _loadedAt = now;
            Rank = 0;
            Players = 0;
            Leaderboard = null;
            Submitting = false;
        }

        public void OnFailed(string message)
        {
            Stage = DailyStage.Error;
            Error = message;
            Submitting = false;
        }

        /// <summary>Would this solution beat the player's best? (Only those are worth sending.)</summary>
        public bool IsImprovement(int moves) => moves > 0 && (BestMoves == 0 || moves < BestMoves);

        public void OnSubmitting() => Submitting = true;

        public void OnSubmitted(DailyResult result)
        {
            Submitting = false;
            LastImproved = result.Improved;
            BestMoves = result.BestMoves;
            BestSeconds = result.BestSeconds;
            Streak = result.Streak;
            Rank = result.Rank;
            Players = result.Players;
            ShortestLength = result.ShortestLength;
        }

        public void OnLeaderboard(DailyLeaderboard leaderboard)
        {
            if (leaderboard == null || leaderboard.Number != Number)
                return;
            Leaderboard = leaderboard;
            Players = leaderboard.Players;
            if (leaderboard.MyRank > 0)
                Rank = leaderboard.MyRank;
        }

        public void Close()
        {
            Stage = DailyStage.Off;
            Error = null;
            Submitting = false;
        }

        /// <summary>The player's clock: since they first opened today's puzzle.</summary>
        public double Elapsed(float now) => _openedSeconds + Math.Max(0f, now - _loadedAt);

        /// <summary>Until the next puzzle (midnight Eastern).</summary>
        public double UntilNext(float now) => Math.Max(0.0, _untilNext - Math.Max(0f, now - _loadedAt));

        /// <summary>
        /// The Wordle-style result to paste anywhere: no spoilers, a green square per move of the best possible
        /// solution and a yellow one per extra move.
        /// </summary>
        public string ShareText(string link)
        {
            var text = new StringBuilder($"Ricochet Daily #{Number}: {BestMoves}/{ShortestLength} moves, {Clock(BestSeconds)}");
            if (Streak > 1)
                text.Append($", {Streak}-day streak");
            text.Append('\n');
            int extra = Math.Max(0, BestMoves - ShortestLength);
            for (int i = 0; i < Math.Min(ShortestLength, 20); i++)
                text.Append("\U0001F7E9"); // green square
            for (int i = 0; i < Math.Min(extra, 10); i++)
                text.Append("\U0001F7E8"); // yellow square
            if (extra > 10)
                text.Append('+');
            text.Append('\n').Append(link);
            return text.ToString();
        }

        /// <summary>"0:42", "12:05", "1:02:03".</summary>
        public static string Clock(double seconds)
        {
            int total = (int)Math.Max(0, Math.Floor(seconds));
            int h = total / 3600, m = total / 60 % 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
    }
}
