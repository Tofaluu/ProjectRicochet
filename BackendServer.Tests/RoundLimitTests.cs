using SharedLogic;

namespace BackendServer.Tests;

/// <summary>The round's time limit: unsolved boards end when it runs out; a solution switches to the normal timer.</summary>
public class RoundLimitTests
{
    private static async Task<(LobbyHarness h, TestClient alice, TestClient bob)> TwoPlayerMatch(int limitSeconds)
    {
        var h = new LobbyHarness(o => o.RoundLimitSeconds = limitSeconds);
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);
        return (h, alice, bob);
    }

    [Fact]
    public async Task EachBoard_ComesWithItsTimeLimit()
    {
        var (h, alice, bob) = await TwoPlayerMatch(300);
        Sent limit = bob.All(HubMethods.RoundTimeLimit).Single();
        Assert.Equal(bob.RoundNumber, (int)limit.Args[0]!);
        Assert.Equal(300.0, (double)limit.Args[1]!);
        Assert.Equal(300, bob.State.Settings.RoundLimitSeconds);
    }

    [Fact]
    public async Task UnsolvedBoard_EndsWithoutAPoint_WhenTheLimitRunsOut()
    {
        var (h, alice, bob) = await TwoPlayerMatch(120);

        await h.AdvanceAsync(115);
        Assert.Equal(0, bob.Count(HubMethods.RoundEnded));
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundEnded) == 1, "the limit to run out", maxSeconds: 10);

        RoundResult result = bob.LastResult;
        Assert.True(result.OutOfTime);
        Assert.Null(result.WinnerName);
        Assert.All(bob.State.Players, p => Assert.Equal(0, p.Score));
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundStarted) == 2, "the next board");
    }

    [Fact]
    public async Task ALateSolution_GetsTheFullTimer_PastTheLimit()
    {
        var (h, alice, bob) = await TwoPlayerMatch(120);

        await h.AdvanceAsync(110); // 10 s of the limit left
        await bob.SubmitLongerAsync();
        Assert.Equal(60.0, (double)alice.All(HubMethods.TimerUpdated).Single().Args[1]!);

        await h.AdvanceAsync(50); // well past the limit
        Assert.Equal(0, alice.Count(HubMethods.RoundEnded));
        await h.AdvanceUntil(() => alice.Count(HubMethods.RoundEnded) == 1, "the timer to run out", maxSeconds: 15);
        Assert.False(alice.LastResult.OutOfTime);
        Assert.Equal("Bob", alice.LastResult.WinnerName);
    }

    [Fact]
    public async Task NoLimit_WhenTurnedOff()
    {
        var (h, alice, bob) = await TwoPlayerMatch(0);
        Assert.Equal(0, bob.Count(HubMethods.RoundTimeLimit));
        await h.AdvanceAsync(3000);
        Assert.Equal(0, bob.Count(HubMethods.RoundEnded));
    }

    [Fact]
    public async Task Joiners_GetWhatsLeftOfTheLimit_UntilItsSolved()
    {
        var (h, alice, bob) = await TwoPlayerMatch(300);
        await h.AdvanceAsync(100);
        TestClient carol = await h.JoinAsync("Carol");
        Assert.Equal(200.0, (double)carol.All(HubMethods.RoundTimeLimit).Single().Args[1]!, precision: 1);

        await bob.SubmitLongerAsync();
        TestClient dave = await h.JoinAsync("Dave");
        Assert.Equal(0, dave.Count(HubMethods.RoundTimeLimit));
        Assert.Single(dave.All(HubMethods.TimerUpdated));
    }

    [Fact]
    public void Settings_ClampTheLimit_AndZeroMeansOff()
    {
        Assert.Equal(0, new MatchSettings { RoundLimitSeconds = -5 }.Clamped().RoundLimitSeconds);
        Assert.Equal(MatchSettings.MinRoundLimitSeconds, new MatchSettings { RoundLimitSeconds = 10 }.Clamped().RoundLimitSeconds);
        Assert.Equal(MatchSettings.MaxRoundLimitSeconds, new MatchSettings { RoundLimitSeconds = 99999 }.Clamped().RoundLimitSeconds);
    }
}
