using SharedLogic;

namespace SharedLogic.Tests;

public class BoardSearchTests
{
    private static BoardSearch Run(int minMoves, int seed, TimeSpan? solverTimeLimit = null)
    {
        var search = new BoardSearch(minMoves, new Random(seed), solverTimeLimit);
        for (int i = 0; i < 20000 && !search.Step(); i++) { }
        return search;
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(10, 2)]
    [InlineData(12, 3)]
    public void FindsABoardWithAtLeastTheMinimum(int minMoves, int seed)
    {
        BoardSearch search = Run(minMoves, seed);

        Assert.True(search.Found);
        Assert.True(search.LongestMoves.Length >= minMoves);
        // The moves are the board's real shortest solution.
        Assert.Equal(search.LongestMoves.Length, Solver.Solve(search.Longest).Moves.Length);
        Assert.Equal(SolutionResult.Valid, GameRules.ValidateSolution(
            search.Longest.Grid, search.Longest.Robots, search.Longest.Target, search.LongestMoves));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(6, 2)]
    [InlineData(11, 3)]
    public void FindsABoardOfAnExactLength(int moves, int seed)
    {
        var search = new BoardSearch(moves, new Random(seed), null, maxMoves: moves);
        for (int i = 0; i < 20000 && !search.Step(); i++) { }

        Assert.True(search.Found);
        Assert.Equal(moves, search.LongestMoves.Length);
        Assert.Equal(moves, Solver.Solve(search.Longest).Moves.Length);
    }

    [Fact]
    public void NeverKeepsABoardPastTheMaximum()
    {
        var search = new BoardSearch(14, new Random(7), TimeSpan.FromSeconds(0.2), maxMoves: 14);
        for (int i = 0; i < 60 && !search.Step(); i++)
            Assert.True(search.Longest == null || search.LongestMoves.Length <= 14);
    }

    [Fact]
    public void KeepsTheLongestBoardWhileSearching()
    {
        var search = new BoardSearch(BoardGenerator.BoardSize * 4, new Random(4), TimeSpan.FromSeconds(0.2)); // never met
        int longest = 0;
        for (int i = 0; i < 50; i++)
        {
            Assert.False(search.Step());
            if (search.Longest == null) continue;
            Assert.True(search.LongestMoves.Length >= longest);
            longest = search.LongestMoves.Length;
        }
        Assert.True(longest >= 5);
    }

    [Fact]
    public void SkipsBoardsTheSolverGivesUpOn()
    {
        // Gives up on all but the easiest boards; whatever it finds still has a known shortest solution.
        BoardSearch search = Run(3, 5, TimeSpan.FromTicks(1));

        Assert.True(search.Found);
        Assert.Equal(search.LongestMoves.Length, Solver.Solve(search.Longest).Moves.Length);
    }

    [Fact]
    public void IsDeterministicForASeed()
    {
        BoardSearch a = Run(8, 6), b = Run(8, 6);

        Assert.Equal(BoardCodec.Encode(a.Longest), BoardCodec.Encode(b.Longest));
        Assert.Equal(a.Solves, b.Solves);
    }
}
