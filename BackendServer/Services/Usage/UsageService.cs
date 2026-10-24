using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace BackendServer.Services
{
    /// <summary>Usage tracking settings (config section <c>Usage</c>).</summary>
    public class UsageOptions
    {
        /// <summary>Opens <c>/api/stats?key=...</c>; set only in the server's environment. Unset = no stats page.</summary>
        public string? StatsKey { get; set; }

        /// <summary>Events one guest may send per hour (more are dropped).</summary>
        public int MaxEventsPerHour { get; set; } = 120;

        /// <summary>Days the stats page shows.</summary>
        public int StatsDays { get; set; } = 14;
    }

    /// <summary>One thing a player did, sent by the game (<c>POST /api/events</c>). Stored as <c>usage_events</c>.</summary>
    public class UsageEventRecord
    {
        public long Id { get; set; }
        public DateTime AtUtc { get; set; }
        public string GuestId { get; set; } = "";
        public string Kind { get; set; } = "";

        /// <summary>From a device the developer marked (<c>?internal=1</c>): left out of the stats.</summary>
        public bool Internal { get; set; }
    }

    /// <summary>What the game sends.</summary>
    public sealed class UsageEventRequest
    {
        public string? GuestId { get; set; }
        public string? Kind { get; set; }
        public bool Internal { get; set; }
    }

    /// <summary>One day on the stats page (non-internal players only).</summary>
    public sealed record UsageDay(DateOnly Day, int Players, int NewPlayers, int ReturningPlayers, int Visits,
        int PracticeSolves, int LobbyJoins, int DailyOpens, int DailySolves, int TutorialStarts, int TutorialFinishes,
        int TutorialSkips);

    public interface IUsageStore
    {
        Task AddAsync(UsageEventRecord e);

        /// <summary>Non-internal events from <paramref name="fromUtc"/> on.</summary>
        Task<IReadOnlyList<UsageEventRecord>> GetSinceAsync(DateTime fromUtc);

        /// <summary>When each of these guests was first seen (any time, non-internal events).</summary>
        Task<IReadOnlyDictionary<string, DateTime>> GetFirstSeenAsync(IReadOnlyCollection<string> guestIds);
    }

    /// <summary>
    /// Lightweight usage tracking - our own table, no third-party analytics: the game reports a few events (opening
    /// the game, a practice solve, joining a lobby, opening / solving the daily puzzle, starting / finishing / skipping
    /// the first-time tutorial) with the device's guest id, and
    /// <c>/api/stats</c> turns them into players per day, new vs returning. Events from the developer's own devices
    /// (<c>?internal=1</c>) are stored but never counted. Unvalidated input: known kinds and well-formed ids only,
    /// and a per-guest hourly cap.
    /// </summary>
    public class UsageService
    {
        public static readonly IReadOnlyList<string> Kinds = new[]
        {
            "visit", "practice_solve", "lobby_join", "daily_open", "daily_solve", "tutorial_start", "tutorial_finish", "tutorial_skip",
        };
        private static readonly Regex GuestIdPattern = new("^[A-Za-z0-9_-]{16,64}$");

        private readonly IUsageStore _store;
        private readonly UsageOptions _options;
        private readonly TimeProvider _time;
        private readonly TimeZoneInfo _zone;
        private readonly ConcurrentDictionary<string, (DateTime HourStart, int Count)> _recent = new();

        public UsageService(IUsageStore store, IOptions<UsageOptions> options, IOptions<DailyOptions> daily, TimeProvider time)
        {
            _store = store;
            _options = options.Value;
            _time = time;
            _zone = TimeZoneInfo.FindSystemTimeZoneById(daily.Value.TimeZone);
        }

        /// <summary>Records the event; false if it was refused (bad input, or too many from this guest).</summary>
        public async Task<bool> RecordAsync(UsageEventRequest request)
        {
            if (request.GuestId == null || !GuestIdPattern.IsMatch(request.GuestId) || !Kinds.Contains(request.Kind))
                return false;
            DateTime now = _time.GetUtcNow().UtcDateTime;
            if (!UnderCap(request.GuestId, now))
                return false;
            await _store.AddAsync(new UsageEventRecord { AtUtc = now, GuestId = request.GuestId, Kind = request.Kind!, Internal = request.Internal });
            return true;
        }

        private bool UnderCap(string guestId, DateTime now)
        {
            if (_recent.Count > 10_000)
                _recent.Clear(); // a crude bound on memory; the cap is only against floods
            var entry = _recent.AddOrUpdate(guestId, (now, 1), (_, e) => now - e.HourStart >= TimeSpan.FromHours(1) ? (now, 1) : (e.HourStart, e.Count + 1));
            return entry.Count <= _options.MaxEventsPerHour;
        }

        public bool StatsKeyMatches(string? key) =>
            !string.IsNullOrEmpty(_options.StatsKey) && key != null
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(_options.StatsKey));

        /// <summary>The last <see cref="UsageOptions.StatsDays"/> days (in the daily puzzle's time zone), newest first.</summary>
        public async Task<IReadOnlyList<UsageDay>> SummaryAsync()
        {
            DateTime now = _time.GetUtcNow().UtcDateTime;
            DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, _zone));
            DateOnly first = today.AddDays(1 - _options.StatsDays);
            DateTime fromUtc = TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone);

            IReadOnlyList<UsageEventRecord> events = await _store.GetSinceAsync(fromUtc);
            IReadOnlyDictionary<string, DateTime> firstSeen = await _store.GetFirstSeenAsync(events.Select(e => e.GuestId).Distinct().ToList());
            DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, _zone));

            var days = new List<UsageDay>();
            for (DateOnly day = today; day >= first; day = day.AddDays(-1))
            {
                List<UsageEventRecord> on = events.Where(e => DayOf(e.AtUtc) == day).ToList();
                List<string> players = on.Select(e => e.GuestId).Distinct().ToList();
                int newPlayers = players.Count(g => DayOf(firstSeen[g]) == day);
                int Count(string kind) => on.Count(e => e.Kind == kind);
                days.Add(new UsageDay(day, players.Count, newPlayers, players.Count - newPlayers, Count("visit"),
                    Count("practice_solve"), Count("lobby_join"), Count("daily_open"), Count("daily_solve"),
                    Count("tutorial_start"), Count("tutorial_finish"), Count("tutorial_skip")));
            }
            return days;
        }
    }
}
