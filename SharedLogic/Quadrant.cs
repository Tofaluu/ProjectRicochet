using System;
using System.Collections.Generic;

namespace SharedLogic
{
    /// <summary>
    /// One 8x8 quarter of the board, described as if it sat in the bottom-left corner:
    /// the board edges run along x = 0 and y = 0 and the centre block is at (7, 7).
    /// <see cref="BoardGenerator"/> rotates it into whichever corner it is dealt to.
    /// </summary>
    public sealed class Quadrant
    {
        public const int Size = 8;

        private readonly List<(GridPos Cell, Direction Side)> _walls = new List<(GridPos, Direction)>();
        private readonly List<Target> _targets = new List<Target>();

        public string Name { get; }
        public IReadOnlyList<(GridPos Cell, Direction Side)> Walls => _walls;
        public IReadOnlyList<Target> Targets => _targets;

        private Quadrant _mirrored;

        private Quadrant(string name)
        {
            Name = name;
        }

        /// <summary>
        /// This piece flipped across its corner-to-centre diagonal ((x, y) -> (y, x)): the two board edges swap,
        /// so it still fits any corner, but it's a mirror image no rotation can make. Same targets and walls,
        /// so it follows the same rules. Mirroring the mirror gives the original layout back.
        /// </summary>
        public Quadrant Mirrored
        {
            get
            {
                if (_mirrored != null)
                    return _mirrored;
                var mirror = new Quadrant(Name + " (mirrored)") { _mirrored = this };
                foreach ((GridPos cell, Direction side) in _walls)
                    mirror._walls.Add((Flip(cell), Flip(side)));
                foreach (Target target in _targets)
                    mirror._targets.Add(new Target(Flip(target.Position), target.Robot));
                return _mirrored = mirror;
            }
        }

        private static GridPos Flip(GridPos cell) => new GridPos(cell.Y, cell.X);

        // Reflecting across the diagonal swaps the axes: up (+y) becomes right (+x), down becomes left.
        private static Direction Flip(Direction side)
        {
            switch (side)
            {
                case Direction.Up: return Direction.Right;
                case Direction.Right: return Direction.Up;
                case Direction.Down: return Direction.Left;
                default: return Direction.Down;
            }
        }

        /// <summary>
        /// The two short walls sticking out of the board edge: one on the east side of
        /// (<paramref name="bottomX"/>, 0) and one on the north side of (0, <paramref name="leftY"/>).
        /// </summary>
        private Quadrant EdgeWalls(int bottomX, int leftY)
        {
            _walls.Add((new GridPos(bottomX, 0), Direction.Right));
            _walls.Add((new GridPos(0, leftY), Direction.Up));
            return this;
        }

        /// <summary>A target in the corner of an L-shaped wall on sides <paramref name="a"/> and <paramref name="b"/>.</summary>
        private Quadrant Target(int x, int y, RobotColor? robot, Direction a, Direction b)
        {
            if (a == b || a == b.Opposite())
                throw new ArgumentException("Target walls must be on two adjacent sides.");

            GridPos cell = new GridPos(x, y);
            _walls.Add((cell, a));
            _walls.Add((cell, b));
            _targets.Add(new Target(cell, robot));
            return this;
        }

        /// <summary>
        /// The four slots, each with interchangeable variants; a board deals one variant per slot. Every
        /// variant has one target per colour (L-shaped walls, never touching another target) and two edge
        /// stubs; only the third slot's variants carry the wildcard target, so every board has exactly one.
        /// These are original layouts, not copies of the board game's.
        /// </summary>
        public static readonly IReadOnlyList<IReadOnlyList<Quadrant>> Slots = new IReadOnlyList<Quadrant>[]
        {
            new[]
            {
                new Quadrant("Alpha")
                    .EdgeWalls(bottomX: 4, leftY: 5)
                    .Target(1, 2, RobotColor.Red, Direction.Up, Direction.Right)
                    .Target(5, 1, RobotColor.Green, Direction.Up, Direction.Left)
                    .Target(6, 5, RobotColor.Blue, Direction.Down, Direction.Left)
                    .Target(3, 6, RobotColor.Yellow, Direction.Down, Direction.Right),
                new Quadrant("Alpha II")
                    .EdgeWalls(bottomX: 3, leftY: 4)
                    .Target(5, 2, RobotColor.Red, Direction.Up, Direction.Left)
                    .Target(2, 5, RobotColor.Green, Direction.Down, Direction.Right)
                    .Target(4, 4, RobotColor.Blue, Direction.Up, Direction.Right)
                    .Target(6, 6, RobotColor.Yellow, Direction.Down, Direction.Left),
                new Quadrant("Alpha III")
                    .EdgeWalls(bottomX: 5, leftY: 6)
                    .Target(2, 1, RobotColor.Red, Direction.Up, Direction.Right)
                    .Target(6, 3, RobotColor.Green, Direction.Down, Direction.Left)
                    .Target(1, 4, RobotColor.Blue, Direction.Up, Direction.Left)
                    .Target(4, 6, RobotColor.Yellow, Direction.Down, Direction.Right),
            },
            new[]
            {
                new Quadrant("Beta")
                    .EdgeWalls(bottomX: 2, leftY: 3)
                    .Target(6, 2, RobotColor.Red, Direction.Down, Direction.Right)
                    .Target(2, 4, RobotColor.Blue, Direction.Up, Direction.Left)
                    .Target(4, 6, RobotColor.Green, Direction.Up, Direction.Right)
                    .Target(1, 1, RobotColor.Yellow, Direction.Down, Direction.Left),
                new Quadrant("Beta II")
                    .EdgeWalls(bottomX: 6, leftY: 2)
                    .Target(3, 3, RobotColor.Red, Direction.Down, Direction.Left)
                    .Target(6, 4, RobotColor.Blue, Direction.Up, Direction.Left)
                    .Target(1, 6, RobotColor.Green, Direction.Down, Direction.Right)
                    .Target(4, 1, RobotColor.Yellow, Direction.Up, Direction.Right),
                new Quadrant("Beta III")
                    .EdgeWalls(bottomX: 1, leftY: 6)
                    .Target(5, 5, RobotColor.Red, Direction.Down, Direction.Right)
                    .Target(2, 2, RobotColor.Blue, Direction.Up, Direction.Left)
                    .Target(6, 1, RobotColor.Green, Direction.Up, Direction.Left)
                    .Target(2, 5, RobotColor.Yellow, Direction.Up, Direction.Right),
            },
            new[]
            {
                new Quadrant("Gamma")
                    .EdgeWalls(bottomX: 5, leftY: 2)
                    .Target(3, 1, RobotColor.Red, Direction.Up, Direction.Left)
                    .Target(5, 3, RobotColor.Blue, Direction.Up, Direction.Right)
                    .Target(1, 5, RobotColor.Green, Direction.Down, Direction.Right)
                    .Target(6, 6, RobotColor.Yellow, Direction.Down, Direction.Left)
                    .Target(2, 3, null, Direction.Down, Direction.Right),
                new Quadrant("Gamma II")
                    .EdgeWalls(bottomX: 2, leftY: 5)
                    .Target(5, 1, RobotColor.Red, Direction.Down, Direction.Right)
                    .Target(1, 3, RobotColor.Blue, Direction.Up, Direction.Right)
                    .Target(4, 4, RobotColor.Green, Direction.Down, Direction.Left)
                    .Target(2, 6, RobotColor.Yellow, Direction.Down, Direction.Left)
                    .Target(6, 3, null, Direction.Up, Direction.Left),
                new Quadrant("Gamma III")
                    .EdgeWalls(bottomX: 4, leftY: 3)
                    .Target(6, 5, RobotColor.Red, Direction.Up, Direction.Right)
                    .Target(3, 2, RobotColor.Blue, Direction.Down, Direction.Left)
                    .Target(6, 1, RobotColor.Green, Direction.Up, Direction.Left)
                    .Target(2, 5, RobotColor.Yellow, Direction.Down, Direction.Right)
                    .Target(4, 4, null, Direction.Up, Direction.Left),
            },
            new[]
            {
                new Quadrant("Delta")
                    .EdgeWalls(bottomX: 3, leftY: 6)
                    .Target(5, 5, RobotColor.Red, Direction.Up, Direction.Left)
                    .Target(1, 3, RobotColor.Blue, Direction.Down, Direction.Right)
                    .Target(6, 1, RobotColor.Green, Direction.Down, Direction.Left)
                    .Target(2, 6, RobotColor.Yellow, Direction.Up, Direction.Right),
                new Quadrant("Delta II")
                    .EdgeWalls(bottomX: 5, leftY: 4)
                    .Target(2, 2, RobotColor.Red, Direction.Down, Direction.Right)
                    .Target(5, 6, RobotColor.Blue, Direction.Down, Direction.Left)
                    .Target(6, 3, RobotColor.Green, Direction.Up, Direction.Right)
                    .Target(2, 5, RobotColor.Yellow, Direction.Up, Direction.Left),
                new Quadrant("Delta III")
                    .EdgeWalls(bottomX: 2, leftY: 2)
                    .Target(4, 2, RobotColor.Red, Direction.Up, Direction.Right)
                    .Target(6, 5, RobotColor.Blue, Direction.Down, Direction.Left)
                    .Target(1, 4, RobotColor.Green, Direction.Down, Direction.Right)
                    .Target(3, 6, RobotColor.Yellow, Direction.Up, Direction.Left),
            },
        };

        /// <summary>The original variant of each slot (one full board's worth).</summary>
        public static readonly IReadOnlyList<Quadrant> All = new[] { Slots[0][0], Slots[1][0], Slots[2][0], Slots[3][0] };
    }
}
