using System;
using Ricochet.Client.Network;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.App
{
    /// <summary>
    /// Optional accounts (the profile popover): signing in with Google hands this device the account's guest id, so the
    /// daily puzzle, streak and past plays follow the player to every device they sign in on (the server merges this
    /// device's progress in). Signing out starts a fresh guest; deleting removes the account and its results for good.
    /// Who's signed in is remembered on the device (<see cref="Email"/>) and checked with the server once a visit.
    /// </summary>
    public class AccountController
    {
        private const string EmailPrefKey = "Ricochet.AccountEmail";

        private readonly GameController _game;
        private readonly DailyController _daily;
        private bool _checked;

        public AccountController(GameController game, DailyController daily)
        {
            _game = game;
            _daily = daily;
        }

        /// <summary>Who the player is signed in as, or null while playing as a guest.</summary>
        public string Email
        {
            get
            {
                string email = PlayerPrefs.GetString(EmailPrefKey, null);
                return string.IsNullOrEmpty(email) ? null : email;
            }
            private set
            {
                if (value == null)
                    PlayerPrefs.DeleteKey(EmailPrefKey);
                else
                    PlayerPrefs.SetString(EmailPrefKey, value);
                PlayerPrefs.Save();
            }
        }

        public bool SignedIn => Email != null;
        public bool Busy { get; private set; }

        /// <summary>The outcome of the last action ("Signed in", an error), or null.</summary>
        public string Message { get; private set; }

        public void ClearMessage() => Message = null;

        /// <summary>A sign-in failed: <see cref="Message"/> says why (the profile closes while Google's dialog is up).</summary>
        public event Action SignInFailed;

        /// <summary>The player asked to delete the account and hasn't confirmed or cancelled yet.</summary>
        public bool ConfirmingDelete { get; set; }

        private NetworkManager Network => _game.Network;

        /// <summary>Once a visit (when the profile opens): still signed in? The account may have been deleted elsewhere.</summary>
        public async void Check()
        {
            if (_checked || !SignedIn || Busy)
                return;
            _checked = true;
            try
            {
                Network.ServerUrl = _game.ServerUrl;
                AccountInfo account = await Network.GetAccountAsync(GuestIdentity.Id);
                if (Busy || !SignedIn)
                    return;
                if (account == null)
                {
                    BecomeGuest();
                    Message = "Your account was deleted, so you're playing as a guest.";
                }
                else if (!string.IsNullOrEmpty(account.Email))
                    Email = account.Email;
            }
            catch (Exception e)
            {
                _checked = false; // offline: try again next time
                Debug.LogWarning("[Account] Check failed: " + NetworkManager.DescribeError(e));
            }
        }

        public async void SignIn()
        {
            if (Busy)
                return;
            Busy = true;
            Message = null;
            try
            {
                string token = await GoogleSignIn.RequestTokenAsync("Sign in to Ricochet",
                    "Keep your streak and daily results on every device you sign in on.");
                if (token == null)
                    return; // closed the dialog
                Network.ServerUrl = _game.ServerUrl;
                AccountInfo account = await Network.SignInWithGoogleAsync(GuestIdentity.Id, token);
                Email = string.IsNullOrEmpty(account.Email) ? "your Google account" : account.Email;
                _checked = true;
                if (account.GuestId != GuestIdentity.Id)
                    _daily.ChangePlayer(account.GuestId);
                Message = account.Merged ? "Signed in. This device's results were added to your account." : "Signed in.";
            }
            catch (Exception e)
            {
                Message = NetworkManager.DescribeError(e);
                Busy = false;
                SignInFailed?.Invoke();
            }
            finally
            {
                Busy = false;
            }
        }

        public void SignOut()
        {
            if (Busy || !SignedIn)
                return;
            BecomeGuest();
            Message = "Signed out. Sign in again any time to pick up where you left off.";
        }

        public async void DeleteAccount()
        {
            if (Busy || !SignedIn)
                return;
            ConfirmingDelete = false;
            Busy = true;
            Message = null;
            try
            {
                Network.ServerUrl = _game.ServerUrl;
                await Network.DeleteAccountAsync(GuestIdentity.Id);
                BecomeGuest();
                Message = "Your account and its results were deleted.";
            }
            catch (Exception e)
            {
                Message = NetworkManager.DescribeError(e);
            }
            finally
            {
                Busy = false;
            }
        }

        // A fresh guest on this device: the account's id stays the account's.
        private void BecomeGuest()
        {
            Email = null;
            ConfirmingDelete = false;
            _daily.ChangePlayer(GuestIdentity.NewId());
        }
    }
}
