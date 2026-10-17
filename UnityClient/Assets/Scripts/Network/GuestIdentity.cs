using System;
using UnityEngine;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// The player's daily puzzle identity without an account: a random secret id made on first use and kept on
    /// this device (PlayerPrefs; in browsers, the site's storage). It carries their results and streak, so it's
    /// never shown to anyone; clearing the browser's data starts a new one.
    /// </summary>
    public static class GuestIdentity
    {
        private const string PrefKey = "Ricochet.GuestId";

        public static string Id
        {
            get
            {
                string id = PlayerPrefs.GetString(PrefKey, "");
                if (id.Length >= 16)
                    return id;
                id = Guid.NewGuid().ToString("N"); // 32 hex digits
                PlayerPrefs.SetString(PrefKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }
    }
}
