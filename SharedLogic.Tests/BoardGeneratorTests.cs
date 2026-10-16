using SharedLogic;

namespace SharedLogic.Tests;

public class BoardGeneratorTests
{
    private static GridPos P(int x, int y) => new GridPos(x, y);

    private static readonly Direction[] AllDirections =
        { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

    public static TheoryData<int> Seeds() => new() { 1, 2, 3, 42, 1234 };

    private static IEnumerable<Quadrant> AllVariants => Quadrant.Slots.SelectMany(slot => slot);

    [Fact]
    public void QuadrantTargetsStayClearOfEdgesSeamsAndEachOther()
    {
        foreach (Quadrant quadrant in AllVariants)
        {
            foreach (Target target in quadrant.Targets)
            {
                GridPos p = target.Position;
                Assert.InRange(p.X, 1, Quadrant.Size - 2);
                Assert.InRange(p.Y, 1, Quadrant.Size - 2);

                foreach (Target other in quadrant.Targets)
                {
                    if (other.Position == p) continue;
                    int distance = Math.Max(Math.Abs(other.Position.X - p.X), Math.Abs(other.Position.Y - p.Y));
                    Assert.True(distance > 1, $"{quadrant.Name}: {target} touches {other}");
                }
            }
        }
    }

    [Fact]
    public void EveryVariantFollowsTheSlotRules()
    {
        Assert.Equal(4, Quadrant.Slots.Count);
        Assert.Equal(AllVariants.Count(), AllVariants.Select(q => q.Name).Distinct().Count());
        for (int slot = 0; slot < Quadrant.Slots.Count; slot++)
        {
            Assert.InRange(Quadrant.Slots[slot].Count, 2, 3);
            foreach (Quadrant quadrant in Quadrant.Slots[slot])
            {
                foreach (RobotColor color in Enum.GetValues<RobotColor>())
                    Assert.Single(quadrant.Targets, t => t.Robot == color);
                // Only the third slot has the wildcard, so every board gets exactly one.
                Assert.Equal(slot == 2 ? 1 : 0, quadrant.Targets.Count(t => t.Robot == null));
                // Two walls per target plus the two edge stubs: every variant is about as busy.
                Assert.Equal(quadrant.Targets.Count * 2 + 2, quadrant.Walls.Count);
            }
        }
        Assert.Same(Quadrant.Slots[0][0], Quadrant.All[0]);
    }

    [Fact]
    public void EveryMixOfVariantsAndCornersMakesAValidBoard()
    {
        int last = BoardGenerator.BoardSize - 1;
        var centre = new[] { P(7, 7), P(7, 8), P(8, 7), P(8, 8) };
        foreach (Quadrant a in Quadrant.Slots[0])
        foreach (Quadrant b in Quadrant.Slots[1])
        foreach (Quadrant c in Quadrant.Slots[2])
        foreach (Quadrant d in Quadrant.Slots[3])
        foreach (Quadrant[] order in Orders(new[] { a, b, c, d }))
        {
            GridState grid = BoardGenerator.Build(order);
            string name = string.Join(", ", order.Select(q => q.Name));

            Assert.Equal(17, grid.Targets.Select(t => t.Position).Distinct().Count());
            for (int x = 0; x <= last; x++)
            for (int y = 0; y <= last; y++)
            {
                if (centre.Contains(P(x, y))) continue;
                // No dead-end pockets: a cell walled on three sides (board edges count) would be a trap.
                int blocked = AllDirections.Count(dir => grid.IsBlocked(P(x, y), dir));
                Assert.True(blocked <= 2, $"{name}: ({x}, {y}) is blocked on {blocked} sides");
            }
        }
    }

    private static IEnumerable<Quadrant[]> Orders(Quadrant[] items)
    {
        if (items.Length == 1)
        {
            yield return items;
            yield break;
        }
        for (int i = 0; i < items.Length; i++)
            foreach (Quadrant[] rest in Orders(items.Where((_, j) => j != i).ToArray()))
                yield return new[] { items[i] }.Concat(rest).ToArray();
    }

    [Fact]
    public void GeneratedBoardsUseMoreThanTheOriginalLayouts()
    {
        var random = new Random(5);
        var layouts = new HashSet<string>();
        for (int i = 0; i < 300; i++)
        {
            RoundSetup round = BoardGenerator.GenerateRound(random);
            // The wire format minus its last 9 bytes (robots + active target): just the walls and targets.
            layouts.Add(Convert.ToBase64String(BoardCodec.Encode(round), 0, 128 - 9));
        }
        Assert.True(layouts.Count > 24, $"only {layouts.Count} layouts");
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
