using BackendServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BackendServer.Tests;

/// <summary>Usage tracking: what's accepted, the per-guest cap, and the stats (internal devices left out).</summary>
public class UsageTests
{
    // Noon in Toronto (EDT, UTC-4).
    private static readonly DateTimeOffset Noon = new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);
    private const string Alice = "alice-guest-id-0001";
    private const string Bob = "bob-guest-id-000002";
    private const string Me = "developer-guest-0001";

    private static (UsageService usage, FakeTimeProvider time) Create(IUsageStore? store = null, string? key = "secret",
        DateTimeOffset? start = null)
    {
        var time = new FakeTimeProvider(start ?? Noon);
        var usage = new UsageService(store ?? new InMemoryUsageStore(),
            Options.Create(new UsageOptions { StatsKey = key, MaxEventsPerHour = 5, StatsDays = 3 }),
            Options.Create(new DailyOptions()), time);
        return (usage, time);
    }

    private static UsageEventRequest E(string guest, string kind, bool isInternal = false) =>
        new() { GuestId = guest, Kind = kind, Internal = isInternal };

    [Fact]
    public async Task OnlyKnownKindsAndWellFormedIds_AndAHourlyCap()
    {
        var (usage, time) = Create();
        Assert.False(await usage.RecordAsync(E("short", "visit")));
        Assert.False(await usage.RecordAsync(E(Alice, "hack")));
        Assert.False(await usage.RecordAsync(new UsageEventRequest { Kind = "visit" }));

        for (int i = 0; i < 5; i++)
            Assert.True(await usage.RecordAsync(E(Alice, "visit")));
        Assert.False(await usage.RecordAsync(E(Alice, "visit"))); // 6th this hour
        Assert.True(await usage.RecordAsync(E(Bob, "visit"))); // others aren't affected

        time.Advance(TimeSpan.FromHours(1));
        Assert.True(await usage.RecordAsync(E(Alice, "visit")));
    }

    [Fact]
    public async Task StatsKey_MustMatch_AndNoKeyMeansNoPage()
    {
        var (usage, _) = Create();
        Assert.True(usage.StatsKeyMatches("secret"));
        Assert.False(usage.StatsKeyMatches("wrong"));
        Assert.False(usage.StatsKeyMatches(null));
        Assert.False(Create(key: null).usage.StatsKeyMatches(""));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Summary_CountsPlayersNewAndReturning_LeavingOutInternalDevices()
    {
        await SummaryScenario(new InMemoryUsageStore());
    }

    [Fact]
    public async Task TheDatabaseStore_WorksTheSame()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<DailyDbContext>().UseSqlite(connection).Options;
        using (var db = new DailyDbContext(options))
            db.Database.EnsureCreated();
        await SummaryScenario(new EfUsageStore(new Factory(options)));
        await OnlineScenario(new EfUsageStore(new Factory(options)));
    }

    [Fact]
    public async Task Online_CountsRecentPings_AndKeepsEachDaysPeak()
    {
        await OnlineScenario(new InMemoryUsageStore());
    }

    // Alice and Bob ping (the developer's device too, never counted); Bob closes the game, so after 2 minutes only Alice is
    // online, but the day's peak stays 2. A new service (server restart) never lowers the stored peak. Next day starts at 0.
    private static async Task OnlineScenario(IUsageStore store)
    {
        var (usage, time) = Create(store, start: Noon.AddDays(10));
        Assert.False(await usage.PingAsync(new OnlinePingRequest { GuestId = "short" }));
        Assert.True(await usage.PingAsync(new OnlinePingRequest { GuestId = Alice }));
        Assert.True(await usage.PingAsync(new OnlinePingRequest { GuestId = Bob }));
        Assert.True(await usage.PingAsync(new OnlinePingRequest { GuestId = Me, Internal = true }));
        UsageSummary summary = await usage.SummaryAsync();
        Assert.Equal(2, summary.OnlineNow);
        Assert.Equal(2, summary.Days[0].PeakOnline);

        time.Advance(TimeSpan.FromSeconds(60));
        await usage.PingAsync(new OnlinePingRequest { GuestId = Alice });
        time.Advance(TimeSpan.FromSeconds(70)); // Bob's last ping is 130 s old, Alice's 70 s
        summary = await usage.SummaryAsync();
        Assert.Equal(1, summary.OnlineNow);
        Assert.Equal(2, summary.Days[0].PeakOnline);

        var (restarted, _) = Create(store, start: time.GetUtcNow());
        Assert.True(await restarted.PingAsync(new OnlinePingRequest { GuestId = Bob }));
        summary = await restarted.SummaryAsync();
        Assert.Equal(1, summary.OnlineNow); // memory only: Alice's earlier ping is gone
        Assert.Equal(2, summary.Days[0].PeakOnline);

        time.Advance(TimeSpan.FromDays(1));
        summary = await usage.SummaryAsync();
        Assert.Equal(0, summary.OnlineNow);
        Assert.Equal(0, summary.Days[0].PeakOnline);
        Assert.Equal(2, summary.Days[1].PeakOnline);
        Assert.Contains("Online now: <b>0</b>", UsagePage.Render(summary));
    }

    // Yesterday: Alice visits and solves in practice. Today: Alice comes back, Bob is new and opens + solves the daily,
    // and the developer's marked device plays too (never counted).
    private static async Task SummaryScenario(IUsageStore store)
    {
        var (usage, time) = Create(store, start: Noon.AddDays(-1));
        await usage.RecordAsync(E(Alice, "visit"));
        await usage.RecordAsync(E(Alice, "practice_solve"));
        await usage.RecordAsync(E(Me, "visit", isInternal: true));

        time.SetUtcNow(Noon);
        await usage.RecordAsync(E(Alice, "visit"));
        await usage.RecordAsync(E(Bob, "visit"));
        await usage.RecordAsync(E(Bob, "daily_open"));
        await usage.RecordAsync(E(Bob, "daily_solve"));
        await usage.RecordAsync(E(Bob, "tutorial_start"));
        await usage.RecordAsync(E(Bob, "tutorial_finish"));
        await usage.RecordAsync(E(Me, "visit", isInternal: true));
        await usage.RecordAsync(E(Me, "lobby_join", isInternal: true));

        UsageSummary summary = await usage.SummaryAsync();
        IReadOnlyList<UsageDay> days = summary.Days;
        Assert.Equal(3, days.Count); // StatsDays, newest first
        Assert.Equal(new DateOnly(2026, 10, 19), days[0].Day);
        Assert.Equal(new UsageDay(new DateOnly(2026, 10, 19), 2, 1, 1, 2, 0, 0, 1, 1, 1, 1, 0, 0), days[0]);
        Assert.Equal(new UsageDay(new DateOnly(2026, 10, 18), 1, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0), days[1]);
        Assert.Equal(0, days[2].Players);

        string page = UsagePage.Render(summary);
        Assert.Contains("Mon Oct 19", page);
        Assert.Contains("Tutorial finished", page);
        Assert.DoesNotContain(Me, page); // no ids on the page
    }

    private sealed class Factory : IDbContextFactory<DailyDbContext>
    {
        private readonly DbContextOptions<DailyDbContext> _options;
        public Factory(DbContextOptions<DailyDbContext> options) => _options = options;
        public DailyDbContext CreateDbContext() => new(_options);
    }
}
