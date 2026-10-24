using Ricochet.Client.Game;
using SharedLogic;
using Xunit;

namespace ClientLogic.Tests
{
    public class TutorialStateTests
    {
        private static readonly Move GreenRight = new Move(RobotColor.Green, Direction.Right);
        private static readonly Move GreenDown = new Move(RobotColor.Green, Direction.Down);
        private static readonly Move RedUp = new Move(RobotColor.Red, Direction.Up);

        private static RobotPositions After(RoundSetup round, params Move[] moves)
        {
            RobotPositions robots = round.Robots;
            foreach (Move move in moves)
                Assert.True(GameRules.TryMove(round.Grid, robots, move, out robots));
            return robots;
        }

        [Fact]
        public void Board_IsSolvedInThreeWithTheBlocker_AndGreenRightAloneOvershoots()
        {
            RoundSetup round = TutorialState.Board();
            SolverResult result = Solver.Solve(round);
            Assert.True(result.Solved);
            Assert.Equal(TutorialState.BestMoves, result.Moves.Length);

            Assert.Equal(TutorialState.Overshoot, After(round, GreenRight)[RobotColor.Green]);
            Assert.Equal(TutorialState.RedBlock, After(round, RedUp)[RobotColor.Red]);
            Assert.Equal(TutorialState.AboveTarget, After(round, RedUp, GreenRight)[RobotColor.Green]);
            Assert.Equal(SolutionResult.Valid, GameRules.ValidateSolution(round.Grid, round.Robots, round.Target, new[] { RedUp, GreenRight, GreenDown }));
        }

        [Fact]
        public void Steps_FollowTheLesson()
        {
            var tutorial = new TutorialState();
            tutorial.Start();
            RoundSetup round = tutorial.Round;

            tutorial.Update(round.Robots, 0, false);
            Assert.Equal(TutorialStep.Slide, tutorial.Step);
            Assert.Equal(RobotColor.Green, tutorial.RobotToSelect);

            Assert.True(tutorial.Update(After(round, GreenRight), 1, false));
            Assert.Equal(TutorialStep.Overshot, tutorial.Step);
            Assert.Equal(1, tutorial.Progress);

            // Undo back to the start: now the blocker, red selected.
            tutorial.Update(round.Robots, 0, false);
            Assert.Equal(TutorialStep.Blocker, tutorial.Step);
            Assert.Equal(RobotColor.Red, tutorial.RobotToSelect);

            tutorial.Update(After(round, RedUp), 1, false);
            Assert.Equal(TutorialStep.MoveRight, tutorial.Step);
            Assert.Equal(RobotColor.Green, tutorial.RobotToSelect);
            tutorial.Update(After(round, RedUp, GreenRight), 2, false);
            Assert.Equal(TutorialStep.MoveDown, tutorial.Step);

            tutorial.Update(After(round, RedUp, GreenRight, GreenDown), 3, true);
            Assert.Equal(TutorialStep.Solved, tutorial.Step);
            Assert.Equal(3, tutorial.Progress);
            Assert.Equal("Solved in 3 moves!", tutorial.Title(3));
        }

        [Fact]
        public void AnyOtherPosition_AsksForAReset()
        {
            var tutorial = new TutorialState();
            tutorial.Start();
            tutorial.Update(After(tutorial.Round, GreenDown), 1, false);
            Assert.Equal(TutorialStep.Astray, tutorial.Step);
            Assert.Contains("Press R", tutorial.Body(touch: false, moves: 1));
            Assert.Contains("Tap Reset", tutorial.Body(touch: true, moves: 1));
        }

        [Fact]
        public void BlockerFirst_SkipsStraightToGreen()
        {
            var tutorial = new TutorialState();
            tutorial.Start();
            tutorial.Update(After(tutorial.Round, RedUp), 1, false);
            Assert.Equal(TutorialStep.MoveRight, tutorial.Step);
        }
    }
}
