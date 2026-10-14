using System;
using Ricochet.Client.Game;
using SharedLogic;
using Xunit;

namespace ClientLogic.Tests
{
    public class BoardSessionTests
    {
        private static readonly RoundSetup Round = BoardGenerator.GenerateRound(new Random(42));
        private static readonly Move[] Shortest = Solver.Solve(Round).Moves;

        private static BoardSession Loaded(bool practice = false)
        {
            var board = new BoardSession();
            board.Load(Round, practice, practice ? 42 : 0);
            return board;
        }

        private static void Play(BoardSession board, Move[] moves)
        {
            foreach (Move move in moves)
            {
                board.Select(move.Robot);
                Assert.True(board.TryMove(move.Direction));
            }
        }

        [Fact]
        public void Load_PracticeOrRound()
        {
            BoardSession board = Loaded(practice: true);
            Assert.Equal(BoardMode.Practice, board.Mode);
            Assert.Equal(42, board.Seed);
            Assert.True(board.AcceptsInput);
            Assert.Equal(Round.Target.Robot ?? RobotColor.Red, board.Selected);

            board.Load(Round, practice: false);
            Assert.Equal(BoardMode.Round, board.Mode);
            Assert.Equal(0, board.Seed);
        }

        [Fact]
        public void PlayingTheSolution_SolvesTheBoard_AndUndoUnsolvesIt()
        {
            BoardSession board = Loaded();
            Play(board, Shortest);
            Assert.True(board.IsSolved);
            Assert.Equal(Shortest, board.Moves);
            Assert.False(board.TryMove(Direction.Up)); // nothing more once solved

            Assert.True(board.Undo());
            Assert.False(board.IsSolved);
            Assert.Equal(Shortest.Length - 1, board.Moves.Count);

            board.Reset();
            Assert.Empty(board.Moves);
            Assert.Equal(Round.Robots, board.Robots);
            Assert.False(board.Undo());
        }

        [Fact]
        public void Replay_LocksTheBoardUntilTheNextOne()
        {
            BoardSession board = Loaded();
            board.TryMove(Direction.Up);
            board.BeginReplay(new ReplaySegment("Bob's solution", Shortest, 1f));
            Assert.Equal(BoardMode.PlayerReplay, board.Mode);
            Assert.Empty(board.Moves); // starts from the round's start
            Assert.False(board.AcceptsInput);

            while (board.ReplayStep < Shortest.Length)
                board.ReplayNextMove();
            Assert.Equal(Shortest, board.Moves);

            board.BeginReplay(new ReplaySegment("Shortest possible", Shortest, 0.6f, isShortest: true));
            Assert.Equal(BoardMode.ShortestReplay, board.Mode);
            board.FinishReplays(lockUntilNextBoard: true);
            Assert.True(board.ReplayFinished);
            Assert.Equal(BoardMode.ShortestReplay, board.Mode);
            Assert.False(board.AcceptsInput);

            board.Load(Round, practice: false);
            Assert.Equal(BoardMode.Round, board.Mode);
            Assert.True(board.AcceptsInput);
        }

        [Fact]
        public void UnlockedReplay_StaysShown_ThenTheFirstMoveStartsFresh()
        {
            BoardSession board = Loaded(practice: true);
            board.BeginReplay(new ReplaySegment("Shortest solution", Shortest, 0.6f, isShortest: true));
            while (board.ReplayStep < Shortest.Length)
                board.ReplayNextMove();
            board.FinishReplays(lockUntilNextBoard: false);
            Assert.Equal(BoardMode.ShortestReplay, board.Mode);
            Assert.True(board.AcceptsInput);

            // The replayed moves aren't the player's: the first move starts again from the round's start.
            Assert.True(board.TryMove(Direction.Up));
            Assert.Equal(BoardMode.Practice, board.Mode);
            Assert.Single(board.Moves);
            Assert.False(board.IsSolved);
        }

        [Fact]
        public void UnlockedReplay_UndoOrReset_GoesBackToTheStart()
        {
            BoardSession board = Loaded(practice: true);
            board.BeginReplay(new ReplaySegment("Shortest solution", Shortest, 0.6f, isShortest: true));
            while (board.ReplayStep < Shortest.Length)
                board.ReplayNextMove();
            board.FinishReplays(lockUntilNextBoard: false);

            Assert.True(board.Undo());
            Assert.Equal(BoardMode.Practice, board.Mode);
            Assert.Empty(board.Moves);
            Assert.Equal(Round.Robots, board.Robots);
        }

        [Fact]
        public void Countdown_BlocksInputUntilTheNextBoard()
        {
            BoardSession board = Loaded();
            board.StartCountdown(now: 5f, seconds: 3f, caption: "Round 2 starts in");
            Assert.False(board.AcceptsInput);
            Assert.Equal(1f, board.CountdownLeft(7f));
            Assert.Equal(0f, board.CountdownLeft(9f));

            board.Load(Round, practice: false);
            Assert.False(board.IsCountingDown);
            Assert.True(board.AcceptsInput);
        }

        [Fact]
        public void EndedRound_IsLockedWithoutAReplay()
        {
            BoardSession board = Loaded();
            board.EndRound();
            Assert.Equal(BoardMode.RoundOver, board.Mode);
            Assert.False(board.AcceptsInput);

            board.BeginReplay(new ReplaySegment("Bob's solution", Shortest, 1f));
            Assert.Equal(BoardMode.PlayerReplay, board.Mode); // a replay shows over it

            board.Load(Round, practice: false);
            Assert.Equal(BoardMode.Round, board.Mode);
        }
    }
}
