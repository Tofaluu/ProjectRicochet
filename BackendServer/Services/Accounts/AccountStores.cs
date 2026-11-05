using Microsoft.EntityFrameworkCore;

namespace BackendServer.Services
{
    /// <summary>Where accounts live: PostgreSQL in production (<see cref="EfAccountStore"/>), memory otherwise.</summary>
    public interface IAccountStore
    {
        Task<AccountRecord?> FindByLoginAsync(string provider, string subject);
        Task<AccountRecord?> FindByHomeAsync(string guestId);

        /// <summary>A new account with its first login.</summary>
        Task<AccountRecord> CreateAsync(string homeGuestId, string provider, string subject, string? email, DateTime now);

        Task TouchAsync(long accountId, string? email, DateTime now);

        /// <summary>Removes the account and its logins (its results are the caller's to delete).</summary>
        Task DeleteAsync(long accountId);
    }

    public sealed class InMemoryAccountStore : IAccountStore
    {
        private readonly object _gate = new();
        private readonly List<AccountRecord> _accounts = new();
        private readonly List<AccountLoginRecord> _logins = new();

        public Task<AccountRecord?> FindByLoginAsync(string provider, string subject)
        {
            lock (_gate)
            {
                AccountLoginRecord? login = _logins.FirstOrDefault(l => l.Provider == provider && l.Subject == subject);
                return Task.FromResult(login == null ? null : Copy(_accounts.First(a => a.Id == login.AccountId)));
            }
        }

        public Task<AccountRecord?> FindByHomeAsync(string guestId)
        {
            lock (_gate)
            {
                AccountRecord? account = _accounts.FirstOrDefault(a => a.HomeGuestId == guestId);
                return Task.FromResult(account == null ? null : Copy(account));
            }
        }

        public Task<AccountRecord> CreateAsync(string homeGuestId, string provider, string subject, string? email, DateTime now)
        {
            lock (_gate)
            {
                var account = new AccountRecord
                {
                    Id = _accounts.Count == 0 ? 1 : _accounts.Max(a => a.Id) + 1, HomeGuestId = homeGuestId, Email = email,
                    CreatedUtc = now, LastSignInUtc = now,
                };
                _accounts.Add(account);
                _logins.Add(new AccountLoginRecord { Provider = provider, Subject = subject, AccountId = account.Id, CreatedUtc = now });
                return Task.FromResult(Copy(account));
            }
        }

        public Task TouchAsync(long accountId, string? email, DateTime now)
        {
            lock (_gate)
            {
                AccountRecord account = _accounts.First(a => a.Id == accountId);
                account.LastSignInUtc = now;
                if (email != null)
                    account.Email = email;
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(long accountId)
        {
            lock (_gate)
            {
                _accounts.RemoveAll(a => a.Id == accountId);
                _logins.RemoveAll(l => l.AccountId == accountId);
            }
            return Task.CompletedTask;
        }

        private static AccountRecord Copy(AccountRecord a) => new()
        {
            Id = a.Id, HomeGuestId = a.HomeGuestId, Email = a.Email, CreatedUtc = a.CreatedUtc, LastSignInUtc = a.LastSignInUtc,
        };
    }

    /// <summary>Accounts in the database, next to the daily puzzle's tables.</summary>
    public sealed class EfAccountStore : IAccountStore
    {
        private readonly IDbContextFactory<DailyDbContext> _contexts;

        public EfAccountStore(IDbContextFactory<DailyDbContext> contexts)
        {
            _contexts = contexts;
        }

        public async Task<AccountRecord?> FindByLoginAsync(string provider, string subject)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.AccountLogins.AsNoTracking()
                .Where(l => l.Provider == provider && l.Subject == subject)
                .Join(db.Accounts, l => l.AccountId, a => a.Id, (l, a) => a)
                .FirstOrDefaultAsync();
        }

        public async Task<AccountRecord?> FindByHomeAsync(string guestId)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.HomeGuestId == guestId);
        }

        public async Task<AccountRecord> CreateAsync(string homeGuestId, string provider, string subject, string? email, DateTime now)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            var account = new AccountRecord { HomeGuestId = homeGuestId, Email = email, CreatedUtc = now, LastSignInUtc = now };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            db.AccountLogins.Add(new AccountLoginRecord { Provider = provider, Subject = subject, AccountId = account.Id, CreatedUtc = now });
            await db.SaveChangesAsync();
            return account;
        }

        public async Task TouchAsync(long accountId, string? email, DateTime now)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            AccountRecord account = await db.Accounts.FirstAsync(a => a.Id == accountId);
            account.LastSignInUtc = now;
            if (email != null)
                account.Email = email;
            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(long accountId)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            await db.AccountLogins.Where(l => l.AccountId == accountId).ExecuteDeleteAsync();
            await db.Accounts.Where(a => a.Id == accountId).ExecuteDeleteAsync();
        }
    }
}
