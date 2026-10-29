using SharedLogic;

namespace SharedLogic.Tests;

public class SolverTests
{
    private static GridPos P(int x, int y) => new GridPos(x, y);

    // Red at the start position; the others parked along the top row out of the way.
    private static RobotPositions RedAt(GridPos red) =>
        new RobotPositions(red, P(13, 15), P(14, 15), P(15, 15));

    [Fact]
    public void RobotsThatMayNotMove_StandStill()
    {
        // Red needs blue as a blocker (see UsesAnotherRobotAsABlocker); with blue standing still it can't be done in 2.
        var grid = new GridState();
        var robots = new RobotPositions(P(0, 0), P(6, 15), P(0, 15), P(1, 15));
        var target = new Target(P(5, 0), RobotColor.Red);
        int redOnly = 1 << (int)RobotColor.Red;

        Assert.False(Solver.Solve(grid, robots, target, maxMoves: 2, movableRobots: redOnly).Solved);
        Assert.False(Solver.Solve(grid, robots, target, movableRobots: 1 << (int)RobotColor.Blue).Solved); // red must move
        Assert.Equal(2, Solver.Solve(grid, robots, target, movableRobots: redOnly | 1 << (int)RobotColor.Blue).Moves.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void FewestRobots_IsTheSmallestGroupThatSolvesItAsQuickly(int seed)
    {
        RoundSetup round = BoardGenerator.GenerateRound(new Random(seed));
        Move[] shortest = Solver.Solve(round).Moves;
        int fewest = Solver.FewestRobots(round, shortest.Length);

        Assert.InRange(fewest, 1, shortest.Select(m => m.Robot).Distinct().Count());
        // Some group of that size solves it in the shortest length using only its own robots; no smaller group does.
        bool any = false;
        for (int group = 1; group < Solver.AllRobots; group++)
        {
            int size = System.Numerics.BitOperations.PopCount((uint)group);
            if (size > fewest)
                continue;
            SolverResult result = Solver.Solve(round, shortest.Length, movableRobots: group);
            if (size < fewest)
                Assert.False(result.Solved);
            else if (result.Solved)
            {
                any = true;
                Assert.Equal(shortest.Length, result.Moves.Length);
                Assert.All(result.Moves, m => Assert.True((group & (1 << (int)m.Robot)) != 0));
                Assert.Equal(SolutionResult.Valid, GameRules.ValidateSolution(round.Grid, round.Robots, round.Target, result.Moves));
            }
        }
        Assert.True(any);
    }

    [Fact]
    public void FindsASingleSlide()
    {
        var grid = new GridState();
        var target = new Target(P(15, 0), RobotColor.Red);

        SolverResult result = Solver.Solve(grid, RedAt(P(0, 0)), target);

        Assert.Equal(new[] { new Move(RobotColor.Red, Direction.Right) }, result.Moves);
    }

    [Fact]
    public void UsesAnotherRobotAsABlocker()
    {
        // Red can't stop at (5, 0) on its own; blue has to drop down to (6, 0) first.
        var grid = new GridState();
        var robots = new RobotPositions(P(0, 0), P(6, 15), P(0, 15), P(1, 15));
        var target = new Target(P(5, 0), RobotColor.Red);

        SolverResult result = Solver.Solve(grid, robots, target);

        Assert.Equal(new[] { new Move(RobotColor.Blue, Direction.Down), new Move(RobotColor.Red, Direction.Right) },
            result.Moves);
    }

    [Fact]
    public void WildcardTargetAcceptsWhicheverRobotIsQuickest()
    {
        var grid = new GridState();
        var robots = new RobotPositions(P(3, 3), P(7, 7), P(0, 15), P(15, 15));
        var target = new Target(P(15, 0), null);

        SolverResult result = Solver.Solve(grid, robots, target);

        // Yellow (15, 15) just slides down.
        Assert.Equal(new[] { new Move(RobotColor.Yellow, Direction.Down) }, result.Moves);
    }

    [Fact]
    public void ReportsAnUnreachableTargetWithoutHittingTheLimit()
    {
        GridState grid = BoardGenerator.Build(Quadrant.All);
        var target = new Target(P(7, 7), RobotColor.Red); // inside the walled-off centre

        SolverResult result = Solver.Solve(grid, RedAt(P(0, 0)), target);

        Assert.False(result.Solved);
        Assert.False(result.HitLimit);
    }

    [Fact]
    public void StopsAtTheMoveLimit()
    {
        var grid = new GridState();
        var robots = new RobotPositions(P(0, 0), P(6, 15), P(0, 15), P(1, 15));
        var target = new Target(P(5, 0), RobotColor.Red); // needs 2 moves

        SolverResult result = Solver.Solve(grid, robots, target, maxMoves: 1);

        Assert.False(result.Solved);
        Assert.True(result.HitLimit);
    }

    public static TheoryData<int> Seeds()
    {
        var seeds = new TheoryData<int>();
        for (int seed = 1; seed <= 60; seed++)
            seeds.Add(seed);
        return seeds;
    }

    // Every answer must be a legal solution, and as short as a brute-force breadth-first search finds.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void MatchesBruteForceOnGeneratedBoards(int seed)
    {
        RoundSetup round = BoardGenerator.GenerateRound(new Random(seed));
        const int bruteForceDepth = 6;

        SolverResult result = Solver.Solve(round, maxMoves: 20, maxNodes: 50_000_000);
        int? shortest = BruteForceShortest(round, bruteForceDepth);

        Assert.True(result.Solved, "solver found nothing");
        Assert.Equal(SolutionResult.Valid,
            GameRules.ValidateSolution(round.Grid, round.Robots, round.Target, result.Moves));
        if (shortest.HasValue)
            Assert.Equal(shortest.Value, result.Moves.Length);
        else
            Assert.True(result.Moves.Length > bruteForceDepth);
    }

    private static int? BruteForceShortest(RoundSetup round, int maxDepth)
    {
        var frontier = new List<RobotPositions> { round.Robots };
        var seen = new HashSet<RobotPositions> { round.Robots };
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var next = new List<RobotPositions>();
            foreach (RobotPositions robots in frontier)
            {
                foreach (RobotColor color in Enum.GetValues<RobotColor>())
                {
                    foreach (Direction dir in Enum.GetValues<Direction>())
                    {
                        if (!GameRules.TryMove(round.Grid, robots, new Move(color, dir), out RobotPositions moved))
                            continue;
                        if (round.Target.AcceptsRobot(color) && moved[color] == round.Target.Position)
                            return depth;
                        if (seen.Add(moved))
                            next.Add(moved);
                    }
                }
            }
            frontier = next;
        }
        return null;
    }
}
