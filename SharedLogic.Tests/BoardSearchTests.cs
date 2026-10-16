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
