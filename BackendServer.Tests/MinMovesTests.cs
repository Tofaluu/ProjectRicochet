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
    public async Task OutOfSearchTime_DealsTheLongestBoardFound()
    {
        var (_, alice) = await MatchWithMinimum(MatchSettings.MaxMinMoves, o => o.BoardSearchSeconds = 0);

        Assert.True(Solver.Solve(alice.Board).Solved);
    }
}
