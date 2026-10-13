using BackendServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharedLogic;

namespace BackendServer.Tests;

/// <summary>
/// A <see cref="LobbyManager"/> wired to a <see cref="FakeHub"/> and a <see cref="FakeTimeProvider"/>:
/// timers, replays and countdowns only advance when a test moves the clock. Boards are still generated
/// and solved for real (on the thread pool), so tests wait for those with <see cref="Until"/>.
/// </summary>
public sealed class LobbyHarness
{
    public const string Code = "TEST";

    public FakeHub Hub { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    public GameOptions Options { get; }
    public LobbyManager Manager { get; }
    private int _nextConnection;

    public LobbyHarness(Action<GameOptions>? configure = null)
    {
        Options = new GameOptions { Seed = 12345 };
        configure?.Invoke(Options);
        Manager = new LobbyManager(Hub, Microsoft.Extensions.Options.Options.Create(Options), Time,
            NullLogger<LobbyManager>.Instance);
    }

    public string NewConnectionId() => "conn" + Interlocked.Increment(ref _nextConnection);

    /// <summary>Joins on a fresh connection, with a player id of its own unless one is given.</summary>
    public async Task<TestClient> JoinAsync(string name, string? playerId = null, string code = Code)
    {
        string connection = NewConnectionId();
        string id = playerId ?? "id-" + name;
        string shownName = await Manager.JoinAsync(connection, name, code, id);
        return new TestClient(this, connection, shownName, id);
    }

    /// <summary>The same player on a new connection (as after an automatic reconnect).</summary>
    public async Task<TestClient> RejoinAsync(TestClient old, string? name = null, string? playerId = null)
    {
        string connection = NewConnectionId();
        string id = playerId ?? old.PlayerId;
        string shownName = await Manager.JoinAsync(connection, name ?? old.Name, Code, id);
        return new TestClient(this, connection, shownName, id);
    }

    /// <summary>The connection drops (SignalR also takes it out of its groups).</summary>
    public async Task DropAsync(TestClient client)
    {
        await Hub.Groups.RemoveFromGroupAsync(client.ConnectionId, Code);
        await Manager.DisconnectAsync(client.ConnectionId);
    }

    /// <summary>Host starts the match and the clock runs through the countdown until the first board arrives.</summary>
    public async Task StartMatchAsync(TestClient host)
    {
        int boards = host.Count(HubMethods.RoundStarted);
        Task start = Manager.StartMatchAsync(host.ConnectionId);
        await AdvanceUntil(() => host.Count(HubMethods.RoundStarted) > boards, "the first board");
        await start;
    }

    /// <summary>Waits (in real time) for background work such as board generation.</summary>
    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Moves the fake clock forward a step at a time until <paramref name="condition"/> holds, giving
    /// background work a moment between steps. Returns how much fake time it took.
    /// </summary>
    public async Task<TimeSpan> AdvanceUntil(Func<bool> condition, string what, double stepSeconds = 0.5,
        double maxSeconds = 600)
    {
        TimeSpan elapsed = TimeSpan.Zero;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.TotalSeconds > maxSeconds || clock.ElapsedMilliseconds > 20000)
                throw new TimeoutException($"{what} didn't happen within {elapsed.TotalSeconds} fake seconds");
            // Let freshly scheduled background work register its timers before the clock moves.
            await Task.Delay(5);
            if (condition())
                break;
            Time.Advance(TimeSpan.FromSeconds(stepSeconds));
            elapsed += TimeSpan.FromSeconds(stepSeconds);
        }
        return elapsed;
    }

    /// <summary>Moves the clock and lets background work react.</summary>
    public async Task AdvanceAsync(double seconds)
    {
        await Task.Delay(20);
        Time.Advance(TimeSpan.FromSeconds(seconds));
        await Task.Delay(20);
    }

    /// <summary>A valid solution that's longer than the shortest one (so it doesn't end the round at once).</summary>
    public static Move[] LongerSolution(RoundSetup round) => LongerSolutions(round).First();

    /// <summary>
    /// Valid solutions longer than the shortest, shortest first: the shortest solution with one or two
    /// extra moves in front.
    /// </summary>
    public static IEnumerable<Move[]> LongerSolutions(RoundSetup round)
    {
        Move[] shortest = Solver.Solve(round).Moves;
        var moves = Enum.GetValues<RobotColor>()
            .SelectMany(r => Enum.GetValues<Direction>().Select(d => new Move(r, d)))
            .ToArray();
        IEnumerable<Move[]> prefixes = moves.Select(m => new[] { m })
            .Concat(moves.SelectMany(m1 => moves.Select(m2 => new[] { m1, m2 })));
        return prefixes
            .Select(prefix => prefix.Concat(shortest).ToArray())
            .Where(candidate => GameRules.ValidateSolution(round.Grid, round.Robots, round.Target, candidate) == SolutionResult.Valid);
    }
}

/// <summary>One connection's view: what it has received, plus shortcuts for calling the manager as it.</summary>
public sealed class TestClient
{
    private readonly LobbyHarness _harness;

    public TestClient(LobbyHarness harness, string connectionId, string name, string playerId)
    {
        _harness = harness;
        ConnectionId = connectionId;
        Name = name;
        PlayerId = playerId;
    }

    public string ConnectionId { get; }
    public string Name { get; }
    public string PlayerId { get; }

    public IReadOnlyList<Sent> Inbox => _harness.Hub.Inbox(ConnectionId);
    public int Count(string method) => Inbox.Count(m => m.Method == method);
    public IEnumerable<Sent> All(string method) => Inbox.Where(m => m.Method == method);

    public LobbyState State => (LobbyState)All(HubMethods.LobbyState).Last().Args[0]!;
    public PlayerInfo Me => State.Players.Single(p => p.Name == Name);
    public PlayerInfo Player(string name) => State.Players.Single(p => p.Name == name);

    public int RoundNumber => (int)All(HubMethods.RoundStarted).Last().Args[0]!;
    public byte[] BoardBytes => (byte[])All(HubMethods.RoundStarted).Last().Args[1]!;
    public RoundSetup Board => BoardCodec.Decode(BoardBytes);
    public RoundResult LastResult => (RoundResult)All(HubMethods.RoundEnded).Last().Args[0]!;
    public string LastRejection => (string)All(HubMethods.SolutionRejected).Last().Args[0]!;

    public Task SubmitAsync(Move[] moves) =>
        _harness.Manager.SubmitAsync(ConnectionId, RoundNumber, MoveCodec.Encode(moves));

    public Task SubmitShortestAsync() => SubmitAsync(Solver.Solve(Board).Moves);
    public Task SubmitLongerAsync() => SubmitAsync(LobbyHarness.LongerSolution(Board));
    public Task SetDoneAsync(bool done = true) => _harness.Manager.SetDoneAsync(ConnectionId, RoundNumber, done);
    public Task LeaveAsync() => _harness.Manager.LeaveAsync(ConnectionId);
}
