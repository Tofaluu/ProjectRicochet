using Microsoft.AspNetCore.SignalR;
using SharedLogic;

namespace BackendServer.Tests;

/// <summary>Lobbies, the host, settings and the match lifecycle.</summary>
public class MatchTests
{
    [Fact]
    public async Task FirstJoinerIsHost_AndNewLobbyWaitsWithNoBoard()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        LobbyState state = bob.State;
        Assert.Equal("Alice", state.Host);
        Assert.Equal(MatchPhase.Waiting, state.Phase);
        Assert.Equal(new[] { "Alice", "Bob" }, state.Players.Select(p => p.Name));
        Assert.All(state.Players, p => Assert.True(p.Connected));
        Assert.Equal(0, alice.Count(HubMethods.RoundStarted));
    }

    [Fact]
    public async Task DuplicateNamesGetANumber()
    {
        var h = new LobbyHarness();
        await h.JoinAsync("Sam");
        TestClient second = await h.JoinAsync("sam", playerId: "other");
        Assert.Equal("sam 2", second.Name);
    }

    [Fact]
    public async Task OnlyTheHostCanStartOrChangeSettings_AndSettingsAreClamped()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        await Assert.ThrowsAsync<HubException>(() => h.Manager.StartMatchAsync(bob.ConnectionId));
        await Assert.ThrowsAsync<HubException>(() => h.Manager.UpdateSettingsAsync(bob.ConnectionId, new MatchSettings()));

        await h.Manager.UpdateSettingsAsync(alice.ConnectionId,
            new MatchSettings { PointsToWin = 0, FirstTimerSeconds = 5, ExtensionSeconds = 999 });
        MatchSettings settings = bob.State.Settings;
        Assert.Equal(MatchSettings.MinPointsToWin, settings.PointsToWin);
        Assert.Equal(MatchSettings.MinTimerSeconds, settings.FirstTimerSeconds);
        Assert.Equal(MatchSettings.MaxTimerSeconds, settings.ExtensionSeconds);
    }

    [Fact]
    public async Task Start_CountsDownThenDealsTheSameBoardToEveryone()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        Task start = h.Manager.StartMatchAsync(alice.ConnectionId);
        Assert.Equal(MatchPhase.Playing, bob.State.Phase);
        Assert.Equal(h.Options.CountdownSeconds, (double)bob.All(HubMethods.RoundCountdown).Single().Args[0]!);

        // Nothing is dealt before the countdown is over, however fast the board is ready.
        await Task.Delay(200);
        await h.AdvanceAsync(h.Options.CountdownSeconds - 0.5);
        Assert.Equal(0, bob.Count(HubMethods.RoundStarted));

        await h.AdvanceUntil(() => bob.Count(HubMethods.RoundStarted) == 1, "the first board");
        await start;
        Assert.Equal(alice.BoardBytes, bob.BoardBytes);
        Assert.Equal(1, bob.State.MatchRound);
    }

    [Fact]
    public async Task NoSecondStart_AndNoSettingChanges_DuringAMatch()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        await h.StartMatchAsync(alice);

        await Assert.ThrowsAsync<HubException>(() => h.Manager.StartMatchAsync(alice.ConnectionId));
        await Assert.ThrowsAsync<HubException>(() => h.Manager.UpdateSettingsAsync(alice.ConnectionId, new MatchSettings()));
    }

    [Fact]
    public async Task ReachingThePointsTarget_FinishesTheMatch_AndPlayAgainResetsScores()
    {
        var h = new LobbyHarness(o => o.PointsToWin = 1);
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);

        await bob.SubmitShortestAsync();
        await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == 1, "the round to end");
        Assert.Equal("Bob", alice.LastResult.MatchWinnerName);

        // After the replays: results, not another board.
        await h.AdvanceUntil(() => alice.State.Phase == MatchPhase.Finished, "the results");
        Assert.Equal("Bob", alice.State.MatchWinner);
        Assert.Equal(1, alice.Player("Bob").Score);
        Assert.Equal(1, alice.Count(HubMethods.RoundStarted));
        Assert.Equal(1, alice.Count(HubMethods.RoundCountdown)); // only the one before the first board

        await h.StartMatchAsync(alice);
        Assert.Equal(MatchPhase.Playing, alice.State.Phase);
        Assert.All(alice.State.Players, p => Assert.Equal(0, p.Score));
        Assert.Null(alice.State.MatchWinner);
    }

    [Fact]
    public async Task HostLeaving_PassesTheHostToTheNextPlayerInJoinOrder()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        TestClient carol = await h.JoinAsync("Carol");

        await alice.LeaveAsync();
        Assert.Equal("Bob", carol.State.Host);
        await h.StartMatchAsync(bob); // Bob can start now
        await Assert.ThrowsAsync<HubException>(() => h.Manager.UpdateSettingsAsync(carol.ConnectionId, new MatchSettings()));
    }

    [Fact]
    public async Task MidMatchJoiner_GetsTheCurrentBoard()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        await h.StartMatchAsync(alice);

        TestClient late = await h.JoinAsync("Late");
        Assert.Equal(alice.BoardBytes, late.BoardBytes);
        Assert.Equal(alice.RoundNumber, late.RoundNumber);
    }

    [Fact]
    public async Task JoinerDuringTheCountdown_GetsTheTimeLeft()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        Task start = h.Manager.StartMatchAsync(alice.ConnectionId);
        await h.AdvanceAsync(1);

        TestClient late = await h.JoinAsync("Late");
        double left = (double)late.All(HubMethods.RoundCountdown).Single().Args[0]!;
        Assert.Equal(h.Options.CountdownSeconds - 1, left, precision: 3);

        await h.AdvanceUntil(() => late.Count(HubMethods.RoundStarted) == 1, "the first board");
        await start;
    }

    [Fact]
    public async Task LastPlayerLeaving_ClosesTheLobby()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        await alice.LeaveAsync();

        // Same code again: a brand new lobby with a new host.
        TestClient bob = await h.JoinAsync("Bob");
        Assert.Equal("Bob", bob.State.Host);
        Assert.Single(bob.State.Players);
    }
}
