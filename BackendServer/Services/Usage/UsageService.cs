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

        /// <summary>A guest counts as online this long after their last ping (the game pings about once a minute).</summary>
        public int OnlineSeconds { get; set; } = 120;
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

    /// <summary>A game still open on someone's screen (<c>POST /api/online</c>, about once a minute while the tab is visible).</summary>
    public sealed class OnlinePingRequest
    {
        public string? GuestId { get; set; }
        public bool Internal { get; set; }
    }

    /// <summary>The most players online at once on one day (Toronto time). Stored as <c>usage_online_peaks</c>.</summary>
    public class OnlinePeakRecord
    {
        public DateOnly Day { get; set; }
        public int Peak { get; set; }

        /// <summary>When the peak was first reached.</summary>
        public DateTime AtUtc { get; set; }
    }

    /// <summary>One day on the stats page (non-internal players only). <c>PeakOnline</c> is 0 for days before it was tracked.</summary>
    public sealed record UsageDay(DateOnly Day, int Players, int NewPlayers, int ReturningPlayers, int Visits,
        int PracticeSolves, int LobbyJoins, int DailyOpens, int DailySolves, int TutorialStarts, int TutorialFinishes,
        int TutorialSkips, int PeakOnline);

    /// <summary>The stats page's data: players online right now and the last days.</summary>
    public sealed record UsageSummary(int OnlineNow, IReadOnlyList<UsageDay> Days);

    public interface IUsageStore
    {
        Task AddAsync(UsageEventRecord e);

        /// <summary>Non-internal events from <paramref name="fromUtc"/> on.</summary>
        Task<IReadOnlyList<UsageEventRecord>> GetSinceAsync(DateTime fromUtc);

        /// <summary>When each of these guests was first seen (any time, non-internal events).</summary>
        Task<IReadOnlyDictionary<string, DateTime>> GetFirstSeenAsync(IReadOnlyCollection<string> guestIds);

        /// <summary>Raises the day's online peak to <paramref name="peak"/> (a lower one leaves it as it is).</summary>
        Task RaisePeakAsync(DateOnly day, int peak, DateTime atUtc);

        /// <summary>The online peaks of the days from <paramref name="first"/> on.</summary>
        Task<IReadOnlyDictionary<DateOnly, int>> GetPeaksAsync(DateOnly first);
    }

    /// <summary>
    /// Lightweight usage tracking - our own table, no third-party analytics: the game reports a few events (opening
    /// the game, a practice solve, joining a lobby, opening / solving the daily puzzle, starting / finishing / skipping
    /// the first-time tutorial) with the device's guest id, and
    /// <c>/api/stats</c> turns them into players per day, new vs returning. Events from the developer's own devices
    /// (<c>?internal=1</c>) are stored but never counted. Unvalidated input: known kinds and well-formed ids only,
    /// and a per-guest hourly cap.
    /// "Online now": the game also pings while it's open and visible; the last ping per guest lives in memory only (online =
    /// pinged in the last <see cref="UsageOptions.OnlineSeconds"/>), and each day's peak is stored whenever it rises.
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
        private readonly ConcurrentDictionary<string, DateTime> _lastPing = new();
        private readonly SemaphoreSlim _peakGate = new(1, 1);
        private DateOnly _peakDay;
        private int _peak = -1; // _peakDay's stored peak; -1 = not read yet

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

        /// <summary>
        /// Notes that this guest's game is open; false if refused (bad id). Internal devices are never counted. Pings
        /// touch the database only when they raise the day's peak.
        /// </summary>
        public async Task<bool> PingAsync(OnlinePingRequest request)
        {
            if (request.GuestId == null || !GuestIdPattern.IsMatch(request.GuestId))
                return false;
            if (request.Internal)
                return true;
            DateTime now = _time.GetUtcNow().UtcDateTime;
            _lastPing[request.GuestId] = now;
            int online = OnlineNow(now);

            DateOnly today = DayOf(now);
            await _peakGate.WaitAsync();
            try
            {
                if (today != _peakDay || _peak < 0)
                {
                    _peak = (await _store.GetPeaksAsync(today)).GetValueOrDefault(today);
                    _peakDay = today;
                }
                if (online > _peak)
                {
                    _peak = online;
                    await _store.RaisePeakAsync(today, online, now);
                }
            }
            finally
            {
                _peakGate.Release();
            }
            return true;
        }

        private int OnlineNow(DateTime now)
        {
            DateTime since = now - TimeSpan.FromSeconds(_options.OnlineSeconds);
            int online = 0;
            foreach (var (guest, at) in _lastPing)
            {
                if (at >= since)
                    online++;
                else
                    _lastPing.TryRemove(new KeyValuePair<string, DateTime>(guest, at)); // only if it hasn't pinged again since
            }
            return online;
        }

        private DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, _zone));

        public bool StatsKeyMatches(string? key) =>
            !string.IsNullOrEmpty(_options.StatsKey) && key != null
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(_options.StatsKey));

        /// <summary>The last <see cref="UsageOptions.StatsDays"/> days (in the daily puzzle's time zone), newest first.</summary>
        public async Task<UsageSummary> SummaryAsync()
        {
            DateTime now = _time.GetUtcNow().UtcDateTime;
            DateOnly today = DayOf(now);
            DateOnly first = today.AddDays(1 - _options.StatsDays);
            DateTime fromUtc = TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone);

            IReadOnlyList<UsageEventRecord> events = await _store.GetSinceAsync(fromUtc);
            IReadOnlyDictionary<string, DateTime> firstSeen = await _store.GetFirstSeenAsync(events.Select(e => e.GuestId).Distinct().ToList());
            IReadOnlyDictionary<DateOnly, int> peaks = await _store.GetPeaksAsync(first);

            var days = new List<UsageDay>();
            for (DateOnly day = today; day >= first; day = day.AddDays(-1))
            {
                List<UsageEventRecord> on = events.Where(e => DayOf(e.AtUtc) == day).ToList();
                List<string> players = on.Select(e => e.GuestId).Distinct().ToList();
                int newPlayers = players.Count(g => DayOf(firstSeen[g]) == day);
                int Count(string kind) => on.Count(e => e.Kind == kind);
                days.Add(new UsageDay(day, players.Count, newPlayers, players.Count - newPlayers, Count("visit"),
                    Count("practice_solve"), Count("lobby_join"), Count("daily_open"), Count("daily_solve"),
                    Count("tutorial_start"), Count("tutorial_finish"), Count("tutorial_skip"), peaks.GetValueOrDefault(day)));
            }
            return new UsageSummary(OnlineNow(now), days);
        }
    }
}
