using System;
using System.Collections.Generic;

namespace SharedLogic
{
    /// <summary>Wall flags for the four sides of a single cell.</summary>
    [Flags]
    public enum Walls : byte
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
    }

    /// <summary>
    /// A target cell. <see cref="Robot"/> is the robot that must stop on it,
    /// or null for a wildcard target any robot can claim (the "vortex").
    /// </summary>
    public readonly struct Target
    {
        public readonly GridPos Position;
        public readonly RobotColor? Robot;

        public Target(GridPos position, RobotColor? robot)
        {
            Position = position;
            Robot = robot;
        }

        public bool AcceptsRobot(RobotColor color) => !Robot.HasValue || Robot.Value == color;

        public override string ToString() => $"{(Robot.HasValue ? Robot.Value.ToString() : "Any")} target {Position}";
    }

    /// <summary>
    /// The static board: size, walls and targets. Robot positions are kept
    /// separately in <see cref="RobotPositions"/> because they change every move.
    /// The board edge always acts as a wall and is not stored in the cell flags.
    /// </summary>
    public sealed class GridState
    {
        public const int DefaultSize = 16;

        private readonly Walls[] _cells;
        private readonly List<Target> _targets = new List<Target>();

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<Target> Targets => _targets;

        public GridState(int width = DefaultSize, int height = DefaultSize)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            _cells = new Walls[width * height];
        }

        public bool IsInBounds(GridPos pos) => pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height;

        public Walls GetWalls(GridPos pos) => _cells[IndexOf(pos)];

        /// <summary>
        /// True if a robot standing on <paramref name="pos"/> cannot step one cell
        /// in <paramref name="direction"/> because of a wall or the board edge.
        /// Robots are not considered.
        /// </summary>
        public bool IsBlocked(GridPos pos, Direction direction)
        {
            if ((_cells[IndexOf(pos)] & direction.ToWall()) != 0)
                return true;
            return !IsInBounds(pos.Step(direction));
        }

        /// <summary>
        /// Places a wall on one side of a cell. The matching side of the neighbouring
        /// cell is set too, so a wall always blocks movement from both directions.
        /// </summary>
        public void AddWall(GridPos pos, Direction side)
        {
            _cells[IndexOf(pos)] |= side.ToWall();

            GridPos neighbour = pos.Step(side);
            if (IsInBounds(neighbour))
                _cells[IndexOf(neighbour)] |= side.Opposite().ToWall();
        }

        public void RemoveWall(GridPos pos, Direction side)
        {
            _cells[IndexOf(pos)] &= ~side.ToWall();

            GridPos neighbour = pos.Step(side);
            if (IsInBounds(neighbour))
                _cells[IndexOf(neighbour)] &= ~side.Opposite().ToWall();
        }

        /// <summary>Walls in a rectangle on all four sides so no robot can enter it (e.g. the centre 2x2).</summary>
        public void AddBlockedArea(GridPos min, int width, int height)
        {
            for (int x = min.X; x < min.X + width; x++)
            {
                for (int y = min.Y; y < min.Y + height; y++)
                {
                    GridPos pos = new GridPos(x, y);
                    AddWall(pos, Direction.Up);
                    AddWall(pos, Direction.Right);
                    AddWall(pos, Direction.Down);
                    AddWall(pos, Direction.Left);
                }
            }
        }

        public void AddTarget(Target target)
        {
            IndexOf(target.Position);
            _targets.Add(target);
        }

        private int IndexOf(GridPos pos)
        {
            if (!IsInBounds(pos))
                throw new ArgumentOutOfRangeException(nameof(pos), pos, $"Outside {Width}x{Height} grid.");
            return pos.Y * Width + pos.X;
        }
    }
}
