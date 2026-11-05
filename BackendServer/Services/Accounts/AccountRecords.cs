namespace BackendServer.Services
{
    /// <summary>Account settings (config section <c>Accounts</c>).</summary>
    public class AccountOptions
    {
        /// <summary>
        /// The Google OAuth client id (public: it's in the web page too). Google sign-ins must be issued for it. Unset = Google
        /// sign-in is off.
        /// </summary>
        public string? GoogleClientId { get; set; }
    }

    /// <summary>
    /// A player account (the <c>accounts</c> table). Its results live under <see cref="HomeGuestId"/>, a guest id like any
    /// other: every device signed in to the account uses it, so the daily puzzle's tables don't know accounts exist.
    /// </summary>
    public class AccountRecord
    {
        public long Id { get; set; }
        public string HomeGuestId { get; set; } = "";

        /// <summary>From the latest sign-in, if the provider gave a verified one (shown to the player as who they're signed in as).</summary>
        public string? Email { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime LastSignInUtc { get; set; }
    }

    /// <summary>One way into an account (the <c>account_logins</c> table): e.g. provider "google" and Google's user id.</summary>
    public class AccountLoginRecord
    {
        public string Provider { get; set; } = "";

        /// <summary>The provider's stable id for the person (Google's "sub"), never their email (which can change).</summary>
        public string Subject { get; set; } = "";

        public long AccountId { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>A person a sign-in provider vouched for.</summary>
    public sealed record ExternalIdentity(string Subject, string? Email);
}
