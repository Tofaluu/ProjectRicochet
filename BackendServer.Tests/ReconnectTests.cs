using SharedLogic;

namespace BackendServer.Tests;

/// <summary>Dropped connections keep their seat for a while; leaving on purpose doesn't.</summary>
public class ReconnectTests
{
    [Fact]
    public async Task DroppedPlayer_IsShownDisconnected_AndKeepsTheirSeat()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        await h.DropAsync(alice);
        PlayerInfo seat = bob.Player("Alice");
        Assert.False(seat.Connected);
        Assert.Equal("Alice", bob.State.Host); // still host while the seat is held
    }

    [Fact]
    public async Task ReconnectingWithTheSameId_TakesBackScoreAndBoard()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);
        await bob.SubmitShortestAsync();
        await h.AdvanceUntil(() => alice.Count(HubMethods.RoundStarted) == 2, "the second board");

        await h.DropAsync(bob);
        TestClient back = await h.RejoinAsync(bob);

        Assert.Equal("Bob", back.Name);
        Assert.Equal(1, back.Me.Score);
        Assert.True(back.Me.Connected);
        Assert.Equal(alice.BoardBytes, back.BoardBytes);
        Assert.Equal(2, alice.State.Players.Length);
    }

    [Fact]
    public async Task RejoiningWithTheSameName_AfterARestart_ReclaimsTheSeat()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);
        await alice.SubmitShortestAsync();
        await LobbyHarness.Until(() => bob.Count(HubMethods.RoundEnded) == 1, "the round to end");

        await h.DropAsync(alice);
        TestClient restarted = await h.RejoinAsync(alice, playerId: "new-install");

        Assert.Equal("Alice", restarted.Name);
        Assert.Equal(1, restarted.Me.Score);
        Assert.Equal("Alice", bob.State.Host);
        Assert.Equal(2, bob.State.Players.Length);
    }

    [Fact]
    public async Task ANameOfAConnectedPlayer_IsNotReclaimed()
    {
        var h = new LobbyHarness();
        await h.JoinAsync("Alice");
        TestClient impostor = await h.JoinAsync("Alice", playerId: "someone-else");
        Assert.Equal("Alice 2", impostor.Name);
    }

    [Fact]
    public async Task ReconnectingBeforeTheServerNoticedTheDrop_ReplacesTheOldConnection()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        TestClient newAlice = await h.RejoinAsync(alice);
        Assert.Equal("Alice", newAlice.Name);
        Assert.Equal(2, bob.State.Players.Length);

        // The old connection's late disconnect doesn't touch the seat.
        await h.Manager.DisconnectAsync(alice.ConnectionId);
        Assert.True(bob.Player("Alice").Connected);
    }

    [Fact]
    public async Task SeatIsFreed_WhenTheGracePeriodRunsOut_AndTheHostPassesOn()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        await h.DropAsync(alice);
        await h.AdvanceAsync(h.Options.ReconnectGraceSeconds - 5);
        Assert.Equal(2, bob.State.Players.Length);

        await h.AdvanceUntil(() => bob.State.Players.Length == 1, "the seat to be freed", maxSeconds: 30);
        Assert.Equal("Bob", bob.State.Host);
    }

    [Fact]
    public async Task ReconnectingInTime_CancelsTheExpiry()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        await h.DropAsync(alice);
        await h.AdvanceAsync(10);
        await h.RejoinAsync(alice);
        await h.AdvanceAsync(h.Options.ReconnectGraceSeconds * 2);
        Assert.Equal(2, bob.State.Players.Length);
        Assert.Equal("Alice", bob.State.Host);
    }

    [Fact]
    public async Task LeavingOnPurpose_FreesTheSeatAtOnce()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");

        await alice.LeaveAsync();
        Assert.Single(bob.State.Players);
    }

    [Fact]
    public async Task DisconnectedPlayers_AreNotWaitedOnInVotes()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        TestClient carol = await h.JoinAsync("Carol");
        await h.StartMatchAsync(alice);
        await bob.SubmitLongerAsync();

        await alice.SetDoneAsync();
        Assert.Equal(0, alice.Count(HubMethods.RoundEnded)); // still waiting for Carol
        await h.DropAsync(carol);
        await LobbyHarness.Until(() => alice.Count(HubMethods.RoundEnded) == 1, "the round to end");
    }

    [Fact]
    public async Task DisconnectedLeader_StillGetsThePoint()
    {
        // Longer than the 60 s round timer, so the seat is still held when the round ends.
        var h = new LobbyHarness(o => o.ReconnectGraceSeconds = 120);
        TestClient alice = await h.JoinAsync("Alice");
        TestClient bob = await h.JoinAsync("Bob");
        await h.StartMatchAsync(alice);
        await bob.SubmitLongerAsync();

        await h.DropAsync(bob);
        await h.AdvanceUntil(() => alice.Count(HubMethods.RoundEnded) == 1, "the timer to run out");
        Assert.Equal(1, alice.Player("Bob").Score);
    }

    [Fact]
    public async Task LobbyWithEveryoneDisconnected_ClosesWhenTheSeatsExpire()
    {
        var h = new LobbyHarness();
        TestClient alice = await h.JoinAsync("Alice");
        await h.DropAsync(alice);

        // Still there during the grace period: Alice gets her seat back.
        await h.AdvanceAsync(10);
        TestClient back = await h.RejoinAsync(alice);
        Assert.Equal("Alice", back.State.Host);

        await h.DropAsync(back);
        await h.AdvanceAsync(h.Options.ReconnectGraceSeconds + 5);
        await Task.Delay(50);
        TestClient bob = await h.JoinAsync("Bob");
        Assert.Single(bob.State.Players); // a fresh lobby
    }
}
