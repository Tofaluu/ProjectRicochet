using Microsoft.EntityFrameworkCore;

namespace BackendServer.Services
{
    /// <summary>Usage events in memory: used when no database is configured (local runs, tests).</summary>
    public sealed class InMemoryUsageStore : IUsageStore
    {
        private readonly object _gate = new();
        private readonly List<UsageEventRecord> _events = new();

        public Task AddAsync(UsageEventRecord e)
        {
            lock (_gate)
            {
                e.Id = _events.Count + 1;
                _events.Add(e);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UsageEventRecord>> GetSinceAsync(DateTime fromUtc)
        {
            lock (_gate)
                return Task.FromResult<IReadOnlyList<UsageEventRecord>>(_events.Where(e => !e.Internal && e.AtUtc >= fromUtc).ToList());
        }

        public Task<IReadOnlyDictionary<string, DateTime>> GetFirstSeenAsync(IReadOnlyCollection<string> guestIds)
        {
            lock (_gate)
            {
                IReadOnlyDictionary<string, DateTime> first = _events
                    .Where(e => !e.Internal && guestIds.Contains(e.GuestId))
                    .GroupBy(e => e.GuestId)
                    .ToDictionary(g => g.Key, g => g.Min(e => e.AtUtc));
                return Task.FromResult(first);
            }
        }
    }

    /// <summary>Usage events in the database (PostgreSQL in production), next to the daily puzzle's tables.</summary>
    public sealed class EfUsageStore : IUsageStore
    {
        private readonly IDbContextFactory<DailyDbContext> _contexts;

        public EfUsageStore(IDbContextFactory<DailyDbContext> contexts)
        {
            _contexts = contexts;
        }

        public async Task AddAsync(UsageEventRecord e)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            db.UsageEvents.Add(e);
            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<UsageEventRecord>> GetSinceAsync(DateTime fromUtc)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.UsageEvents.AsNoTracking().Where(e => !e.Internal && e.AtUtc >= fromUtc).ToListAsync();
        }

        public async Task<IReadOnlyDictionary<string, DateTime>> GetFirstSeenAsync(IReadOnlyCollection<string> guestIds)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            List<string> ids = guestIds.ToList();
            return await db.UsageEvents.AsNoTracking()
                .Where(e => !e.Internal && ids.Contains(e.GuestId))
                .GroupBy(e => e.GuestId)
                .Select(g => new { g.Key, First = g.Min(e => e.AtUtc) })
                .ToDictionaryAsync(x => x.Key, x => x.First);
        }
    }
}
