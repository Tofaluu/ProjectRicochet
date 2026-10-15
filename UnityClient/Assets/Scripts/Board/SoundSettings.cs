using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>The player's sound choice, saved between sessions (first piece of the planned settings page).</summary>
    public static class SoundSettings
    {
        private const string MutedPrefKey = "Ricochet.Muted";

        public static bool Muted
        {
            get => PlayerPrefs.GetInt(MutedPrefKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(MutedPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void ToggleMute() => Muted = !Muted;
    }
}
