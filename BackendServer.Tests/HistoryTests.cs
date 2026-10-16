using SharedLogic;

namespace BackendServer.Tests;

/// <summary>The match history (every player's best per board) and showing the best solution during a round.</summary>
public class HistoryTests
{
    private static async Task<(LobbyHarness h, TestClient alice, TestClient bob)> TwoPlayerMatch(bool showBest = false)
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        if (showBest)
        {
            MatchSettings settings = alice.State.Settings;
            settings.ShowBestSolution = true;
            await h.Manager.UpdateSettingsAsync(alice.ConnectionId, settings);
        }
        await h.StartMatchAsync(alice);
        return (h, alice, bob);
    }

    private static Task RecordAsync(LobbyHarness h, TestClient client, Move[] moves) =>
        h.Manager.RecordAsync(client.ConnectionId, client.RoundNumber, MoveCodec.Encode(moves));

    // Two valid solutions longer than the shortest, of different lengths: (shorter, longer).
    private static (Move[] shorter, Move[] longer) TwoLengths(RoundSetup board)
    {
        Move[][] all = LobbyHarness.LongerSolutions(board).ToArray();
        Move[] shorter = all[0];
        return (shorter, all.First(m => m.Length > shorter.Length));
    }

    [Fact]
    public async Task RoundEnded_RecordsEveryPlayersBest_ShortestFirst()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        var (shorter, longer) = TwoLengths(alice.Board);

        await bob.SubmitAsync(longer);          // takes the lead
        await alice.SubmitAsync(shorter);       // beats it
        await RecordAsync(h, bob, longer);      // no longer shorter than Bob's own best: no change
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundEnded) == 1, "the timer to run out");

        Assert.Equal(0, bob.LastResult.SolvedPauseSeconds); // ended by the timer: no pause before the replays
        RoundRecord record = bob.LastResult.Record;
        Assert.Equal(1, record.MatchRound);
        Assert.Equal(alice.BoardBytes, record.Board);
        Assert.Equal("Alice", record.WinnerName);
        Assert.Equal(new[] { "Alice", "Bob" }, record.Solutions.Select(s => s.Name));
        Assert.Equal(MoveCodec.Encode(shorter), record.Solutions[0].Moves);
        Assert.Equal(MoveCodec.Encode(longer), record.Solutions[1].Moves);
        Assert.NotEmpty(record.ShortestMoves);
    }

    [Fact]
    public async Task RecordedSolution_OnlyGoesToTheHistory()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        var (shorter, longer) = TwoLengths(alice.Board);
        await alice.SubmitAsync(shorter);
        int bests = alice.Count(HubMethods.NewBestSolution);
        int timers = alice.Count(HubMethods.TimerUpdated);

        await RecordAsync(h, bob, longer);

        Assert.Equal(bests, alice.Count(HubMethods.NewBestSolution));
        Assert.Equal(timers, alice.Count(HubMethods.TimerUpdated));
        Assert.True(alice.Player("Alice").Leading);
        Assert.Equal(0, bob.Count(HubMethods.SolutionRejected));

        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundEnded) == 1, "the timer to run out");
        Assert.Equal(new[] { "Alice", "Bob" }, bob.LastResult.Record.Solutions.Select(s => s.Name));
    }

    [Fact]
    public async Task OwnBest_KeepsEachPlayersShortest()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        var (shorter, longer) = TwoLengths(alice.Board);
        await RecordAsync(h, bob, longer);
        await RecordAsync(h, bob, shorter);
        await RecordAsync(h, bob, longer);
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        PlayerSolution own = bob.LastResult.Record.Solutions.Single(s => s.Name == "Bob");
        Assert.Equal(MoveCodec.Encode(shorter), own.Moves);
    }

    [Fact]
    public async Task InvalidRecordedSolution_IsIgnored()
    {
        var (h, alice, bob) = await TwoPlayerMatch();
        await RecordAsync(h, bob, new[] { new Move(RobotColor.Red, Direction.Up) });
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        Assert.Equal(new[] { "Alice" }, bob.LastResult.Record.Solutions.Select(s => s.Name));
    }

    [Fact]
    public async Task MidMatchJoiner_GetsTheHistory_AndANewMatchClearsIt()
    {
        var h = new LobbyHarness(o => o.PointsToWin = 2);
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        TestClient carol = await h.JoinAsync("Carol");
        var history = (RoundRecord[])carol.All(HubMethods.MatchHistory).Single().Args[0]!;
        Assert.Equal(new[] { 1 }, history.Select(r => r.MatchRound));

        // Second point wins the match; the history stays for the results, then Play again clears it.
        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundStarted) == 2, "the next board");
        await alice.SubmitShortestAsync();
        await h.AdvanceUntil(() => bob.State.Phase == MatchPhase.Finished, "the results");
        TestClient dave = await h.JoinAsync("Dave");
        Assert.Equal(2, ((RoundRecord[])dave.All(HubMethods.MatchHistory).Single().Args[0]!).Length);

        await h.StartMatchAsync(alice);
        TestClient erin = await h.JoinAsync("Erin");
        Assert.Equal(0, erin.Count(HubMethods.MatchHistory));
    }

    [Fact]
    public async Task NewBest_CarriesTheMoves_OnlyWhenTheLobbyShowsThem()
    {
        var (_, alice, bob) = await TwoPlayerMatch();
        await alice.SubmitLongerAsync();
        Assert.Empty((byte[])bob.All(HubMethods.NewBestSolution).Last().Args[3]!);

        var (_, carol, dave) = await TwoPlayerMatch(showBest: true);
        Move[] moves = LobbyHarness.LongerSolution(carol.Board);
        await carol.SubmitAsync(moves);
        Assert.Equal(MoveCodec.Encode(moves), (byte[])dave.All(HubMethods.NewBestSolution).Last().Args[3]!);
        Assert.True(dave.State.Settings.ShowBestSolution);
    }
}
