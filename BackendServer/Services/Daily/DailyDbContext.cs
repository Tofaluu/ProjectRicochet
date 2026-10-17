using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendServer.Services
{
    /// <summary>
    /// The daily puzzle's tables. Schema changes go through EF Core migrations (<c>BackendServer/Migrations</c>,
    /// applied at startup): <c>dotnet ef migrations add Name --project BackendServer</c>.
    /// </summary>
    public class DailyDbContext : DbContext
    {
        public DailyDbContext(DbContextOptions<DailyDbContext> options) : base(options) { }

        public DbSet<DailyPuzzleRecord> Puzzles => Set<DailyPuzzleRecord>();
        public DbSet<DailyPlayerRecord> Players => Set<DailyPlayerRecord>();
        public DbSet<DailyEntryRecord> Entries => Set<DailyEntryRecord>();

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<DailyPuzzleRecord>(e =>
            {
                e.ToTable("daily_puzzles");
                e.HasKey(p => p.Number);
                e.Property(p => p.Number).ValueGeneratedNever();
                e.HasIndex(p => p.Date).IsUnique();
            });
            model.Entity<DailyPlayerRecord>(e =>
            {
                e.ToTable("daily_players");
                e.HasKey(p => p.GuestId);
                e.Property(p => p.GuestId).HasMaxLength(64);
                e.Property(p => p.Name).HasMaxLength(20);
            });
            model.Entity<DailyEntryRecord>(e =>
            {
                e.ToTable("daily_entries");
                e.HasKey(x => new { x.Number, x.GuestId });
                e.Property(x => x.GuestId).HasMaxLength(64);
                // The leaderboard: one day's solvers, fewest moves then fastest.
                e.HasIndex(x => new { x.Number, x.BestMoves, x.BestSeconds });
                e.HasIndex(x => x.GuestId);
            });
        }
    }

    /// <summary>For <c>dotnet ef</c> (generating migrations doesn't connect to a database).</summary>
    public class DailyDbContextFactory : IDesignTimeDbContextFactory<DailyDbContext>
    {
        public DailyDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<DailyDbContext>().UseNpgsql("Host=localhost;Database=ricochet").Options);
    }
}
