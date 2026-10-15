using System;
using System.Linq;
using Ricochet.Client.Game;
using SharedLogic;
using Xunit;

namespace ClientLogic.Tests
{
    /// <summary>The match history and watching the current best during a round.</summary>
    public class HistoryTests
    {
        private const string Me = "Alice";
        private static readonly RoundSetup Board = BoardGenerator.GenerateRound(new Random(42));
        private static readonly Move[] Shortest = Solver.Solve(Board).Moves;

        private static LobbyState Lobby(MatchPhase phase, int matchRound = 0) => new LobbyState
        {
            Code = "TEST",
            Host = Me,
            Phase = phase,
            Settings = new MatchSettings { PointsToWin = 5, FirstTimerSeconds = 60, ExtensionSeconds = 45 },
            MatchRound = matchRound,
            Players = new[] { new PlayerInfo { Name = Me, Connected = true }, new PlayerInfo { Name = "Bob", Connected = true } },
        };

        private static MatchState InRound(int round)
        {
            var match = new MatchState();
            match.OnLobbyState(Lobby(MatchPhase.Waiting), Me);
            match.OnLobbyState(Lobby(MatchPhase.Playing), Me);
            match.OnRoundStarted(round);
            return match;
        }

        private static RoundRecord Record(int matchRound) => new RoundRecord
        {
            MatchRound = matchRound,
            Board = BoardCodec.Encode(Board),
            WinnerName = "Bob",
            Solutions = new[]
            {
                new PlayerSolution { Name = "Bob", Moves = MoveCodec.Encode(Shortest) },
                new PlayerSolution { Name = Me, Moves = MoveCodec.Encode(Shortest.Concat(Shortest).ToArray()) },
            },
            ShortestMoves = MoveCodec.Encode(Shortest),
        };

        private static RoundResult Result(int round, int matchRound) => new RoundResult
        {
            RoundNumber = round, WinnerName = "Bob", WinnerMoves = MoveCodec.Encode(Shortest),
            ShortestMoves = MoveCodec.Encode(Shortest), ReplayStepSeconds = 1, ShortestReplayStepSeconds = 0.6,
            Record = Record(matchRound),
        };

        [Fact]
        public void RoundEnded_AddsTheBoardToTheHistory_Decoded()
        {
            MatchState match = InRound(7);
            match.OnRoundEnded(Result(7, 1), Shortest, Shortest);

            HistoryRound round = Assert.Single(match.History);
            Assert.Equal(1, round.MatchRound);
            Assert.Equal("Bob", round.WinnerName);
            Assert.Equal(new[] { "Bob", Me }, round.Solutions.Select(s => s.Name));
            Assert.Equal(Shortest, round.Solutions[0].Moves);
            Assert.Equal(Shortest, round.Shortest);
            Assert.Equal(BoardCodec.Encode(Board), BoardCodec.Encode(round.Board));
        }

        [Fact]
        public void CatchUp_ReplacesTheHistory_AndOverlapsDontDuplicate()
        {
            MatchState match = InRound(8);
            match.OnMatchHistory(new[] { Record(2), Record(1) });
            match.OnRoundEnded(Result(8, 2), Shortest, Shortest);

            Assert.Equal(new[] { 1, 2 }, match.History.Select(r => r.MatchRound));
        }

        [Fact]
        public void ANewMatch_ClearsTheHistory_ButTheResultsKeepIt()
        {
            MatchState match = InRound(7);
            match.OnRoundEnded(Result(7, 1), Shortest, Shortest);
            match.OnLobbyState(Lobby(MatchPhase.Finished, 1), Me);
            Assert.Single(match.History);

            match.OnLobbyState(Lobby(MatchPhase.Playing), Me);
            Assert.Empty(match.History);
        }

        [Fact]
        public void WatchingTheBest_OnlyWhenShown_AndSomeoneElseLeads()
        {
            MatchState match = InRound(7);
            match.OnNewBest(7, "Bob", 9, boardSolution: -1);
            Assert.False(match.CanWatchBest); // lobby doesn't show solutions

            match.OnNewBest(7, "Bob", Shortest.Length, boardSolution: -1, Shortest);
            Assert.True(match.CanWatchBest);
            Assert.Equal(Shortest, match.BestSolution);

            match.OnNewBest(7, Me, 2, boardSolution: -1, Shortest);
            Assert.False(match.CanWatchBest); // it's mine

            match.OnNewBest(7, "Bob", 1, boardSolution: -1, Shortest);
            match.OnRoundEnded(Result(7, 1), Shortest, Shortest);
            Assert.False(match.CanWatchBest);
        }

        [Fact]
        public void Restore_PutsTheAttemptBack_AfterWatchingAReplay()
        {
            var board = new BoardSession();
            board.Load(Board, practice: false, seed: 0);
            Move first = Shortest[0];
            board.Select(first.Robot);
            Assert.True(board.TryMove(first.Direction));
            RobotPositions attempt = board.Robots;
            Move[] mine = board.Moves.ToArray();

            board.BeginReplay(new ReplaySegment("Bob", Shortest, 0.6f));
            while (board.ReplayStep < Shortest.Length)
                board.ReplayNextMove();
            board.FinishReplays(lockUntilNextBoard: true);
            Assert.False(board.AcceptsInput);

            board.Restore(mine);
            Assert.Equal(BoardMode.Round, board.Mode);
            Assert.True(board.AcceptsInput);
            Assert.Equal(attempt, board.Robots);
            Assert.Equal(mine, board.Moves);
            Assert.False(board.IsSolved);
        }
    }
}
