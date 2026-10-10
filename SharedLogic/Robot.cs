using System;

namespace SharedLogic
{
    public enum RobotColor : byte
    {
        Red,
        Blue,
        Green,
        Yellow,
    }

    public readonly struct Robot : IEquatable<Robot>
    {
        public const int Count = 4;

        public readonly RobotColor Color;
        public readonly GridPos Position;

        public Robot(RobotColor color, GridPos position)
        {
            Color = color;
            Position = position;
        }

        public bool Equals(Robot other) => Color == other.Color && Position == other.Position;
        public override bool Equals(object obj) => obj is Robot other && Equals(other);
        public override int GetHashCode() => ((int)Color * 397) ^ Position.GetHashCode();
        public override string ToString() => $"{Color} {Position}";
    }

    /// <summary>
    /// Where all four robots are. Immutable and allocation-free, so moves can be
    /// applied speculatively (scratchpad, undo, solution checking, solvers).
    /// </summary>
    public readonly struct RobotPositions : IEquatable<RobotPositions>
    {
        public readonly GridPos Red;
        public readonly GridPos Blue;
        public readonly GridPos Green;
        public readonly GridPos Yellow;

        public RobotPositions(GridPos red, GridPos blue, GridPos green, GridPos yellow)
        {
            Red = red;
            Blue = blue;
            Green = green;
            Yellow = yellow;
        }

        public GridPos this[RobotColor color]
        {
            get
            {
                switch (color)
                {
                    case RobotColor.Red: return Red;
                    case RobotColor.Blue: return Blue;
                    case RobotColor.Green: return Green;
                    case RobotColor.Yellow: return Yellow;
                    default: throw new ArgumentOutOfRangeException(nameof(color), color, null);
                }
            }
        }

        public RobotPositions With(RobotColor color, GridPos position)
        {
            switch (color)
            {
                case RobotColor.Red: return new RobotPositions(position, Blue, Green, Yellow);
                case RobotColor.Blue: return new RobotPositions(Red, position, Green, Yellow);
                case RobotColor.Green: return new RobotPositions(Red, Blue, position, Yellow);
                case RobotColor.Yellow: return new RobotPositions(Red, Blue, Green, position);
                default: throw new ArgumentOutOfRangeException(nameof(color), color, null);
            }
        }

        public bool IsOccupied(GridPos position)
        {
            return Red == position || Blue == position || Green == position || Yellow == position;
        }

        public Robot GetRobot(RobotColor color) => new Robot(color, this[color]);

        public bool Equals(RobotPositions other)
        {
            return Red == other.Red && Blue == other.Blue && Green == other.Green && Yellow == other.Yellow;
        }

        public override bool Equals(object obj) => obj is RobotPositions other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Red.GetHashCode();
                hash = hash * 31 + Blue.GetHashCode();
                hash = hash * 31 + Green.GetHashCode();
                hash = hash * 31 + Yellow.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(RobotPositions a, RobotPositions b) => a.Equals(b);
        public static bool operator !=(RobotPositions a, RobotPositions b) => !a.Equals(b);
    }
}
