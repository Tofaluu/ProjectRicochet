using BackendServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharedLogic;

namespace BackendServer.Tests;

/// <summary>Accounts: signing in claims or merges a device's guest progress; deleting removes everything.</summary>
public class AccountTests
{
    private const string Launch = "2026-10-12";
    private static readonly DateTimeOffset LaunchNoon = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);
    private const string Laptop = "laptop-guest-id-0001";
    private const string Phone = "phone-guest-id-00002";
    private const string Tablet = "tablet-guest-id-0003";

    // Tokens "google:<sub>" are valid for that Google user; anything else isn't.
    private sealed class FakeGoogle : IGoogleTokenValidator
    {
        public Task<ExternalIdentity?> ValidateAsync(string idToken) => Task.FromResult(idToken.StartsWith("google:")
            ? new ExternalIdentity(idToken.Substring(7), idToken.Substring(7) + "@gmail.com") : null);
    }

    private sealed record Setup(AccountService Accounts, DailyService Daily, IDailyStore Store, IUsageStore Usage, FakeTimeProvider Time);

    private static Setup Create(IDailyStore store, IUsageStore usage, IAccountStore accounts)
    {
        var time = new FakeTimeProvider(LaunchNoon);
        var daily = new DailyService(store, Options.Create(new DailyOptions { LaunchDate = Launch, Seed = 7, MinMoves = 4, MaxMoves = 5 }),
            time, NullLogger<DailyService>.Instance);
        var service = new AccountService(accounts, store, usage, new FakeGoogle(), time, NullLogger<AccountService>.Instance);
        return new Setup(service, daily, store, usage, time);
    }

    private static byte[] Shortest(DailyPuzzleInfo info) => MoveCodec.Encode(Solver.Solve(BoardCodec.Decode(info.Board)).Moves);

    [Fact]
    public async Task InMemory() => await Scenario(Create(new InMemoryDailyStore(), new InMemoryUsageStore(), new InMemoryAccountStore()));

    [Fact]
    public async Task TheDatabaseStores_WorkTheSame()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<DailyDbContext>().UseSqlite(connection).Options;
        using (var db = new DailyDbContext(options))
            db.Database.EnsureCreated();
        var factory = new Factory(options);
        await Scenario(Create(new EfDailyStore(factory), new EfUsageStore(factory), new EfAccountStore(factory)));
    }

    // The laptop solves day 1 and signs in (its progress becomes the account's). The phone played day 1 slower and day 2,
    // then signs in: its days join the account, the laptop's faster day 1 stays. A tablet signed in to the account then signs
    // in as someone else: a new, empty account, the first one untouched. Deleting the first account removes its results.
    private static async Task Scenario(Setup s)
    {
        DailyPuzzleInfo day1 = await s.Daily.GetDailyAsync(Laptop, "Sam");
        s.Time.Advance(TimeSpan.FromSeconds(30));
        await s.Daily.SubmitAsync(Laptop, 1, Shortest(day1));
        await s.Usage.AddAsync(new UsageEventRecord { AtUtc = LaunchNoon.UtcDateTime, GuestId = Laptop, Kind = "visit" });

        Assert.Null(await s.Accounts.GetAccountAsync(Laptop));
        await Assert.ThrowsAsync<HubException>(() => s.Accounts.SignInWithGoogleAsync(Laptop, "forged"));
        AccountInfo first = await s.Accounts.SignInWithGoogleAsync(Laptop, "google:sam");
        Assert.Equal(Laptop, first.GuestId); // the device's own id becomes the account's home
        Assert.Equal("sam@gmail.com", first.Email);
        Assert.False(first.Merged);

        await s.Daily.GetDailyAsync(Phone, "Sam");
        s.Time.Advance(TimeSpan.FromMinutes(5));
        await s.Daily.SubmitAsync(Phone, 1, Shortest(day1));
        s.Time.Advance(TimeSpan.FromDays(1));
        DailyPuzzleInfo day2 = await s.Daily.GetDailyAsync(Phone, "Sam");
        await s.Daily.SubmitAsync(Phone, 2, Shortest(day2));

        AccountInfo phone = await s.Accounts.SignInWithGoogleAsync(Phone, "google:sam");
        Assert.Equal(Laptop, phone.GuestId);
        Assert.True(phone.Merged);
        IReadOnlyList<DailyEntryRecord> entries = await s.Store.GetEntriesAsync(Laptop);
        Assert.Equal(new[] { 1, 2 }, entries.Select(e => e.Number).OrderBy(n => n));
        Assert.Equal(30, entries.Single(e => e.Number == 1).BestSeconds, 3); // the faster day 1
        Assert.Empty(await s.Store.GetEntriesAsync(Phone));
        Assert.False((await s.Accounts.SignInWithGoogleAsync(Laptop, "google:sam")).Merged); // signing in again: nothing to move

        AccountInfo other = await s.Accounts.SignInWithGoogleAsync(Laptop, "google:alex"); // a device playing as Sam's account
        Assert.NotEqual(Laptop, other.GuestId);
        Assert.Equal(2, (await s.Store.GetEntriesAsync(Laptop)).Count); // Sam's progress stays Sam's
        Assert.Equal("alex@gmail.com", (await s.Accounts.GetAccountAsync(other.GuestId))!.Email);

        // The stats page's counts: new accounts and sign-ins to existing ones, under the account's home id.
        IReadOnlyList<UsageEventRecord> events = await s.Usage.GetSinceAsync(DateTime.MinValue);
        Assert.Equal(new[] { Laptop, other.GuestId }, events.Where(e => e.Kind == UsageService.AccountNew).Select(e => e.GuestId));
        Assert.Equal(new[] { Laptop, Laptop }, events.Where(e => e.Kind == UsageService.SignIn).Select(e => e.GuestId));

        await Assert.ThrowsAsync<HubException>(() => s.Accounts.DeleteAccountAsync(Tablet)); // not an account
        await s.Accounts.DeleteAccountAsync(Laptop);
        Assert.Null(await s.Accounts.GetAccountAsync(Laptop));
        Assert.Empty(await s.Store.GetEntriesAsync(Laptop));
        Assert.DoesNotContain(await s.Usage.GetSinceAsync(DateTime.MinValue), e => e.GuestId == Laptop);
        Assert.NotNull(await s.Accounts.GetAccountAsync(other.GuestId)); // other accounts untouched

        await s.Usage.AddAsync(new UsageEventRecord { AtUtc = LaunchNoon.UtcDateTime, GuestId = Tablet, Kind = "visit", Internal = true });
        AccountInfo again = await s.Accounts.SignInWithGoogleAsync(Tablet, "google:sam"); // a deleted account starts over
        Assert.Equal(Tablet, again.GuestId);
        Assert.DoesNotContain(await s.Usage.GetSinceAsync(DateTime.MinValue), e => e.GuestId == Tablet); // signed in on a device marked internal: not counted
    }

    [Fact]
    public async Task TheRealGoogleCheck_RefusesJunk_AndIsOffWithoutAClientId()
    {
        var off = new GoogleTokenValidator(Options.Create(new AccountOptions()));
        Assert.Null(await off.ValidateAsync("google:sam"));
        var on = new GoogleTokenValidator(Options.Create(new AccountOptions { GoogleClientId = "test.apps.googleusercontent.com" }));
        Assert.Null(await on.ValidateAsync("not-a-token"));
        Assert.Null(await on.ValidateAsync(""));
    }

    private sealed class Factory : IDbContextFactory<DailyDbContext>
    {
        private readonly DbContextOptions<DailyDbContext> _options;
        public Factory(DbContextOptions<DailyDbContext> options) => _options = options;
        public DailyDbContext CreateDbContext() => new(_options);
    }
}
