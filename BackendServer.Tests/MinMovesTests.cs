using BackendServer.Services;
using SharedLogic;

namespace BackendServer.Tests;

/// <summary>The host's minimum board length (<see cref="MatchSettings.MinMoves"/>).</summary>
public class MinMovesTests
{
    private static async Task<(LobbyHarness h, TestClient alice)> MatchWithMinimum(int minMoves, Action<GameOptions>? configure = null)
    {
        var h = new LobbyHarness(configure);
        TestClient alice = await h.JoinAsync("Alice");
        await h.JoinAsync("Bob");
        MatchSettings settings = alice.State.Settings;
        settings.MinMoves = minMoves;
        await h.Manager.UpdateSettingsAsync(alice.ConnectionId, settings);
        await h.StartMatchAsync(alice);
        return (h, alice);
    }

    [Fact]
    public async Task NewLobbies_UseTheConfiguredDefault_AndTheMinimumIsClamped()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        Assert.Equal(h.Options.MinBoardMoves, alice.State.Settings.MinMoves);

        Assert.Equal(MatchSettings.MinMinMoves, new MatchSettings { MinMoves = 0 }.Clamped().MinMoves);
        Assert.Equal(MatchSettings.MaxMinMoves, new MatchSettings { MinMoves = 20 }.Clamped().MinMoves);
    }

    [Fact]
    public async Task DealtBoards_NeedAtLeastTheMinimum()
    {
        var (h, alice) = await MatchWithMinimum(12);

        Assert.True(Solver.Solve(alice.Board).Moves.Length >= 12);

        // The next board too (prepared during the replays).
        await alice.SubmitShortestAsync();
        await h.AdvanceUntil(() => alice.Count(HubMethods.RoundStarted) == 2, "the next board");
        Assert.True(Solver.Solve(alice.Board).Moves.Length >= 12);
    }

    [Fact]
    public async Task BoardsTheSolverGivesUpOn_AreNeverDealt()
    {
        // A solver limit so short it gives up on all but the easiest boards, and no search time.
        var (h, alice) = await MatchWithMinimum(MatchSettings.DefaultMinMoves, o =>
        {
            o.SolverTimeLimitSeconds = 0.00001;
            o.BoardSearchSeconds = 0;
        });

        for (int board = 1; board <= 4; board++) // the 5th point would end the match
        {
            // Only a board with a known shortest solution ends the moment someone finds it.
            await alice.SubmitShortestAsync();
            await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == board, "the round to end at once");
            Assert.NotEmpty(alice.LastResult.ShortestMoves);
            await h.AdvanceUntil(() => alice.Count(HubMethods.RoundStarted) == board + 1, "the next board");
        }
    }

    [Fact]
    public async Task OutOfSearchTime_DealsTheLongestBoardFound()
    {
        var (_, alice) = await MatchWithMinimum(MatchSettings.MaxMinMoves, o => o.BoardSearchSeconds = 0);

        Assert.True(Solver.Solve(alice.Board).Solved);
    }
}
