namespace BackendServer.Services
{
    /// <summary>
    /// Keeps today's and tomorrow's daily puzzles generated ahead of time (checked every few minutes), so the first
    /// player of the day doesn't wait for a hard Sunday board to be found.
    /// </summary>
    public sealed class DailyPrepareService : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);

        private readonly DailyService _daily;
        private readonly TimeProvider _time;
        private readonly ILogger<DailyPrepareService> _log;
        private readonly DailyOptions _options;

        public DailyPrepareService(DailyService daily, TimeProvider time, ILogger<DailyPrepareService> log,
            Microsoft.Extensions.Options.IOptions<DailyOptions> options)
        {
            _options = options.Value;
            _daily = daily;
            _time = time;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stopping)
        {
            try
            {
                await _daily.MergeAliasesAsync();
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Merging aliased guests failed");
            }
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    int today = _daily.TodaysNumber;
                    if (today >= 1)
                        await _daily.EnsurePuzzleAsync(today);
                    if (today + 1 >= 1)
                        await _daily.EnsurePuzzleAsync(today + 1);
                    for (int number = today - 1; number >= Math.Max(1, today - _options.BackfillDays); number--)
                        await _daily.EnsurePuzzleAsync(number);
                }
                catch (Exception e)
                {
                    // E.g. the database is briefly unreachable: try again next time.
                    _log.LogWarning(e, "Preparing the daily puzzles failed");
                }
                await Task.Delay(CheckEvery, _time, stopping);
            }
        }
    }
}
