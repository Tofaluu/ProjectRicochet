using System;
using Ricochet.Client.Board;
using UnityEngine;

namespace Ricochet.Client.Theme
{
    /// <summary>
    /// The player's choice of colours around the board (Settings > Palette). A palette changes the surroundings only:
    /// the UI's colours (App.uss defines each one's variables under <see cref="UssClass"/>), the ground behind the board
    /// and the frame around the player's own board (practice and rounds alike). The board itself, the robot colours and
    /// the replay frames (a player's replay, the shortest) stay the same in every palette.
    /// </summary>
    public sealed class Palette
    {
        public string Id { get; }
        public string Name { get; }
        /// <summary>The ground behind everything (the camera's clear colour, matching the UI's --bg).</summary>
        public Color Ground { get; }
        /// <summary>The palette's action colour (its swatch in the picker).</summary>
        public Color Accent { get; }
        /// <summary>The frame (and desktop banner) of the player's own board: practice and rounds.</summary>
        public Color BoardFrame { get; }
        /// <summary>Light grounds: coloured text gets darkened instead of lightened to stay readable.</summary>
        public bool IsLight { get; }
        /// <summary>Buttons get a black press-down shadow under them (drawn by AppUI).</summary>
        public bool PressShadow { get; }
        public string UssClass => "palette--" + Id;

        private Palette(string id, string name, uint ground, uint accent, uint boardFrame, bool isLight = false, bool pressShadow = false)
        {
            Id = id;
            Name = name;
            Ground = Rgb(ground);
            Accent = Rgb(accent);
            BoardFrame = Rgb(boardFrame);
            IsLight = isLight;
            PressShadow = pressShadow;
        }

        public static readonly Palette[] All =
        {
            new Palette("toynight", "Toybox night", 0x1f1914, 0x26914f, 0xecdec1, pressShadow: true),
            new Palette("night", "Night table", 0x1c1713, 0x3b6fd9, 0x6b4423),
            new Palette("indigo", "Indigo night", 0x171a29, 0x6a5be0, 0x786bc7),
            new Palette("table", "Board game table", 0xefe6d3, 0x2f5fc4, 0x8a5a34, isLight: true),
            new Palette("toybox", "Toybox", 0xfff4dc, 0x1f7a44, 0x2b2117, isLight: true),
            new Palette("teal", "Deep teal", 0x0f2427, 0xf2a33a, 0x0d6b63),
            new Palette("chalk", "Chalkboard", 0x22302a, 0x3a6fd8, 0x7a4f2a),
        };

        public static Palette Default => All[0];

        private static Palette _current;

        /// <summary>The player's palette (saved in <see cref="PlayerSettings.Palette"/>; unknown ids fall back to the default).</summary>
        public static Palette Current => _current ?? (_current = Find(PlayerSettings.Palette) ?? Default);

        /// <summary>Raised after <see cref="Select"/> changes the palette, so views re-colour themselves.</summary>
        public static event Action Changed;

        public static void Select(Palette palette)
        {
            if (palette == null || palette == Current)
                return;
            _current = palette;
            PlayerSettings.Palette = palette.Id;
            Changed?.Invoke();
        }

        private static Palette Find(string id) => Array.Find(All, p => p.Id == id);

        private static Color Rgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);
    }
}
