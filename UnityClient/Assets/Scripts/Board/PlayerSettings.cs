using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// The player's own options, saved between sessions (the start of the planned settings page): sound,
    /// the big burst when the target robot reaches the target, and the move preview dots.
    /// </summary>
    public static class PlayerSettings
    {
        private const string MutedPrefKey = "Ricochet.Muted";
        private const string BigBurstPrefKey = "Ricochet.BigBurst";
        private const string MovePreviewPrefKey = "Ricochet.MovePreview";

        public static bool Muted
        {
            get => Get(MutedPrefKey, false);
            set => Set(MutedPrefKey, value);
        }

        public static bool BigBurst
        {
            get => Get(BigBurstPrefKey, true);
            set => Set(BigBurstPrefKey, value);
        }

        /// <summary>Dots showing where the selected robot would stop in each direction (click one to move there).</summary>
        public static bool MovePreview
        {
            get => Get(MovePreviewPrefKey, true);
            set => Set(MovePreviewPrefKey, value);
        }

        private static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;

        private static void Set(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
