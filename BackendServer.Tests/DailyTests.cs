using BackendServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharedLogic;

namespace BackendServer.Tests;

/// <summary>The daily puzzle: one board a day for everyone, guest results, the leaderboard and streaks.</summary>
public class DailyTests
{
    // Launch on a Monday. Noon in Toronto (EDT, UTC-4) on launch day.
    private const string Launch = "2026-10-12";
    private static readonly DateTimeOffset LaunchNoon = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);

    private const string Alice = "alice-guest-id-0001";
    private const string Bob = "bob-guest-id-000002";
    private const string Carol = "carol-guest-id-0003";

    // Boards of 4-5 moves keep the tests quick, unless a test checks the real weekday lengths.
    private static (DailyService daily, FakeTimeProvider time) Create(IDailyStore? store = null, bool weekdayLengths = false)
    {
        var time = new FakeTimeProvider(LaunchNoon);
        var options = Options.Create(new DailyOptions
        {
            LaunchDate = Launch, Seed = 7, SearchSeconds = 20,
            MinMoves = weekdayLengths ? null : 4, MaxMoves = weekdayLengths ? null : 5,
        });
        return (new DailyService(store ?? new InMemoryDailyStore(), options, time, NullLogger<DailyService>.Instance), time);
    }

    private static Move[] Shortest(DailyPuzzleInfo info) => Solver.Solve(BoardCodec.Decode(info.Board)).Moves;
    private static byte[] Encode(Move[] moves) => MoveCodec.Encode(moves);

    [Fact]
    public async Task EveryoneGetsTheSameBoard_NumberedFromLaunch_WithTheDaysLength()
    {
        var (daily, _) = Create();
        DailyPuzzleInfo alice = await daily.GetDailyAsync(Alice, "Alice");
        DailyPuzzleInfo bob = await daily.GetDailyAsync(Bob, "Bob");

        Assert.Equal(1, alice.Number);
        Assert.Equal("2026-10-12", alice.Date);
        Assert.Equal(alice.Board, bob.Board);
        Assert.Equal(alice.ShortestLength, Shortest(alice).Length); // a board with a known shortest solution
        Assert.InRange(alice.ShortestLength, 4, 5);
        Assert.Equal(0, alice.BestMoves);
        Assert.Equal(12 * 3600, alice.SecondsUntilNext, 1); // noon -> midnight
    }

    [Fact]
    public async Task TheNextPuzzleComesAtMidnightEastern_AndItsHarder()
    {
        var (daily, time) = Create(weekdayLengths: true);
        time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 3, 59, 0, TimeSpan.Zero)); // 23:59 in Toronto
        DailyPuzzleInfo monday = await daily.GetDailyAsync(Alice, "Alice");
        Assert.Equal(1, monday.Number);
        Assert.Equal(9, monday.ShortestLength);

        time.Advance(TimeSpan.FromMinutes(2));
        DailyPuzzleInfo tuesday = await daily.GetDailyAsync(Alice, "Alice");
        Assert.Equal(2, tuesday.Number);
        Assert.InRange(tuesday.ShortestLength, 10, 11);
        Assert.Equal((10, 11), DailyService.Difficulty(DayOfWeek.Tuesday));
        Assert.Equal((16, 17), DailyService.Difficulty(DayOfWeek.Sunday));
    }

    [Fact]
    public void EachDaysTwoLengths_AreEquallyLikely()
    {
        var random = new Random(3);
        int longer = 0;
        for (int i = 0; i < 2000; i++)
        {
            int length = DailyService.PickLength(10, 11, random);
            Assert.InRange(length, 10, 11);
            if (length == 11)
                longer++;
        }
        Assert.InRange(longer, 900, 1100);
        Assert.Equal(9, DailyService.PickLength(9, 9, random));
    }

    [Fact]
    public async Task Solutions_AreTimedFromFirstOpening_AndOnlyFewerMovesImprove()
    {
        var (daily, time) = Create();
        DailyPuzzleInfo info = await daily.GetDailyAsync(Alice, "Alice");
        Move[] shortest = Shortest(info);
        Move[] longer = LobbyHarness.LongerSolution(BoardCodec.Decode(info.Board));

        time.Advance(TimeSpan.FromSeconds(30));
        DailyResult first = await daily.SubmitAsync(Alice, 1, Encode(longer));
        Assert.True(first.Improved);
        Assert.Equal(30, first.Seconds, 3);
        Assert.Equal(1, first.Rank);

        // Opening again doesn't restart the clock.
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(60, (await daily.GetDailyAsync(Alice, "Alice")).SecondsSinceOpened, 3);

        DailyResult better = await daily.SubmitAsync(Alice, 1, Encode(shortest));
        Assert.True(better.Improved);
        Assert.Equal(Encode(shortest), better.BestSolution); // the player's own best comes back (for sharing)
        Assert.Equal(shortest.Length, better.BestMoves);
        Assert.Equal(60, better.BestSeconds, 3);
        Assert.Equal(shortest.Length, better.ShortestLength);

        time.Advance(TimeSpan.FromSeconds(30));
        DailyResult same = await daily.SubmitAsync(Alice, 1, Encode(shortest));
        Assert.False(same.Improved); // as few moves, later: the earlier time stands
        Assert.Equal(60, same.BestSeconds, 3);
        Assert.Equal(Encode(shortest), (await daily.GetDailyAsync(Alice, "Alice")).BestSolution);
        Assert.Equal(1, same.Streak);
    }

    [Fact]
    public async Task BadRequests_AreRefused()
    {
        var (daily, time) = Create();
        await Assert.ThrowsAsync<HubException>(() => daily.GetDailyAsync("short", "Alice")); // not a guest id
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitAsync(Alice, 1, new byte[] { 0 })); // never opened it

        DailyPuzzleInfo info = await daily.GetDailyAsync(Alice, "Alice");
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitAsync(Alice, 1, Encode(new[] { new Move(RobotColor.Red, Direction.Up) })));
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitAsync(Alice, 1, new byte[] { 255 })); // not a move
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitAsync(Alice, 2, Encode(Shortest(info)))); // not today's

        time.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitAsync(Alice, 1, Encode(Shortest(info)))); // that day is over
        await Assert.ThrowsAsync<HubException>(() => daily.GetLeaderboardAsync(Alice, 3)); // not yet
    }

    [Fact]
    public async Task Leaderboard_FewestMovesThenFastest()
    {
        await LeaderboardScenario(Create());
    }

    [Fact]
    public async Task Streaks_CountDaysInARow()
    {
        var (daily, time) = Create();
        for (int day = 1; day <= 2; day++)
        {
            DailyPuzzleInfo info = await daily.GetDailyAsync(Alice, "Alice");
            Assert.Equal(day - 1, info.Streak); // yesterday's still counts before today is solved
            Assert.Equal(day, (await daily.SubmitAsync(Alice, info.Number, Encode(Shortest(info)))).Streak);
            time.Advance(TimeSpan.FromDays(1));
        }
        Assert.Equal(2, (await daily.GetDailyAsync(Alice, "Alice")).Streak); // day 3, not solved yet

        time.Advance(TimeSpan.FromDays(1)); // day 3 skipped
        Assert.Equal(0, (await daily.GetDailyAsync(Alice, "Alice")).Streak);
    }

    [Fact]
    public async Task TheDatabaseStore_WorksTheSame()
    {
        // The EF Core store on an in-memory SQLite database (production uses PostgreSQL through the same code).
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<DailyDbContext>().UseSqlite(connection).Options;
        using (var db = new DailyDbContext(options))
            db.Database.EnsureCreated();
        var store = new EfDailyStore(new Factory(options));

        await LeaderboardScenario(Create(store));
        Assert.Equal(new[] { 1 }, await store.GetSolvedNumbersAsync(Alice, 1, 5));
        Assert.NotNull(await store.GetPuzzleAsync(1));
    }

    // Bob and Carol tie on moves, Bob faster; Alice, first to finish, takes more moves. Alice renames herself.
    private static async Task LeaderboardScenario((DailyService daily, FakeTimeProvider time) setup)
    {
        var (daily, time) = setup;
        DailyPuzzleInfo info = await daily.GetDailyAsync(Alice, "Alice");
        await daily.GetDailyAsync(Bob, "Bob");
        await daily.GetDailyAsync(Carol, "Carol");
        Move[] shortest = Shortest(info);
        Move[] longer = LobbyHarness.LongerSolution(BoardCodec.Decode(info.Board));

        time.Advance(TimeSpan.FromSeconds(2));
        await daily.SubmitAsync(Alice, 1, Encode(longer));
        time.Advance(TimeSpan.FromSeconds(3));
        await daily.SubmitAsync(Bob, 1, Encode(shortest));
        time.Advance(TimeSpan.FromSeconds(5));
        DailyResult carol = await daily.SubmitAsync(Carol, 1, Encode(shortest));
        Assert.Equal(2, carol.Rank);
        Assert.Equal(3, carol.Players);
        await daily.GetDailyAsync(Alice, "Ally");

        DailyLeaderboard board = await daily.GetLeaderboardAsync(Alice, 1);
        Assert.Equal(3, board.Players);
        Assert.Equal(new[] { "Bob", "Carol", "Ally" }, board.Top.Select(e => e.Name));
        Assert.Equal(new[] { 5.0, 10.0 }, board.Top.Take(2).Select(e => Math.Round(e.Seconds, 3)));
        Assert.Equal(3, board.MyRank);
        Assert.True(board.Top[2].IsMe);
        Assert.False(board.Top[0].IsMe);
    }

    private sealed class Factory : IDbContextFactory<DailyDbContext>
    {
        private readonly DbContextOptions<DailyDbContext> _options;
        public Factory(DbContextOptions<DailyDbContext> options) => _options = options;
        public DailyDbContext CreateDbContext() => new(_options);
    }
}
