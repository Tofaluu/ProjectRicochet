using System.Text.RegularExpressions;
using Google.Apis.Auth;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SharedLogic;

namespace BackendServer.Services
{
    /// <summary>Checks a sign-in token from Google (faked in tests).</summary>
    public interface IGoogleTokenValidator
    {
        /// <summary>Who the token is for, or null if it isn't a valid Google ID token for our client id.</summary>
        Task<ExternalIdentity?> ValidateAsync(string idToken);
    }

    /// <summary>
    /// Verifies Google ID tokens locally (Google's signing keys, fetched and cached by Google.Apis.Auth): signature,
    /// issuer, expiry and that it was issued for <see cref="AccountOptions.GoogleClientId"/>.
    /// </summary>
    public sealed class GoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly AccountOptions _options;

        public GoogleTokenValidator(IOptions<AccountOptions> options)
        {
            _options = options.Value;
        }

        public async Task<ExternalIdentity?> ValidateAsync(string idToken)
        {
            if (string.IsNullOrWhiteSpace(_options.GoogleClientId) || string.IsNullOrWhiteSpace(idToken))
                return null;
            try
            {
                GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { _options.GoogleClientId } });
                return new ExternalIdentity(payload.Subject, payload.EmailVerified ? payload.Email : null);
            }
            catch (InvalidJwtException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Optional accounts on top of guest ids (user, 2026-11-04): an account is a "home" guest id plus the sign-ins that
    /// lead to it. Signing in on a device hands that device the home id, so every device of the account plays as the same
    /// guest and the daily puzzle's code doesn't change. The first sign-in claims the device's own guest id as the home;
    /// signing in elsewhere merges that device's guest progress into the home (<see cref="IDailyStore.MergeGuestAsync"/>:
    /// on days both played, the better result stays). Signing out is the client's: it starts a fresh guest id.
    /// </summary>
    public class AccountService
    {
        public const string Google = "google";
        private static readonly Regex GuestIdPattern = new("^[A-Za-z0-9_-]{16,64}$");

        private readonly IAccountStore _accounts;
        private readonly IDailyStore _daily;
        private readonly IUsageStore _usage;
        private readonly IGoogleTokenValidator _google;
        private readonly TimeProvider _time;
        private readonly ILogger<AccountService> _log;
        private readonly SemaphoreSlim _gate = new(1, 1); // one sign-in at a time: no duplicate accounts from a double tap

        public AccountService(IAccountStore accounts, IDailyStore daily, IUsageStore usage, IGoogleTokenValidator google,
            TimeProvider time, ILogger<AccountService> log)
        {
            _accounts = accounts;
            _daily = daily;
            _usage = usage;
            _google = google;
            _time = time;
            _log = log;
        }

        public async Task<AccountInfo> SignInWithGoogleAsync(string guestId, string idToken)
        {
            CheckGuestId(guestId);
            ExternalIdentity identity = await _google.ValidateAsync(idToken)
                ?? throw new HubException("Google sign-in didn't work. Please try again.");
            return await SignInAsync(guestId, Google, identity);
        }

        private async Task<AccountInfo> SignInAsync(string guestId, string provider, ExternalIdentity identity)
        {
            DateTime now = _time.GetUtcNow().UtcDateTime;
            await _gate.WaitAsync();
            try
            {
                AccountRecord? account = await _accounts.FindByLoginAsync(provider, identity.Subject);
                // A device already playing as another account's home (signed in to someone else) keeps that progress there.
                bool deviceIsAnAccount = await _accounts.FindByHomeAsync(guestId) is { } owner && owner.Id != account?.Id;
                bool merged = false;
                if (account == null)
                {
                    account = await _accounts.CreateAsync(deviceIsAnAccount ? NewGuestId() : guestId, provider, identity.Subject, identity.Email, now);
                    _log.LogInformation("New account {Id} ({Provider})", account.Id, provider);
                    await RecordAsync(UsageService.AccountNew, account.HomeGuestId, guestId, now);
                }
                else
                {
                    if (account.HomeGuestId != guestId && !deviceIsAnAccount)
                        merged = await _daily.MergeGuestAsync(guestId, account.HomeGuestId) > 0;
                    await _accounts.TouchAsync(account.Id, identity.Email, now);
                    await RecordAsync(UsageService.SignIn, account.HomeGuestId, guestId, now);
                }
                return new AccountInfo { GuestId = account.HomeGuestId, Email = identity.Email ?? account.Email ?? "", Merged = merged };
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>The account this guest id is the home of, or null (a plain guest, or the account was deleted).</summary>
        public async Task<AccountInfo?> GetAccountAsync(string guestId)
        {
            CheckGuestId(guestId);
            AccountRecord? account = await _accounts.FindByHomeAsync(guestId);
            return account == null ? null : new AccountInfo { GuestId = account.HomeGuestId, Email = account.Email ?? "" };
        }

        /// <summary>
        /// Deletes the account whose home this guest id is and everything stored under it: daily results, past plays, the
        /// name, usage events. Other devices still holding the id become fresh guests.
        /// </summary>
        public async Task DeleteAccountAsync(string guestId)
        {
            CheckGuestId(guestId);
            await _gate.WaitAsync();
            try
            {
                AccountRecord account = await _accounts.FindByHomeAsync(guestId)
                    ?? throw new HubException("There's no account to delete. Sign in first.");
                await _daily.DeleteGuestAsync(account.HomeGuestId);
                await _usage.DeleteGuestAsync(account.HomeGuestId);
                await _accounts.DeleteAsync(account.Id);
                _log.LogInformation("Deleted account {Id}", account.Id);
            }
            finally
            {
                _gate.Release();
            }
        }

        // For the stats page; a failure here never fails the sign-in.
        private async Task RecordAsync(string kind, string homeGuestId, string deviceGuestId, DateTime now)
        {
            try
            {
                await UsageService.RecordAccountAsync(_usage, kind, homeGuestId, deviceGuestId, now);
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Couldn't record a {Kind} event", kind);
            }
        }

        private static string NewGuestId() => Guid.NewGuid().ToString("N");

        private static void CheckGuestId(string guestId)
        {
            if (guestId == null || !GuestIdPattern.IsMatch(guestId))
                throw new HubException("That player id isn't valid.");
        }
    }
}
