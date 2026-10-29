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

    [Fact]
    public async Task PastPuzzles_OnlyAfterTheirDay_AndKeptApartFromIt()
    {
        await PastPuzzleScenario(Create());
    }

    [Fact]
    public async Task StreakLeaderboard_CurrentStreaksOnly_LongestFirst()
    {
        await StreakScenario(Create());
    }

    [Fact]
    public async Task TheDatabaseStore_KeepsPastPlaysAndStreaksTheSame()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<DailyDbContext>().UseSqlite(connection).Options;
        using (var db = new DailyDbContext(options))
            db.Database.EnsureCreated();
        var store = new EfDailyStore(new Factory(options));

        await PastPuzzleScenario(Create(store));
        Assert.Single(await store.GetArchiveAsync(Bob));
        Assert.Single(await store.GetArchiveAsync(Alice));
    }

    [Fact]
    public async Task TheDatabaseStore_StreakLeaderboard()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<DailyDbContext>().UseSqlite(connection).Options;
        using (var db = new DailyDbContext(options))
            db.Database.EnsureCreated();
        await StreakScenario(Create(new EfDailyStore(new Factory(options))));
    }

    // Alice solves day 1 (a longer solution), Bob never opens it. On day 2 both play day 1 again: Bob's solve and Alice's
    // look at the solution show on their calendars, but day 1's leaderboard and their streaks stay as they were.
    private static async Task PastPuzzleScenario((DailyService daily, FakeTimeProvider time) setup)
    {
        var (daily, time) = setup;
        DailyPuzzleInfo dayOne = await daily.GetDailyAsync(Alice, "Alice");
        Move[] shortest = Shortest(dayOne);
        Move[] longer = LobbyHarness.LongerSolution(BoardCodec.Decode(dayOne.Board));
        await daily.SubmitAsync(Alice, 1, Encode(longer));

        // Today's puzzle is never in the archive, nor its solution.
        await Assert.ThrowsAsync<HubException>(() => daily.GetPastAsync(Alice, 1));
        await Assert.ThrowsAsync<HubException>(() => daily.ShowSolutionAsync(Alice, 1));
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitPastAsync(Alice, 1, Encode(shortest)));

        time.Advance(TimeSpan.FromDays(1));
        DailyPuzzleInfo dayTwo = await daily.GetDailyAsync(Alice, "Alice");
        await daily.GetDailyAsync(Bob, "Bob");
        await Assert.ThrowsAsync<HubException>(() => daily.GetPastAsync(Alice, 2)); // today
        await Assert.ThrowsAsync<HubException>(() => daily.GetPastAsync(Alice, 3)); // tomorrow, even if it's made already
        await Assert.ThrowsAsync<HubException>(() => daily.GetPastAsync(Alice, 0));

        DailyPastInfo past = await daily.GetPastAsync(Bob, 1);
        Assert.Equal(dayOne.Board, past.Board);
        Assert.Equal(dayOne.ShortestLength, past.ShortestLength);
        Assert.False(past.Opened);
        Assert.Equal(1, past.Players);
        await Assert.ThrowsAsync<HubException>(() => daily.SubmitPastAsync(Bob, 1, Encode(new[] { new Move(RobotColor.Red, Direction.Up) })));

        DailyPastResult first = await daily.SubmitPastAsync(Bob, 1, Encode(longer));
        Assert.True(first.Improved);
        DailyPastResult better = await daily.SubmitPastAsync(Bob, 1, Encode(shortest));
        Assert.True(better.Improved);
        Assert.Equal(shortest.Length, better.LaterMoves);
        Assert.False((await daily.SubmitPastAsync(Bob, 1, Encode(longer))).Improved);
        Assert.Equal(Encode(shortest), (await daily.GetPastAsync(Bob, 1)).LaterSolution);

        DailySolution solution = await daily.ShowSolutionAsync(Alice, 1);
        Assert.Equal(dayOne.ShortestLength, solution.Moves.Length);
        DailyPastInfo alicePast = await daily.GetPastAsync(Alice, 1);
        Assert.True(alicePast.SawSolution);
        Assert.True(alicePast.Opened);
        Assert.Equal(longer.Length, alicePast.BestMoves);
        Assert.Equal(Encode(longer), alicePast.BestSolution);

        // Day 1's leaderboard: still only Alice, with her solution on the day.
        DailyLeaderboard board = await daily.GetLeaderboardAsync(Bob, 1);
        Assert.Equal(1, board.Players);
        Assert.Equal(longer.Length, board.Top.Single().Moves);
        Assert.Equal(0, board.MyRank);
        Assert.Equal(0, (await daily.GetDailyAsync(Bob, "Bob")).Streak);

        DailyCalendar bobs = await daily.GetCalendarAsync(Bob);
        Assert.Equal(2, bobs.Today);
        Assert.Equal(new[] { 1, 2 }, bobs.Days.Select(d => d.Number));
        Assert.Equal("2026-10-12", bobs.Days[0].Date);
        Assert.Equal(dayOne.ShortestLength, bobs.Days[0].ShortestLength);
        Assert.False(bobs.Days[0].Opened);
        Assert.Equal(shortest.Length, bobs.Days[0].LaterMoves);
        Assert.False(bobs.Days[0].SawSolution);
        Assert.True(bobs.Days[1].Opened); // today: opened, not solved
        Assert.Equal(0, bobs.Days[1].BestMoves);

        DailyCalendar alices = await daily.GetCalendarAsync(Alice);
        Assert.Equal(longer.Length, alices.Days[0].BestMoves);
        Assert.Equal(0, alices.Days[0].LaterMoves);
        Assert.True(alices.Days[0].SawSolution);
        Assert.Equal(dayTwo.ShortestLength, alices.Days[1].ShortestLength);
    }

    // Day 5: Alice solved days 1-3 (3, over), Bob days 3-5 (3, going), Erin days 1-2 then 5 (best 2, now on 1), Dave day 4
    // only (1, going: today can still be saved), Carol day 1 only (1, over), plus a later play of day 2 by Carol that
    // doesn't count.
    private static async Task StreakScenario((DailyService daily, FakeTimeProvider time) setup)
    {
        var (daily, time) = setup;
        const string dave = "dave-guest-id-00004";
        const string erin = "erin-guest-id-00005";
        var solvers = new[]
        {
            new[] { Alice, Carol, erin },
            new[] { Alice, erin },
            new[] { Alice, Bob },
            new[] { Bob, dave },
            new[] { Bob, erin },
        };
        for (int day = 0; day < solvers.Length; day++)
        {
            if (day > 0)
                time.Advance(TimeSpan.FromDays(1));
            foreach (string guest in solvers[day])
            {
                DailyPuzzleInfo info = await daily.GetDailyAsync(guest, guest.Split('-')[0]);
                await daily.SubmitAsync(guest, info.Number, Encode(Shortest(info)));
            }
        }
        DailyPastInfo dayTwo = await daily.GetPastAsync(Carol, 2);
        await daily.SubmitPastAsync(Carol, 2, Encode(Solver.Solve(BoardCodec.Decode(dayTwo.Board)).Moves));

        StreakLeaderboard board = await daily.GetStreakLeaderboardAsync(Bob);
        // Equal longest streaks share a place; the one going now (Bob's 3) is listed before one that's over (Alice's).
        Assert.Equal(new[] { "bob", "alice", "erin", "dave", "carol" }, board.Top.Select(e => e.Name));
        Assert.Equal(new[] { 3, 3, 2, 1, 1 }, board.Top.Select(e => e.Days));
        Assert.Equal(new[] { 3, 0, 1, 1, 0 }, board.Top.Select(e => e.Current));
        Assert.Equal(new[] { 1, 1, 3, 4, 4 }, board.Top.Select(e => e.Rank));
        Assert.True(board.Top[0].IsMe);
        Assert.Equal(3, board.MyBest);
        Assert.Equal(3, board.MyCurrent);
        Assert.Equal(1, board.MyRank);
        Assert.Equal(5, board.Players);

        StreakLeaderboard erins = await daily.GetStreakLeaderboardAsync(erin);
        Assert.Equal(2, erins.MyBest);
        Assert.Equal(1, erins.MyCurrent);
        Assert.Equal(3, erins.MyRank);

        StreakLeaderboard franks = await daily.GetStreakLeaderboardAsync("frank-guest-id-0006");
        Assert.Equal(0, franks.MyBest);
        Assert.Equal(0, franks.MyRank);
        Assert.DoesNotContain(franks.Top, e => e.IsMe);
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
