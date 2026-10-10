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

        private Quadrant(string name)
        {
            Name = name;
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
        /// The quadrant set. Each has one target per colour; one also has the wildcard
        /// target. These are original layouts, not copies of the board game's.
        /// </summary>
        public static readonly IReadOnlyList<Quadrant> All = new[]
        {
            new Quadrant("Alpha")
                .EdgeWalls(bottomX: 4, leftY: 5)
                .Target(1, 2, RobotColor.Red, Direction.Up, Direction.Right)
                .Target(5, 1, RobotColor.Green, Direction.Up, Direction.Left)
                .Target(6, 5, RobotColor.Blue, Direction.Down, Direction.Left)
                .Target(3, 6, RobotColor.Yellow, Direction.Down, Direction.Right),

            new Quadrant("Beta")
                .EdgeWalls(bottomX: 2, leftY: 3)
                .Target(6, 2, RobotColor.Red, Direction.Down, Direction.Right)
                .Target(2, 4, RobotColor.Blue, Direction.Up, Direction.Left)
                .Target(4, 6, RobotColor.Green, Direction.Up, Direction.Right)
                .Target(1, 1, RobotColor.Yellow, Direction.Down, Direction.Left),

            new Quadrant("Gamma")
                .EdgeWalls(bottomX: 5, leftY: 2)
                .Target(3, 1, RobotColor.Red, Direction.Up, Direction.Left)
                .Target(5, 3, RobotColor.Blue, Direction.Up, Direction.Right)
                .Target(1, 5, RobotColor.Green, Direction.Down, Direction.Right)
                .Target(6, 6, RobotColor.Yellow, Direction.Down, Direction.Left)
                .Target(2, 3, null, Direction.Down, Direction.Right),

            new Quadrant("Delta")
                .EdgeWalls(bottomX: 3, leftY: 6)
                .Target(5, 5, RobotColor.Red, Direction.Up, Direction.Left)
                .Target(1, 3, RobotColor.Blue, Direction.Down, Direction.Right)
                .Target(6, 1, RobotColor.Green, Direction.Down, Direction.Left)
                .Target(2, 6, RobotColor.Yellow, Direction.Up, Direction.Right),
        };
    }
}
