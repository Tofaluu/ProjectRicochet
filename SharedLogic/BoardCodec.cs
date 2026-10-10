using System;

namespace SharedLogic
{
    /// <summary>
    /// Compact binary form of a <see cref="RoundSetup"/> for sending over the network.
    /// It stores the walls themselves rather than which quadrants were used, so the
    /// client never needs the generator and hand-made boards work too.
    /// </summary>
    /// <remarks>
    /// Layout (version 1):
    /// <code>
    /// version, width, height
    /// walls: 2 bits per cell in row-major order (bit 0 = North, bit 1 = East);
    ///        South and West come from the neighbouring cell, the board edge is implicit
    /// target count, then per target: x, y, robot (0xFF = any robot)
    /// robots: x, y for Red, Blue, Green, Yellow
    /// active target index
    /// </code>
    /// A 16x16 board with 17 targets is 128 bytes.
    /// </remarks>
    public static class BoardCodec
    {
        public const byte FormatVersion = 1;
        private const byte AnyRobot = 0xFF;

        public static byte[] Encode(RoundSetup round)
        {
            GridState grid = round.Grid;
            if (grid.Width > 255 || grid.Height > 255 || grid.Targets.Count > 255)
                throw new ArgumentException("Board too large for this format.", nameof(round));

            int cellCount = grid.Width * grid.Height;
            int wallBytes = (cellCount * 2 + 7) / 8;
            var data = new byte[3 + wallBytes + 1 + grid.Targets.Count * 3 + Robot.Count * 2 + 1];
            int i = 0;

            data[i++] = FormatVersion;
            data[i++] = (byte)grid.Width;
            data[i++] = (byte)grid.Height;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    Walls walls = grid.GetWalls(new GridPos(x, y));
                    int bits = ((walls & Walls.North) != 0 ? 1 : 0) | ((walls & Walls.East) != 0 ? 2 : 0);
                    int bit = (y * grid.Width + x) * 2;
                    data[i + bit / 8] |= (byte)(bits << (bit % 8));
                }
            }
            i += wallBytes;

            data[i++] = (byte)grid.Targets.Count;
            foreach (Target target in grid.Targets)
            {
                data[i++] = (byte)target.Position.X;
                data[i++] = (byte)target.Position.Y;
                data[i++] = target.Robot.HasValue ? (byte)target.Robot.Value : AnyRobot;
            }

            for (int r = 0; r < Robot.Count; r++)
            {
                GridPos pos = round.Robots[(RobotColor)r];
                data[i++] = (byte)pos.X;
                data[i++] = (byte)pos.Y;
            }

            data[i++] = (byte)round.TargetIndex;
            return data;
        }

        /// <exception cref="FormatException">The data is truncated, has an unknown version, or describes an invalid board.</exception>
        public static RoundSetup Decode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var reader = new Reader(data);
            byte version = reader.Next();
            if (version != FormatVersion)
                throw new FormatException($"Unsupported board format version {version}.");

            int width = reader.Next();
            int height = reader.Next();
            if (width == 0 || height == 0)
                throw new FormatException("Board has no cells.");

            var grid = new GridState(width, height);
            int wallStart = reader.Position;
            reader.Skip((width * height * 2 + 7) / 8);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int bit = (y * width + x) * 2;
                    int bits = (data[wallStart + bit / 8] >> (bit % 8)) & 3;
                    if ((bits & 1) != 0) grid.AddWall(new GridPos(x, y), Direction.Up);
                    if ((bits & 2) != 0) grid.AddWall(new GridPos(x, y), Direction.Right);
                }
            }

            int targetCount = reader.Next();
            for (int t = 0; t < targetCount; t++)
            {
                GridPos pos = reader.NextPos(grid);
                byte robot = reader.Next();
                if (robot != AnyRobot && robot >= Robot.Count)
                    throw new FormatException($"Unknown robot {robot}.");
                grid.AddTarget(new Target(pos, robot == AnyRobot ? (RobotColor?)null : (RobotColor)robot));
            }

            var robots = new RobotPositions(reader.NextPos(grid), reader.NextPos(grid), reader.NextPos(grid), reader.NextPos(grid));

            int targetIndex = reader.Next();
            if (targetIndex >= targetCount)
                throw new FormatException("Active target index out of range.");
            if (reader.Position != data.Length)
                throw new FormatException("Trailing data after board.");

            return new RoundSetup(grid, robots, targetIndex);
        }

        private struct Reader
        {
            private readonly byte[] _data;
            public int Position { get; private set; }

            public Reader(byte[] data)
            {
                _data = data;
                Position = 0;
            }

            public byte Next()
            {
                if (Position >= _data.Length)
                    throw new FormatException("Board data is truncated.");
                return _data[Position++];
            }

            public void Skip(int count)
            {
                if (Position + count > _data.Length)
                    throw new FormatException("Board data is truncated.");
                Position += count;
            }

            public GridPos NextPos(GridState grid)
            {
                var pos = new GridPos(Next(), Next());
                if (!grid.IsInBounds(pos))
                    throw new FormatException($"Position {pos} is outside the board.");
                return pos;
            }
        }
    }
}
