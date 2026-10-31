using Microsoft.EntityFrameworkCore;

namespace BackendServer.Services
{
    /// <summary>Usage events in memory: used when no database is configured (local runs, tests).</summary>
    public sealed class InMemoryUsageStore : IUsageStore
    {
        private readonly object _gate = new();
        private readonly List<UsageEventRecord> _events = new();
        private readonly Dictionary<DateOnly, int> _peaks = new();

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

        public Task RaisePeakAsync(DateOnly day, int peak, DateTime atUtc)
        {
            lock (_gate)
                if (peak > _peaks.GetValueOrDefault(day))
                    _peaks[day] = peak;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<DateOnly, int>> GetPeaksAsync(DateOnly first)
        {
            lock (_gate)
                return Task.FromResult<IReadOnlyDictionary<DateOnly, int>>(_peaks.Where(p => p.Key >= first).ToDictionary(p => p.Key, p => p.Value));
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

        public async Task RaisePeakAsync(DateOnly day, int peak, DateTime atUtc)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            OnlinePeakRecord? row = await db.OnlinePeaks.FindAsync(day);
            if (row == null)
                db.OnlinePeaks.Add(new OnlinePeakRecord { Day = day, Peak = peak, AtUtc = atUtc });
            else if (peak > row.Peak)
            {
                row.Peak = peak;
                row.AtUtc = atUtc;
            }
            else
                return;
            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyDictionary<DateOnly, int>> GetPeaksAsync(DateOnly first)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.OnlinePeaks.AsNoTracking().Where(p => p.Day >= first).ToDictionaryAsync(p => p.Day, p => p.Peak);
        }
    }
}
