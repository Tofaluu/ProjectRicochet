using System;
using Ricochet.Client.Board;
using UnityEngine;

namespace Ricochet.Client.Theme
{
    /// <summary>The family of pieces a board style draws with (walls, targets, the centre block and the robots).</summary>
    public enum BoardKind
    {
        /// <summary>Wood tiles, raised walnut walls, robots as painted wooden pawns.</summary>
        Wood,
        /// <summary>Printed card, thick ink lines and hard shadows, like the Toybox UI.</summary>
        Ink,
        /// <summary>Graph paper, pen walls, robots as stickers.</summary>
        Paper,
    }

    /// <summary>
    /// The player's choice of board (Settings > Board), independent of the <see cref="Palette"/> around it. A style
    /// changes how the board and its pieces are drawn (<see cref="BoardView"/>); the rules, the robot colours and the
    /// meaning of the replay frames stay the same in every style.
    /// </summary>
    public sealed class BoardStyle
    {
        public string Id { get; }
        public string Name { get; }
        public BoardKind Kind { get; }
        /// <summary>Tile colours, alternating like a checkerboard (the same twice = one tone).</summary>
        public Color TileA { get; }
        public Color TileB { get; }
        /// <summary>The thin lines between tiles.</summary>
        public Color Grout { get; }
        /// <summary>The frame around the player's own board; null = the palette's.</summary>
        public Color? Frame { get; }

        private BoardStyle(string id, string name, BoardKind kind, uint tileA, uint tileB, uint grout, uint? frame = null)
        {
            Id = id;
            Name = name;
            Kind = kind;
            TileA = Rgb(tileA);
            TileB = Rgb(tileB);
            Grout = Rgb(grout);
            Frame = frame.HasValue ? Rgb(frame.Value) : (Color?)null;
        }

        public static readonly BoardStyle[] All =
        {
            new BoardStyle("wood", "Wood", BoardKind.Wood, 0xe6c48e, 0xe6c48e, 0xc39661, frame: 0x6e4524),
            new BoardStyle("wood2", "Wood, two tones", BoardKind.Wood, 0xe9c893, 0xd6ad75, 0xb98a54, frame: 0x6e4524),
            new BoardStyle("ink", "Ink & toy", BoardKind.Ink, 0xf8efdc, 0xf8efdc, 0xe4d6bb),
            new BoardStyle("paper", "Graph paper", BoardKind.Paper, 0xfbf8f0, 0xfbf8f0, 0xc3d4ea),
        };

        public static BoardStyle Default => All[0];

        /// <summary>The walls' colour (and the swatch's dot in the picker).</summary>
        public Color Wall => Kind == BoardKind.Wood ? Rgb(0x5a361b) : Kind == BoardKind.Ink ? Ink : Rgb(0x24305a);

        /// <summary>The outline colour of the Ink style's pieces.</summary>
        public static Color Ink => Rgb(0x1d1610);

        private static BoardStyle _current;

        /// <summary>The player's board (saved in <see cref="PlayerSettings.BoardStyle"/>; unknown ids fall back to the default).</summary>
        public static BoardStyle Current => _current ?? (_current = Find(PlayerSettings.BoardStyle) ?? Default);

        /// <summary>Raised after <see cref="Select"/> changes the style, so the board redraws itself.</summary>
        public static event Action Changed;

        public static void Select(BoardStyle style)
        {
            if (style == null || style == Current)
                return;
            _current = style;
            PlayerSettings.BoardStyle = style.Id;
            Changed?.Invoke();
        }

        private static BoardStyle Find(string id) => Array.Find(All, s => s.Id == id);

        public static Color Rgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);
    }
}
