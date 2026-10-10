using System;
using System.Collections.Generic;

namespace SharedLogic
{
    /// <summary>Everything a player needs to start a round: the board, where the robots are, and which target is active.</summary>
    public sealed class RoundSetup
    {
        public GridState Grid { get; }
        public RobotPositions Robots { get; }
        public int TargetIndex { get; }
        public Target Target => Grid.Targets[TargetIndex];

        public RoundSetup(GridState grid, RobotPositions robots, int targetIndex)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (targetIndex < 0 || targetIndex >= grid.Targets.Count)
                throw new ArgumentOutOfRangeException(nameof(targetIndex));

            Grid = grid;
            Robots = robots;
            TargetIndex = targetIndex;
        }
    }

    /// <summary>Builds the standard 16x16 board from four <see cref="Quadrant"/>s and sets up rounds on it.</summary>
    public static class BoardGenerator
    {
        public const int BoardSize = Quadrant.Size * 2;

        /// <summary>
        /// Assembles a board from exactly four quadrants, dealt clockwise from the
        /// bottom-left corner: bottom-left, top-left, top-right, bottom-right.
        /// </summary>
        public static GridState Build(IReadOnlyList<Quadrant> quadrants)
        {
            if (quadrants == null || quadrants.Count != 4)
                throw new ArgumentException("A board needs exactly four quadrants.", nameof(quadrants));

            var grid = new GridState(BoardSize, BoardSize);
            for (int corner = 0; corner < 4; corner++)
            {
                Quadrant quadrant = quadrants[corner];
                foreach ((GridPos cell, Direction side) in quadrant.Walls)
                    grid.AddWall(Rotate(cell, corner), Rotate(side, corner));
                foreach (Target target in quadrant.Targets)
                    grid.AddTarget(new Target(Rotate(target.Position, corner), target.Robot));
            }

            grid.AddBlockedArea(new GridPos(Quadrant.Size - 1, Quadrant.Size - 1), 2, 2);
            return grid;
        }

        /// <summary>A board with <see cref="Quadrant.All"/> dealt to the corners in random order.</summary>
        public static GridState GenerateGrid(Random random)
        {
            var quadrants = new List<Quadrant>(Quadrant.All);
            for (int i = quadrants.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Quadrant swap = quadrants[i];
                quadrants[i] = quadrants[j];
                quadrants[j] = swap;
            }
            return Build(quadrants);
        }

        /// <summary>
        /// Puts the four robots on distinct random cells that aren't targets and aren't
        /// walled in on all sides (the centre block).
        /// </summary>
        public static RobotPositions PlaceRobots(GridState grid, Random random)
        {
            var taken = new HashSet<GridPos>();
            foreach (Target target in grid.Targets)
                taken.Add(target.Position);

            var placed = new GridPos[Robot.Count];
            for (int i = 0; i < placed.Length; i++)
            {
                GridPos pos;
                do
                {
                    pos = new GridPos(random.Next(grid.Width), random.Next(grid.Height));
                } while (taken.Contains(pos) || grid.IsEnclosed(pos));

                taken.Add(pos);
                placed[i] = pos;
            }
            return new RobotPositions(placed[0], placed[1], placed[2], placed[3]);
        }

        /// <summary>A fresh board, robots and a random active target.</summary>
        public static RoundSetup GenerateRound(Random random)
        {
            GridState grid = GenerateGrid(random);
            RobotPositions robots = PlaceRobots(grid, random);
            return new RoundSetup(grid, robots, random.Next(grid.Targets.Count));
        }

        // A quarter turn clockwise about the board centre maps bottom-left -> top-left -> top-right -> bottom-right.
        private static GridPos Rotate(GridPos pos, int quarterTurns)
        {
            for (int i = 0; i < quarterTurns; i++)
                pos = new GridPos(pos.Y, BoardSize - 1 - pos.X);
            return pos;
        }

        // Direction is declared clockwise (Up, Right, Down, Left), so a clockwise turn is +1.
        private static Direction Rotate(Direction direction, int quarterTurns)
        {
            return (Direction)(((int)direction + quarterTurns) % 4);
        }
    }
}
