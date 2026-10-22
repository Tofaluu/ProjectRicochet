using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// The player's own options, saved between sessions (the start of the planned settings page): sound,
    /// the big burst when the target robot reaches the target, the move preview dots and the practice boards'
    /// length.
    /// </summary>
    public static class PlayerSettings
    {
        private const string MutedPrefKey = "Ricochet.Muted";
        private const string BigBurstPrefKey = "Ricochet.BigBurst";
        private const string MovePreviewPrefKey = "Ricochet.MovePreview";
        private const string PracticeExactPrefKey = "Ricochet.PracticeExact";
        private const string PracticeMovesPrefKey = "Ricochet.PracticeMoves";
        private const string PalettePrefKey = "Ricochet.Palette";
        private const string BoardStylePrefKey = "Ricochet.BoardStyle";
        // A typical board's length (the median of random boards).
        public const int DefaultPracticeMoves = 6;

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

        /// <summary>Practice boards of an exact length (<see cref="PracticeMoves"/>); off = plain random boards.</summary>
        public static bool PracticeExact
        {
            get => Get(PracticeExactPrefKey, false);
            set => Set(PracticeExactPrefKey, value);
        }

        /// <summary>
        /// With <see cref="PracticeExact"/>, practice boards' shortest solution is exactly this long ("solve in N"; lobbies use a minimum instead, so
        /// players don't know the best length). Same range as a lobby's min moves.
        /// </summary>
        public static int PracticeMoves
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(PracticeMovesPrefKey, DefaultPracticeMoves),
                MatchSettings.MinMinMoves, MatchSettings.MaxMinMoves);
            set
            {
                PlayerPrefs.SetInt(PracticeMovesPrefKey, Mathf.Clamp(value, MatchSettings.MinMinMoves, MatchSettings.MaxMinMoves));
                PlayerPrefs.Save();
            }
        }

        /// <summary>The chosen palette's id (see <c>Theme.Palette</c>); empty = the default.</summary>
        public static string Palette
        {
            get => PlayerPrefs.GetString(PalettePrefKey, "");
            set
            {
                PlayerPrefs.SetString(PalettePrefKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The chosen board style's id (see <c>Theme.BoardStyle</c>); empty = the default.</summary>
        public static string BoardStyle
        {
            get => PlayerPrefs.GetString(BoardStylePrefKey, "");
            set
            {
                PlayerPrefs.SetString(BoardStylePrefKey, value);
                PlayerPrefs.Save();
            }
        }

        private static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;

        private static void Set(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
