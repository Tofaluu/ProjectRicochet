using SharedLogic;

namespace SharedLogic.Tests;

public class BoardGeneratorTests
{
    private static GridPos P(int x, int y) => new GridPos(x, y);

    private static readonly Direction[] AllDirections =
        { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

    public static TheoryData<int> Seeds() => new() { 1, 2, 3, 42, 1234 };

    [Fact]
    public void QuadrantTargetsStayClearOfEdgesSeamsAndEachOther()
    {
        foreach (Quadrant quadrant in Quadrant.All)
        {
            foreach (Target target in quadrant.Targets)
            {
                GridPos p = target.Position;
                Assert.InRange(p.X, 1, Quadrant.Size - 2);
                Assert.InRange(p.Y, 1, Quadrant.Size - 2);

                foreach (Target other in quadrant.Targets)
                {
                    if (other.Position == p) continue;
                    int distance = Math.Abs(other.Position.X - p.X) + Math.Abs(other.Position.Y - p.Y);
                    Assert.True(distance > 1, $"{quadrant.Name}: {target} touches {other}");
                }
            }
        }
    }

    [Fact]
    public void FullBoardHasOneTargetPerColourPerQuadrantPlusWildcard()
    {
        GridState grid = BoardGenerator.Build(Quadrant.All);

        Assert.Equal(17, grid.Targets.Count);
        Assert.Equal(17, grid.Targets.Select(t => t.Position).Distinct().Count());
        foreach (RobotColor color in Enum.GetValues<RobotColor>())
            Assert.Equal(4, grid.Targets.Count(t => t.Robot == color));
        Assert.Single(grid.Targets, t => t.Robot == null);
    }

    [Fact]
    public void EveryTargetSitsInAnLShapedWallCornerAfterRotation()
    {
        GridState grid = BoardGenerator.Build(Quadrant.All);

        foreach (Target target in grid.Targets)
        {
            Walls walls = grid.GetWalls(target.Position);
            Assert.True(
                walls == (Walls.North | Walls.East) || walls == (Walls.East | Walls.South) ||
                walls == (Walls.South | Walls.West) || walls == (Walls.West | Walls.North),
                $"{target} has walls {walls}");
        }
    }

    [Fact]
    public void QuadrantsAreRotatedIntoTheirCorners()
    {
        // Alpha's red target is at (1, 2) with walls Up + Right. A quarter turn clockwise
        // per corner maps (x, y) -> (y, 15 - x) and Up -> Right -> Down -> Left.
        var alphaEverywhere = new[] { Quadrant.All[0], Quadrant.All[0], Quadrant.All[0], Quadrant.All[0] };
        GridState grid = BoardGenerator.Build(alphaEverywhere);

        Assert.Equal(Walls.North | Walls.East, grid.GetWalls(P(1, 2)));   // bottom-left
        Assert.Equal(Walls.East | Walls.South, grid.GetWalls(P(2, 14)));  // top-left
        Assert.Equal(Walls.South | Walls.West, grid.GetWalls(P(14, 13))); // top-right
        Assert.Equal(Walls.West | Walls.North, grid.GetWalls(P(13, 1)));  // bottom-right
    }

    [Fact]
    public void CentreIsWalledOffAndEachEdgeHasTwoStubs()
    {
        GridState grid = BoardGenerator.Build(Quadrant.All);

        foreach (GridPos centre in new[] { P(7, 7), P(7, 8), P(8, 7), P(8, 8) })
            Assert.All(AllDirections, d => Assert.True(grid.IsBlocked(centre, d)));

        int last = BoardGenerator.BoardSize - 1;
        int bottom = 0, top = 0, left = 0, right = 0;
        for (int i = 0; i < last; i++)
        {
            if (grid.IsBlocked(P(i, 0), Direction.Right)) bottom++;
            if (grid.IsBlocked(P(i, last), Direction.Right)) top++;
            if (grid.IsBlocked(P(0, i), Direction.Up)) left++;
            if (grid.IsBlocked(P(last, i), Direction.Up)) right++;
        }
        Assert.Equal(new[] { 2, 2, 2, 2 }, new[] { bottom, top, left, right });
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RobotsLandOnDistinctFreeCells(int seed)
    {
        var random = new Random(seed);
        for (int round = 0; round < 50; round++)
        {
            RoundSetup setup = BoardGenerator.GenerateRound(random);
            var positions = Enum.GetValues<RobotColor>().Select(c => setup.Robots[c]).ToList();

            Assert.Equal(4, positions.Distinct().Count());
            foreach (GridPos pos in positions)
            {
                Assert.DoesNotContain(setup.Grid.Targets, t => t.Position == pos);
                Assert.Contains(AllDirections, d => !setup.Grid.IsBlocked(pos, d));
            }
        }
    }

    [Fact]
    public void GenerationIsDeterministicForASeed()
    {
        byte[] a = BoardCodec.Encode(BoardGenerator.GenerateRound(new Random(7)));
        byte[] b = BoardCodec.Encode(BoardGenerator.GenerateRound(new Random(7)));
        Assert.Equal(a, b);
    }
}
