using System;
using System.Threading.Tasks;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace Ricochet.Client.Network
{
    /// <summary>
    /// Asks the player to sign in with Google and returns Google's ID token for the server to check
    /// (<c>HubMethods.SignInWithGoogle</c>). Browsers only for now (<c>Plugins/WebGL/GoogleSignIn.jslib</c>: Google's
    /// own button in a dialog over the page); the desktop builds and the Editor would need Google's app sign-in flow.
    /// </summary>
    public static class GoogleSignIn
    {
        /// <summary>The OAuth client id (public: Google puts it in every sign-in; the server's <c>Accounts:GoogleClientId</c>).</summary>
        public const string ClientId = "256153572328-397ogco5pfa3karmkfp4b5kvh5q9d9kd.apps.googleusercontent.com";

        public static bool Supported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private delegate void ResultCallback(int kind, string value);

        [DllImport("__Internal")]
        private static extern void Ricochet_GoogleSignIn(string clientId, string title, string body, ResultCallback callback);

        private static TaskCompletionSource<string> _pending;

        [MonoPInvokeCallback(typeof(ResultCallback))]
        private static void OnResult(int kind, string value)
        {
            TaskCompletionSource<string> pending = _pending;
            _pending = null;
            if (pending == null)
                return;
            if (kind < 0)
                pending.TrySetException(new Exception(value));
            else
                pending.TrySetResult(kind > 0 ? value : null);
        }
#endif

        /// <summary>Google's ID token, or null if the player closed the dialog. Throws if Google's sign-in couldn't load.</summary>
        public static Task<string> RequestTokenAsync(string title, string body)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _pending?.TrySetResult(null);
            var pending = new TaskCompletionSource<string>();
            _pending = pending;
            Ricochet_GoogleSignIn(ClientId, title, body, OnResult);
            return pending.Task;
#else
            return Task.FromException<string>(new NotSupportedException("Sign-in works in the browser version for now."));
#endif
        }
    }
}
