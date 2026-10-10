using SharedLogic;

namespace SharedLogic.Tests;

public class GameRulesTests
{
    private static GridPos P(int x, int y) => new GridPos(x, y);

    // Parks the three robots we don't care about in the top row, out of the way.
    private static RobotPositions RedAt(GridPos red) =>
        new RobotPositions(red, P(13, 15), P(14, 15), P(15, 15));

    [Theory]
    [InlineData(Direction.Up, 5, 15)]
    [InlineData(Direction.Down, 5, 0)]
    [InlineData(Direction.Left, 0, 5)]
    [InlineData(Direction.Right, 15, 5)]
    public void SlidesToBoardEdgeOnEmptyBoard(Direction dir, int x, int y)
    {
        var grid = new GridState();
        Assert.Equal(P(x, y), GameRules.ComputeStop(grid, RedAt(P(5, 5)), RobotColor.Red, dir));
    }

    [Fact]
    public void StopsInFrontOfWallOnOwnCell()
    {
        var grid = new GridState();
        grid.AddWall(P(5, 9), Direction.Up);

        Assert.Equal(P(5, 9), GameRules.ComputeStop(grid, RedAt(P(5, 5)), RobotColor.Red, Direction.Up));
    }

    [Fact]
    public void WallBlocksFromBothSides()
    {
        var grid = new GridState();
        grid.AddWall(P(5, 9), Direction.Up); // between (5,9) and (5,10)

        Assert.Equal(P(5, 10), GameRules.ComputeStop(grid, RedAt(P(5, 14)), RobotColor.Red, Direction.Down));
        Assert.Equal(Walls.South, grid.GetWalls(P(5, 10)));
    }

    [Fact]
    public void StopsBeforeAnotherRobot()
    {
        var grid = new GridState();
        var robots = new RobotPositions(P(2, 3), P(9, 3), P(0, 15), P(15, 15));

        Assert.Equal(P(8, 3), GameRules.ComputeStop(grid, robots, RobotColor.Red, Direction.Right));
    }

    [Fact]
    public void BlockedRobotDoesNotMove()
    {
        var grid = new GridState();
        grid.AddWall(P(4, 4), Direction.Left);
        var robots = RedAt(P(4, 4));

        Assert.False(GameRules.TryMove(grid, robots, new Move(RobotColor.Red, Direction.Left), out var result));
        Assert.Equal(robots, result);
    }

    [Fact]
    public void TryMoveOnlyMovesTheChosenRobot()
    {
        var grid = new GridState();
        var robots = RedAt(P(4, 4));

        Assert.True(GameRules.TryMove(grid, robots, new Move(RobotColor.Red, Direction.Down), out var result));
        Assert.Equal(robots.With(RobotColor.Red, P(4, 0)), result);
    }

    [Fact]
    public void BlockedCentreCannotBeEntered()
    {
        var grid = new GridState();
        grid.AddBlockedArea(P(7, 7), 2, 2);

        Assert.Equal(P(7, 6), GameRules.ComputeStop(grid, RedAt(P(7, 0)), RobotColor.Red, Direction.Up));
        Assert.Equal(P(9, 8), GameRules.ComputeStop(grid, RedAt(P(15, 8)), RobotColor.Red, Direction.Left));
    }

    [Fact]
    public void ValidSolutionUsingAnotherRobotAsBlocker()
    {
        // Target at (10, 0). Red at (0, 0) would slide past it to (15, 0),
        // so first drop Blue down to (11, 0) as a blocker.
        var grid = new GridState();
        var target = new Target(P(10, 0), RobotColor.Red);
        var start = new RobotPositions(P(0, 0), P(11, 12), P(0, 15), P(15, 15));
        var moves = new[]
        {
            new Move(RobotColor.Blue, Direction.Down),
            new Move(RobotColor.Red, Direction.Right),
        };

        Assert.Equal(SolutionResult.Valid, GameRules.ValidateSolution(grid, start, target, moves));
        Assert.Equal(SolutionResult.TargetNotReached,
            GameRules.ValidateSolution(grid, start, target, new[] { moves[1] }));
    }

    [Fact]
    public void WrongRobotOnTargetIsNotValid()
    {
        var grid = new GridState();
        grid.AddWall(P(10, 0), Direction.Right);
        var start = RedAt(P(0, 0));
        var moves = new[] { new Move(RobotColor.Red, Direction.Right) };

        Assert.Equal(SolutionResult.TargetNotReached,
            GameRules.ValidateSolution(grid, start, new Target(P(10, 0), RobotColor.Blue), moves));
        Assert.Equal(SolutionResult.Valid,
            GameRules.ValidateSolution(grid, start, new Target(P(10, 0), null), moves));
    }

    [Fact]
    public void NoOpMoveInvalidatesSolution()
    {
        var grid = new GridState();
        grid.AddWall(P(10, 0), Direction.Right);
        var start = RedAt(P(0, 0));
        var moves = new[]
        {
            new Move(RobotColor.Red, Direction.Down), // already on the bottom edge
            new Move(RobotColor.Red, Direction.Right),
        };

        Assert.Equal(SolutionResult.NoOpMove,
            GameRules.ValidateSolution(grid, start, new Target(P(10, 0), RobotColor.Red), moves));
    }

    [Fact]
    public void EmptySolutionIsRejected()
    {
        Assert.Equal(SolutionResult.Empty,
            GameRules.ValidateSolution(new GridState(), RedAt(P(0, 0)), new Target(P(0, 0), null), new Move[0]));
    }

    [Fact]
    public void OutOfBoundsAccessThrows()
    {
        var grid = new GridState();
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.AddWall(P(16, 0), Direction.Up));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.AddTarget(new Target(P(-1, 0), null)));
    }
}
