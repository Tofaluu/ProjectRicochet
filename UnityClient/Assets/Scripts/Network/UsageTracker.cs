using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// Reports a few usage events (opening the game, a practice solve, joining a lobby, opening / solving the daily) to
    /// the server's <c>/api/events</c>, with the device's guest id, so the developer can see whether people come back.
    /// Fire and forget: failures are ignored. A device marked internal (<c>?internal=1</c> once in the browser,
    /// <c>-internal</c> on the desktop, always in the Editor; <c>?internal=0</c> unmarks it) still reports, flagged,
    /// and the stats leave it out. Added by <c>AppUI</c> at start-up.
    /// </summary>
    public class UsageTracker : MonoBehaviour
    {
        private const string InternalPrefKey = "Ricochet.Internal";

        private static UsageTracker _instance;
        private Func<string> _hubUrl;

        public static bool IsInternal => Application.isEditor || PlayerPrefs.GetInt(InternalPrefKey, 0) == 1;

        /// <summary>Marks (or unmarks) this device as the developer's own.</summary>
        public static void SetInternal(bool isInternal)
        {
            PlayerPrefs.SetInt(InternalPrefKey, isInternal ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <param name="hubUrl">The game server's hub URL (events go to the same server).</param>
        public void Init(Func<string> hubUrl)
        {
            _instance = this;
            _hubUrl = hubUrl;
        }

        /// <summary>
        /// One of the server's known kinds: visit, practice_solve, lobby_join, daily_open, daily_solve, tutorial_start,
        /// tutorial_finish, tutorial_skip.
        /// </summary>
        public static void Track(string kind)
        {
            if (_instance != null)
                _instance.Send(kind);
        }

        private void Send(string kind)
        {
            string hub = _hubUrl();
            int path = hub.LastIndexOf("/gamehub", StringComparison.OrdinalIgnoreCase);
            string url = (path >= 0 ? hub.Substring(0, path) : hub.TrimEnd('/')) + "/api/events";
            string json = "{\"guestId\":\"" + GuestIdentity.Id + "\",\"kind\":\"" + kind + "\",\"internal\":" + (IsInternal ? "true" : "false") + "}";

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SendWebRequest().completed += _ => request.Dispose();
        }
    }
}
