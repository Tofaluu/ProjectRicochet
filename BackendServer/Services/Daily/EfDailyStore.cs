using Microsoft.EntityFrameworkCore;

namespace BackendServer.Services
{
    /// <summary>Daily puzzles and results in the database (PostgreSQL in production) through EF Core.</summary>
    public sealed class EfDailyStore : IDailyStore
    {
        private readonly IDbContextFactory<DailyDbContext> _contexts;

        public EfDailyStore(IDbContextFactory<DailyDbContext> contexts)
        {
            _contexts = contexts;
        }

        public async Task<DailyPuzzleRecord?> GetPuzzleAsync(int number)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Puzzles.AsNoTracking().FirstOrDefaultAsync(p => p.Number == number);
        }

        public async Task SavePuzzleAsync(DailyPuzzleRecord puzzle)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            if (await db.Puzzles.AnyAsync(p => p.Number == puzzle.Number))
                return;
            db.Puzzles.Add(puzzle);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Saved by someone else in between: theirs stands.
            }
        }

        public async Task<DailyEntryRecord> OpenAsync(int number, string guestId, string name, DateTime now)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            DailyPlayerRecord? player = await db.Players.FindAsync(guestId);
            if (player == null)
                db.Players.Add(new DailyPlayerRecord { GuestId = guestId, Name = name, CreatedUtc = now, LastSeenUtc = now });
            else
            {
                player.Name = name;
                player.LastSeenUtc = now;
            }
            DailyEntryRecord? entry = await db.Entries.FindAsync(number, guestId);
            if (entry == null)
            {
                entry = new DailyEntryRecord { Number = number, GuestId = guestId, OpenedUtc = now };
                db.Entries.Add(entry);
            }
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The same player opened it twice at once (two tabs): the other call's rows stand.
                await using DailyDbContext again = await _contexts.CreateDbContextAsync();
                return await again.Entries.AsNoTracking().FirstAsync(e => e.Number == number && e.GuestId == guestId);
            }
            return entry;
        }

        public async Task<DailyEntryRecord?> GetEntryAsync(int number, string guestId)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Number == number && e.GuestId == guestId);
        }

        public async Task SaveBestAsync(int number, string guestId, int moves, double seconds, byte[] solution, DateTime now)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            DailyEntryRecord entry = await db.Entries.FirstAsync(e => e.Number == number && e.GuestId == guestId);
            entry.BestMoves = moves;
            entry.BestSeconds = seconds;
            entry.BestSolution = solution;
            entry.SolvedUtc = now;
            await db.SaveChangesAsync();
        }

        public async Task<(IReadOnlyList<DailyRow> Top, int Players)> GetLeaderboardAsync(int number, int count)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            IQueryable<DailyEntryRecord> solved = db.Entries.Where(e => e.Number == number && e.BestMoves > 0);
            List<DailyRow> top = await solved
                .OrderBy(e => e.BestMoves).ThenBy(e => e.BestSeconds)
                .Take(count)
                .Join(db.Players, e => e.GuestId, p => p.GuestId, (e, p) => new DailyRow(e.GuestId, p.Name, e.BestMoves, e.BestSeconds))
                .ToListAsync();
            // Join can reorder rows; the leaderboard order is what matters.
            top = top.OrderBy(r => r.Moves).ThenBy(r => r.Seconds).ToList();
            return (top, await solved.CountAsync());
        }

        public async Task<int> GetRankAsync(int number, int moves, double seconds)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return 1 + await db.Entries.CountAsync(e => e.Number == number && e.BestMoves > 0
                && (e.BestMoves < moves || (e.BestMoves == moves && e.BestSeconds < seconds)));
        }

        public async Task<IReadOnlyList<int>> GetSolvedNumbersAsync(string guestId, int from, int to)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Entries
                .Where(e => e.GuestId == guestId && e.BestMoves > 0 && e.Number >= from && e.Number <= to)
                .Select(e => e.Number).OrderBy(n => n).ToListAsync();
        }

        public async Task<IReadOnlyList<DailySummary>> GetSummariesAsync(int to)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            var rows = await db.Puzzles.Where(p => p.Number <= to).OrderBy(p => p.Number)
                .Select(p => new { p.Number, p.Date, Length = p.Shortest.Length }).ToListAsync();
            return rows.Select(r => new DailySummary(r.Number, r.Date, r.Length)).ToList();
        }

        public async Task<IReadOnlyList<DailyEntryRecord>> GetEntriesAsync(string guestId)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Entries.AsNoTracking().Where(e => e.GuestId == guestId).ToListAsync();
        }

        public async Task<IReadOnlyList<DailyArchiveRecord>> GetArchiveAsync(string guestId)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Archive.AsNoTracking().Where(e => e.GuestId == guestId).ToListAsync();
        }

        public async Task<DailyArchiveRecord?> GetArchiveEntryAsync(int number, string guestId)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            return await db.Archive.AsNoTracking().FirstOrDefaultAsync(e => e.Number == number && e.GuestId == guestId);
        }

        public async Task SaveArchiveEntryAsync(DailyArchiveRecord entry)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            DailyArchiveRecord? stored = await db.Archive.FindAsync(entry.Number, entry.GuestId);
            if (stored == null)
                db.Archive.Add(entry);
            else
                db.Entry(stored).CurrentValues.SetValues(entry);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two tabs saved at once: the other one's row stands.
            }
        }

        public async Task<IReadOnlyList<DailySolve>> GetStreakSolvesAsync(int from, int to)
        {
            await using DailyDbContext db = await _contexts.CreateDbContextAsync();
            IQueryable<string> going = db.Entries.Where(e => e.BestMoves > 0 && e.Number >= to - 1 && e.Number <= to)
                .Select(e => e.GuestId);
            var rows = await db.Entries
                .Where(e => e.BestMoves > 0 && e.Number >= from && e.Number <= to && going.Contains(e.GuestId))
                .Join(db.Players, e => e.GuestId, p => p.GuestId, (e, p) => new { e.GuestId, p.Name, e.Number })
                .ToListAsync();
            return rows.Select(r => new DailySolve(r.GuestId, r.Name, r.Number)).ToList();
        }
    }
}
