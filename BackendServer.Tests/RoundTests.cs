using SharedLogic;

namespace BackendServer.Tests;

/// <summary>Solutions, the timer, votes and how rounds end.</summary>
public class RoundTests
{
    private static async Task<(LobbyHarness h, TestClient alice, TestClient bob)> TwoPlayerMatch(
        Action<BackendServer.Services.GameOptions>? configure = null)
    {
        var h = new LobbyHarness(configure);
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);
        return (h, alice, bob);
    }

    [Fact]
    public async Task ShortestPossibleSolution_EndsTheRoundAtOnce_AndScores()
    {
        var (h, alice, bob) = await TwoPlayerMatch();

        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        RoundResult result = bob.LastResult;
        Assert.Equal("Alice", result.WinnerName);
        Assert.Equal(result.ShortestMoves, result.WinnerMoves);
        Assert.Equal(1, bob.Player("Alice").Score);
        Assert.Equal(0, bob.Player("Bob").Score);
    }

    [Fact]
    public async Task AfterTheReplays_CountsDownThenDealsTheNextBoard()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        int firstRound = bob.RoundNumber;
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundCountdown) == 2, "the countdown");
        Assert.Equal(1, bob.Count(HubMethods.RoundStarted));
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundStarted) == 2, "the next board");
        Assert.Equal(firstRound + 1, bob.RoundNumber);
        Assert.Equal(2, bob.State.MatchRound);
    }

    [Fact]
    public async Task TheReplaysFinalPosition_StaysUpASecond_BeforeTheCountdown()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        // The winner's replay only (it is the shortest): a pause, then one step per move.
        RoundResult result = bob.LastResult;
        double replay = result.ReplayPauseSeconds + MoveCodec.Decode(result.WinnerMoves).Length * result.ReplayStepSeconds;
        await h.AdvanceAsync(replay + h.Options.AfterReplaySeconds - 0.25);
        Assert.Equal(1, bob.Count(HubMethods.RoundCountdown));
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundCountdown) == 2, "the countdown", stepSeconds: 0.1, maxSeconds: 1);
    }

    [Fact]
    public async Task FirstSolution_StartsTheTimer_AndTheRoundEndsWhenItRunsOut()
    {
        var (h, alice, bob) = await TwoPlayerMatch();

        await bob.SubmitLongerAsync();
        Assert.Equal(60.0, (double)alice.All(HubMethods.TimerUpdated).Single().Args[1]!);
        Assert.True(alice.Player("Bob").Leading);

        await h.AdvanceAsync(59);
        Assert.Equal(0, alice.Count(HubMethods.RoundEnded));

        await h.AdvanceUntil(() => alice.Count(HubMethods.RoundEnded) == 1, "the timer to run out", maxSeconds: 5);
        Assert.Equal("Bob", alice.LastResult.WinnerName);
        Assert.Equal(1, alice.Player("Bob").Score);
    }

    [Fact]
    public async Task BetterSolution_TopsUpTheTimer_ButNeverShortensIt()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        List<Move[]> longer = LobbyHarness.LongerSolutions(bob.Board).ToList();
        Move[] better = longer.First();
        Move[] worse = longer.First(m => m.Length > better.Length);

        await bob.SubmitAsync(worse);
        await h.AdvanceAsync(50); // 10 s left
        await alice.SubmitAsync(better);
        Assert.True(bob.Player("Alice").Leading);
        Assert.Equal(45.0, (double)bob.All(HubMethods.TimerUpdated).Last().Args[1]!);

        // 45 s from the better solution, not 10.
        await h.AdvanceAsync(40);
        Assert.Equal(0, bob.Count(HubMethods.RoundEnded));
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundEnded) == 1, "the timer to run out", maxSeconds: 10);
        Assert.Equal("Alice", bob.LastResult.WinnerName);
    }

    [Fact]
    public async Task BetterSolutionWithPlentyOfTimeLeft_DoesntChangeTheTimer()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        List<Move[]> longer = LobbyHarness.LongerSolutions(bob.Board).ToList();
        Move[] better = longer.First();
        Move[] worse = longer.First(m => m.Length > better.Length);

        await bob.SubmitAsync(worse);
        await alice.SubmitAsync(better); // 60 s left > 45 s
        Assert.Single(bob.All(HubMethods.TimerUpdated));
    }

    [Fact]
    public async Task SolutionsThatDontBeatTheBest_AreRejected()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        Move[] longer = LobbyHarness.LongerSolution(bob.Board);

        await bob.SubmitAsync(longer);
        await alice.SubmitAsync(longer);
        Assert.StartsWith("Not better", alice.LastRejection);
        Assert.True(alice.Player("Bob").Leading);
    }

    [Fact]
    public async Task InvalidAndStaleSolutions_AreRejected()
    {
        var (h, alice, bob) = await TwoPlayerMatch();

        await alice.SubmitAsync(Array.Empty<Move>());
        Assert.StartsWith("Invalid solution", alice.LastRejection);

        await h.Manager.SubmitAsync(alice.ConnectionId, alice.RoundNumber - 1, MoveCodec.Encode(Solver.Solve(alice.Board).Moves));
        Assert.Equal("That round is over.", alice.LastRejection);
    }

    [Fact]
    public async Task EveryoneElseDone_EndsTheRound_WithTheLeaderWinning()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        await bob.SubmitLongerAsync();

        await alice.SetDoneAsync();
        await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == 1, "the round to end");
        Assert.Equal("Bob", alice.LastResult.WinnerName);
    }

    [Fact]
    public async Task TheLeaderCantVote()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        await bob.SubmitLongerAsync();

        await bob.SetDoneAsync();
        Assert.False(alice.Player("Bob").Done);
    }

    [Fact]
    public async Task EveryoneSkippingAnUnsolvedBoard_EndsItWithNoPoint_AndTheShortestPlaysAtNormalSpeed()
    {
        var (h, alice, bob) = await TwoPlayerMatch();

        await alice.SetDoneAsync();
        Assert.Equal(0, alice.Count(HubMethods.RoundEnded));
        await bob.SetDoneAsync();
        await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == 1, "the round to end");

        RoundResult result = alice.LastResult;
        Assert.Null(result.WinnerName);
        Assert.Empty(result.WinnerMoves);
        Assert.NotEmpty(result.ShortestMoves);
        Assert.Equal(h.Options.ReplayStepSeconds, result.ShortestReplayStepSeconds);
        Assert.All(alice.State.Players, p => Assert.Equal(0, p.Score));
    }

    [Fact]
    public async Task ShortestAfterALongerWinner_PlaysFaster()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        await bob.SubmitLongerAsync();
        await alice.SetDoneAsync();
        await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == 1, "the round to end");

        Assert.Equal(h.Options.ShortestReplayStepSeconds, alice.LastResult.ShortestReplayStepSeconds);
    }

    [Fact]
    public async Task SkipReplayVote_StartsTheCountdownWithoutWaitingForTheReplays()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        // Bob (the only non-winner) skips; no fake time passes.
        await bob.SetDoneAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundCountdown) == 2, "the countdown");
    }

    [Fact]
    public async Task SoloPlayer_LockingIn_EndsTheRound()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        await h.StartMatchAsync(alice);

        await alice.SubmitLongerAsync();
        await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == 1, "the round to end");
    }
}
