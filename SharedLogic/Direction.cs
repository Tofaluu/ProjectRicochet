using System;

namespace SharedLogic
{
    /// <summary>
    /// A movement direction on the grid. Y grows upward (matching Unity world space),
    /// so Up is +Y and Down is -Y.
    /// </summary>
    public enum Direction : byte
    {
        Up,
        Right,
        Down,
        Left,
    }

    public static class DirectionExtensions
    {
        public static int DeltaX(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Right: return 1;
                case Direction.Left: return -1;
                default: return 0;
            }
        }

        public static int DeltaY(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return 1;
                case Direction.Down: return -1;
                default: return 0;
            }
        }

        public static Direction Opposite(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return Direction.Down;
                case Direction.Right: return Direction.Left;
                case Direction.Down: return Direction.Up;
                case Direction.Left: return Direction.Right;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        /// <summary>The wall flag on the side of a cell facing this direction.</summary>
        public static Walls ToWall(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return Walls.North;
                case Direction.Right: return Walls.East;
                case Direction.Down: return Walls.South;
                case Direction.Left: return Walls.West;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }
    }
}
