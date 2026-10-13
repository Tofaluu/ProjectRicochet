using System.Linq;
using Ricochet.Client.Game;
using SharedLogic;
using Xunit;

namespace ClientLogic.Tests
{
    public class MatchStateTests
    {
        private const string Me = "Alice";

        private static PlayerInfo P(string name, int score = 0, bool done = false, bool leading = false, bool connected = true) =>
            new PlayerInfo { Name = name, Score = score, Done = done, Leading = leading, Connected = connected };

        private static LobbyState Lobby(MatchPhase phase = MatchPhase.Waiting, int matchRound = 0, bool roundOver = false,
            string winner = null, params PlayerInfo[] players)
        {
            return new LobbyState
            {
                Code = "TEST",
                Host = Me,
                Phase = phase,
                Settings = new MatchSettings { PointsToWin = 5, FirstTimerSeconds = 60, ExtensionSeconds = 45 },
                MatchRound = matchRound,
                MatchWinner = winner,
                RoundOver = roundOver,
                Players = players.Length > 0 ? players : new[] { P(Me), P("Bob") },
            };
        }

        private static Move[] Moves(int count) =>
            Enumerable.Repeat(new Move(RobotColor.Red, Direction.Up), count).ToArray();

        private static RoundResult Result(int round, string winner = "Bob", string matchWinner = null) =>
            new RoundResult { RoundNumber = round, WinnerName = winner, ReplayStepSeconds = 1, ShortestReplayStepSeconds = 0.6, MatchWinnerName = matchWinner };

        // A match in progress with board <paramref name="round"/> on screen.
        private static MatchState InRound(int round = 7)
        {
            var match = new MatchState();
            match.OnLobbyState(Lobby(), Me);
            match.OnLobbyState(Lobby(MatchPhase.Playing), Me);
            match.OnRoundStarted(round);
            match.OnLobbyState(Lobby(MatchPhase.Playing, matchRound: 1), Me);
            return match;
        }

        [Fact]
        public void Joining_BetweenMatches_MeansPractice()
        {
            var match = new MatchState();
            Assert.Equal(MatchStage.NotInLobby, match.Stage);

            Assert.Equal(LobbyChange.BetweenMatches, match.OnLobbyState(Lobby(), Me));
            Assert.Equal(MatchStage.BetweenMatches, match.Stage);
            Assert.True(match.IAmHost);
            Assert.Equal(LobbyChange.None, match.OnLobbyState(Lobby(), Me));
        }

        [Fact]
        public void Start_Countdown_Board()
        {
            var match = new MatchState();
            match.OnLobbyState(Lobby(), Me);

            Assert.Equal(LobbyChange.MatchStarted, match.OnLobbyState(Lobby(MatchPhase.Playing), Me));
            Assert.Equal(MatchStage.Dealing, match.Stage);

            match.OnCountdown(3f, now: 10f);
            Assert.Equal(MatchStage.Countdown, match.Stage);
            Assert.Equal(2f, match.CountdownLeft(11f));
            Assert.Equal(0f, match.CountdownLeft(20f)); // "Go!" until the board arrives
            Assert.Equal(MatchStage.Countdown, match.Stage);

            match.OnRoundStarted(4);
            Assert.Equal(MatchStage.Solving, match.Stage);
            Assert.Equal(4, match.Round);
            Assert.False(match.TimerRunning);
        }

        [Fact]
        public void JoiningMidMatch_WaitsForTheCaughtUpBoard()
        {
            var match = new MatchState();
            Assert.Equal(LobbyChange.None, match.OnLobbyState(Lobby(MatchPhase.Playing, matchRound: 3), Me));
            Assert.Equal(MatchStage.Dealing, match.Stage);
            match.OnRoundStarted(9);
            Assert.Equal(MatchStage.Solving, match.Stage);
        }

        [Fact]
        public void Timer_ShowsWhenTimeWasAdded()
        {
            MatchState match = InRound();
            Assert.True(match.OnTimerUpdated(7, 60f, now: 100f));
            Assert.False(match.TimeJustAdded(100f));
            Assert.Equal(50f, match.TimeLeft(110f));

            match.OnTimerUpdated(7, 45f, now: 120f);
            Assert.True(match.TimeJustAdded(121f));
            Assert.False(match.TimeJustAdded(120f + MatchState.TimeAddedNoticeSeconds));
            Assert.Equal(0f, match.TimeLeft(1000f));

            Assert.False(match.OnTimerUpdated(6, 10f, now: 130f)); // an old round's
            Assert.Equal(35f, match.TimeLeft(130f));
        }

        [Fact]
        public void DoneButton_DependsOnTheRoundAndTheLead()
        {
            MatchState match = InRound();
            Assert.Equal(DoneAction.SkipBoard, match.DoneAction);

            match.OnTimerUpdated(7, 60f, 0f);
            Assert.Equal(DoneAction.ImDone, match.DoneAction);

            match.OnLobbyState(Lobby(MatchPhase.Playing, 1, false, null, P(Me, leading: true), P("Bob")), Me);
            Assert.Equal(DoneAction.None, match.DoneAction); // the leader isn't part of the vote

            match.OnLobbyState(Lobby(MatchPhase.Playing, 1, false, null, P(Me), P("Bob", leading: true)), Me);
            match.OnRoundEnded(Result(7), Moves(5), Moves(5));
            Assert.Equal(DoneAction.SkipReplay, match.DoneAction);

            match.OnCountdown(3f, 0f);
            Assert.Equal(DoneAction.None, match.DoneAction);
        }

        [Fact]
        public void Votes_CountOnlyConnectedNonLeaders()
        {
            MatchState match = InRound();
            match.OnLobbyState(Lobby(MatchPhase.Playing, 1, false, null,
                P(Me, done: true), P("Bob", leading: true), P("Carol"), P("Dan", done: true, connected: false)), Me);
            Assert.True(match.IAmDone);
            Assert.Equal(1, match.DoneCount);
            Assert.Equal(2, match.VoterCount);

            Assert.False(match.ToggleDone());
            Assert.False(match.IAmDone);
        }

        [Fact]
        public void LockIn_OnlyWhenItCanTakeTheLead()
        {
            MatchState match = InRound();
            Assert.False(match.CanLockIn(boardSolved: false));
            Assert.True(match.CanLockIn(boardSolved: true));
            Assert.True(match.TryLockIn(9));

            match.OnNewBest(7, "Bob", 6, boardSolution: -1);
            Assert.False(match.TryLockIn(6));
            Assert.Contains("don't beat Bob's 6", match.Feedback);
            Assert.True(match.TryLockIn(5));

            match.OnNewBest(7, Me, 5, boardSolution: 5);
            Assert.False(match.TryLockIn(5));
            Assert.Contains("already lead", match.Feedback);

            match.OnRoundEnded(Result(7, Me), Moves(5), Moves(5));
            Assert.False(match.CanLockIn(boardSolved: true));
        }

        [Fact]
        public void SomeoneElsesBest_ResetsOnlyABoardHoldingANoShorterSolution()
        {
            MatchState match = InRound();
            Assert.True(match.OnNewBest(7, "Bob", 6, boardSolution: 6));
            Assert.True(match.OnNewBest(7, "Bob", 5, boardSolution: 8));
            Assert.False(match.OnNewBest(7, "Bob", 4, boardSolution: -1)); // mid-attempt
            Assert.False(match.OnNewBest(7, "Bob", 4, boardSolution: 3)); // already shorter
            Assert.False(match.OnNewBest(7, Me, 3, boardSolution: 3)); // their own
            Assert.False(match.OnNewBest(6, "Bob", 2, boardSolution: 3)); // an old round's
            Assert.Equal(3, match.BestMoves);
        }

        [Fact]
        public void RoundEnded_ReplaysTheWinnerThenTheShortest()
        {
            MatchState match = InRound();
            var replays = match.OnRoundEnded(Result(7), Moves(6), Moves(4));
            Assert.Equal(2, replays.Count);
            Assert.Equal("Bob's solution", replays[0].Label);
            Assert.False(replays[0].IsShortest);
            Assert.Equal(1f, replays[0].StepSeconds);
            Assert.True(replays[1].IsShortest);
            Assert.Equal(0.6f, replays[1].StepSeconds, 3);
            Assert.Equal(MatchStage.RoundOver, match.Stage);
            Assert.Contains("Shortest possible: 4", match.Feedback);
        }

        [Fact]
        public void RoundEnded_ShortestWinnerIsReplayedAlone()
        {
            MatchState match = InRound();
            var replays = match.OnRoundEnded(Result(7, Me, matchWinner: Me), Moves(4), Moves(4));
            Assert.Equal("Your solution", replays.Single().Label);
            Assert.Contains("you win!", match.Feedback);
        }

        [Fact]
        public void RoundEnded_SkippedBoard()
        {
            MatchState match = InRound();
            Assert.True(match.OnRoundEnded(Result(7, null), new Move[0], Moves(5)).Single().IsShortest);

            match = InRound();
            Assert.Empty(match.OnRoundEnded(Result(7, null), new Move[0], null));
        }

        [Fact]
        public void RoundEnded_WinnerWhoLeftScoresNothing_AndOldRoundsAreIgnored()
        {
            MatchState match = InRound();
            Assert.Null(match.OnRoundEnded(Result(6), Moves(5), Moves(5)));
            Assert.Equal(MatchStage.Solving, match.Stage);

            match.OnRoundEnded(Result(7, "Zed"), Moves(5), Moves(5));
            Assert.Contains("but left", match.Feedback);
        }

        [Fact]
        public void RejoiningDuringTheReplays_ShowsTheRoundAsOver()
        {
            // Catch-up order: LobbyState, RoundStarted (the old board), LobbyState again.
            var match = new MatchState();
            Assert.Equal(LobbyChange.None, match.OnLobbyState(Lobby(MatchPhase.Playing, 2, roundOver: true), Me));
            match.OnRoundStarted(8);
            Assert.Equal(LobbyChange.RoundOver, match.OnLobbyState(Lobby(MatchPhase.Playing, 2, roundOver: true), Me));
            Assert.Equal(MatchStage.RoundOver, match.Stage);
            Assert.Equal(DoneAction.SkipReplay, match.DoneAction);
            Assert.False(match.CanLockIn(boardSolved: true));
            Assert.Equal(LobbyChange.None, match.OnLobbyState(Lobby(MatchPhase.Playing, 2, roundOver: true), Me));
        }

        [Fact]
        public void TheUsualRoundOverLobbyState_ChangesNothing()
        {
            MatchState match = InRound();
            match.OnRoundEnded(Result(7), Moves(5), Moves(5));
            Assert.Equal(LobbyChange.None, match.OnLobbyState(Lobby(MatchPhase.Playing, 1, roundOver: true), Me));
        }

        [Fact]
        public void MatchWon_BackToPractice()
        {
            MatchState match = InRound();
            match.OnRoundEnded(Result(7, "Bob", matchWinner: "Bob"), Moves(5), Moves(5));
            Assert.Equal(LobbyChange.BetweenMatches, match.OnLobbyState(Lobby(MatchPhase.Finished, 1, false, "Bob"), Me));
            Assert.Equal(MatchStage.BetweenMatches, match.Stage);
            Assert.Equal("Bob", match.MatchWinner);
            Assert.Equal(0, match.Round);
        }

        [Fact]
        public void TimeLimit_ShowsUntilTheFirstSolution()
        {
            MatchState match = InRound(7);
            Assert.False(match.OnRoundTimeLimit(6, 300f, now: 0f)); // an old round
            Assert.True(match.OnRoundTimeLimit(7, 300f, now: 0f));
            Assert.True(match.HasTimeLimit);
            Assert.Equal(DoneAction.SkipBoard, match.DoneAction);
            Assert.Equal(200f, match.TimeLimitLeft(100f));

            match.OnTimerUpdated(7, 60f, now: 290f);
            Assert.False(match.HasTimeLimit);
            Assert.Equal(60f, match.TimeLeft(290f)); // the full timer, past the limit
            Assert.False(match.TimeJustAdded(290f));
            Assert.False(match.OnRoundTimeLimit(7, 10f, now: 290f));
        }

        [Fact]
        public void TimeLimitRunningOut_SaysSo()
        {
            MatchState match = InRound(7);
            match.OnRoundTimeLimit(7, 300f, now: 0f);
            var result = new RoundResult { RoundNumber = 7, OutOfTime = true, ReplayStepSeconds = 1, ShortestReplayStepSeconds = 1 };
            Assert.Single(match.OnRoundEnded(result, new Move[0], Moves(9)));
            Assert.StartsWith("Time's up", match.Feedback);
            Assert.False(match.HasTimeLimit);
        }

        [Fact]
        public void Standings_HighestScoreFirst_TiesInJoinOrder()
        {
            var match = new MatchState();
            match.OnLobbyState(Lobby(MatchPhase.Waiting, 0, false, null, P("A", 1), P("B", 3), P("C", 1)), Me);
            Assert.Equal(new[] { "B", "A", "C" }, match.Standings.Select(p => p.Name));
        }

        [Fact]
        public void Reconnecting_KeepsTheLobby_GivingUpLeavesIt()
        {
            MatchState match = InRound();
            Assert.False(match.OnConnectionStatus("Connection lost, reconnecting...", inLobby: true, reconnecting: true));
            Assert.Equal("Connection lost, reconnecting...", match.Feedback);
            match.OnRequestFailed("Couldn't start", reconnecting: true);
            Assert.Equal("Connection lost, reconnecting...", match.Feedback);

            Assert.False(match.OnConnectionStatus("Reconnected", inLobby: true, reconnecting: false));
            Assert.Equal("Reconnected!", match.Feedback);
            Assert.Equal(MatchStage.Solving, match.Stage);

            Assert.True(match.OnConnectionStatus("Disconnected", inLobby: false, reconnecting: false));
            Assert.Equal(MatchStage.NotInLobby, match.Stage);
            Assert.False(match.OnConnectionStatus("Disconnected", inLobby: false, reconnecting: false));

            // Joining again starts afresh.
            Assert.Equal(LobbyChange.BetweenMatches, match.OnLobbyState(Lobby(), Me));
        }
    }
}
