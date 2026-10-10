using System;
using System.Collections.Generic;

namespace SharedLogic
{
    public readonly struct Move : IEquatable<Move>
    {
        public readonly RobotColor Robot;
        public readonly Direction Direction;

        public Move(RobotColor robot, Direction direction)
        {
            Robot = robot;
            Direction = direction;
        }

        public bool Equals(Move other) => Robot == other.Robot && Direction == other.Direction;
        public override bool Equals(object obj) => obj is Move other && Equals(other);
        public override int GetHashCode() => ((int)Robot << 8) | (int)Direction;
        public override string ToString() => $"{Robot} {Direction}";
    }

    public enum SolutionResult
    {
        Valid,
        /// <summary>No moves were submitted.</summary>
        Empty,
        /// <summary>A move left its robot where it was. Players can't count those toward a solution.</summary>
        NoOpMove,
        /// <summary>Every move was legal but the target robot didn't finish on the target.</summary>
        TargetNotReached,
    }

    /// <summary>
    /// Pure movement rules. Used by the client scratchpad to preview moves and by
    /// the server to validate submitted solutions, so both always agree.
    /// </summary>
    public static class GameRules
    {
        /// <summary>
        /// Slides <paramref name="robot"/> in <paramref name="direction"/> until the next
        /// cell is behind a wall, off the board, or occupied by another robot.
        /// Returns the robot's current position if it can't move at all.
        /// </summary>
        public static GridPos ComputeStop(GridState grid, RobotPositions robots, RobotColor robot, Direction direction)
        {
            GridPos pos = robots[robot];
            while (!grid.IsBlocked(pos, direction))
            {
                GridPos next = pos.Step(direction);
                if (robots.IsOccupied(next))
                    break;
                pos = next;
            }
            return pos;
        }

        /// <summary>
        /// Applies a move. Returns false (and leaves <paramref name="result"/> equal to
        /// <paramref name="robots"/>) if the robot can't move in that direction.
        /// </summary>
        public static bool TryMove(GridState grid, RobotPositions robots, Move move, out RobotPositions result)
        {
            GridPos start = robots[move.Robot];
            GridPos stop = ComputeStop(grid, robots, move.Robot, move.Direction);
            result = robots.With(move.Robot, stop);
            return stop != start;
        }

        /// <summary>
        /// Checks a submitted solution: every move must move its robot, and after the
        /// last move a robot accepted by <paramref name="target"/> must be standing on it.
        /// Passing over the target mid-slide doesn't count; the robot has to stop there.
        /// </summary>
        public static SolutionResult ValidateSolution(
            GridState grid, RobotPositions start, Target target, IReadOnlyList<Move> moves)
        {
            if (moves == null || moves.Count == 0)
                return SolutionResult.Empty;

            RobotPositions robots = start;
            for (int i = 0; i < moves.Count; i++)
            {
                if (!TryMove(grid, robots, moves[i], out robots))
                    return SolutionResult.NoOpMove;
            }

            Move last = moves[moves.Count - 1];
            bool reached = target.AcceptsRobot(last.Robot) && robots[last.Robot] == target.Position;
            return reached ? SolutionResult.Valid : SolutionResult.TargetNotReached;
        }
    }
}
