using System;
using System.Collections.Generic;

namespace SharedLogic
{
    public sealed class SolverResult
    {
        /// <summary>A shortest solution, or null if none was found within the limits.</summary>
        public Move[] Moves { get; }

        /// <summary>True if the search stopped because it hit the depth or node limit, not because no solution exists.</summary>
        public bool HitLimit { get; }

        /// <summary>Positions examined; a rough measure of how hard the board was.</summary>
        public long NodesSearched { get; }

        public bool Solved => Moves != null;

        public SolverResult(Move[] moves, bool hitLimit, long nodesSearched)
        {
            Moves = moves;
            HitLimit = hitLimit;
            NodesSearched = nodesSearched;
        }
    }

    /// <summary>
    /// Finds a shortest solution (fewest moves) that <see cref="GameRules.ValidateSolution"/> accepts.
    /// </summary>
    /// <remarks>
    /// Iterative-deepening depth-first search with two prunings that keep it fast:
    /// <list type="bullet">
    /// <item>A lower bound per cell: how many straight-line moves the target robot needs to reach the
    /// target if it could stop anywhere (as if other robots were always in the right place). A branch is
    /// cut when moves used + that bound exceed the current depth limit.</item>
    /// <item>A table of positions already searched with at least as many moves left. The robots that
    /// don't need to reach the target are interchangeable, so positions differing only by swapping them
    /// share an entry.</item>
    /// </list>
    /// Boards are limited to 256 cells so a whole position packs into one 32-bit key.
    /// </remarks>
    public sealed class Solver
    {
        public const int DefaultMaxMoves = 12;
        public const long DefaultMaxNodes = 3_000_000;

        private const int Unreachable = 255;

        private readonly int _width;
        private readonly int _cellCount;
        // Bit d set = a robot on that cell can't step in Direction d (wall or board edge).
        private readonly byte[] _blocked;
        private readonly int[] _step;

        private Solver(GridState grid)
        {
            _width = grid.Width;
            _cellCount = grid.Width * grid.Height;
            if (_cellCount > 256)
                throw new NotSupportedException("The solver supports boards of up to 256 cells.");

            _blocked = new byte[_cellCount];
            for (int i = 0; i < _cellCount; i++)
            {
                var pos = new GridPos(i % _width, i / _width);
                for (int d = 0; d < 4; d++)
                {
                    if (grid.IsBlocked(pos, (Direction)d))
                        _blocked[i] |= (byte)(1 << d);
                }
            }
            _step = new[] { _width, 1, -_width, -1 }; // Up, Right, Down, Left as cell-index offsets
        }

        public static SolverResult Solve(GridState grid, RobotPositions start, Target target,
            int maxMoves = DefaultMaxMoves, long maxNodes = DefaultMaxNodes)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            return new Solver(grid).Run(start, target, maxMoves, maxNodes);
        }

        public static SolverResult Solve(RoundSetup round, int maxMoves = DefaultMaxMoves, long maxNodes = DefaultMaxNodes)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            return Solve(round.Grid, round.Robots, round.Target, maxMoves, maxNodes);
        }

        // ---- Search state (one search per Solver instance) ----

        private readonly int[] _pos = new int[Robot.Count];
        private int _targetCell;
        private int _targetRobot; // -1 = any robot
        private byte[] _lowerBound;
        private Dictionary<uint, byte> _seen;
        private Move[] _path;
        private int _foundLength;
        private long _nodes;
        private long _maxNodes;
        private bool _aborted;

        private SolverResult Run(RobotPositions start, Target target, int maxMoves, long maxNodes)
        {
            for (int r = 0; r < Robot.Count; r++)
                _pos[r] = Index(start[(RobotColor)r]);
            _targetCell = Index(target.Position);
            _targetRobot = target.Robot.HasValue ? (int)target.Robot.Value : -1;
            _lowerBound = ComputeLowerBounds(_targetCell);
            _maxNodes = maxNodes;
            _path = new Move[Math.Max(maxMoves, 1)];
            _seen = new Dictionary<uint, byte>();

            // Every solution has at least one move, even if a robot already sits on the target.
            int first = Math.Max(1, Heuristic());
            if (first == Unreachable)
                return new SolverResult(null, false, 0);

            for (int limit = first; limit <= maxMoves; limit++)
            {
                _seen.Clear();
                if (Search(0, limit))
                {
                    var moves = new Move[_foundLength];
                    Array.Copy(_path, moves, _foundLength);
                    return new SolverResult(moves, false, _nodes);
                }
                if (_aborted)
                    return new SolverResult(null, true, _nodes);
            }
            return new SolverResult(null, true, _nodes);
        }

        /// <summary>Tries every move from the current position; true once a solution of length <paramref name="limit"/> is in <see cref="_path"/>.</summary>
        private bool Search(int depth, int limit)
        {
            if (++_nodes > _maxNodes)
            {
                _aborted = true;
                return false;
            }

            int remaining = limit - depth - 1; // moves left after the one we're about to make
            for (int i = 0; i < Robot.Count; i++)
            {
                // Try the target robot first; it's usually the one that finishes.
                int r = _targetRobot >= 0 ? (i == 0 ? _targetRobot : (i <= _targetRobot ? i - 1 : i)) : i;
                int from = _pos[r];
                for (int d = 0; d < 4; d++)
                {
                    int to = Slide(r, d);
                    if (to == from)
                        continue;

                    _pos[r] = to;
                    _path[depth] = new Move((RobotColor)r, (Direction)d);

                    // The last move must bring an accepted robot onto the target.
                    if (to == _targetCell && (_targetRobot < 0 || _targetRobot == r))
                    {
                        _foundLength = depth + 1;
                        _pos[r] = from;
                        return true;
                    }

                    if (remaining > 0 && Math.Max(1, Heuristic()) <= remaining)
                    {
                        uint key = Key();
                        if (!_seen.TryGetValue(key, out byte seenRemaining) || seenRemaining < remaining)
                        {
                            _seen[key] = (byte)remaining;
                            if (Search(depth + 1, limit))
                            {
                                _pos[r] = from;
                                return true;
                            }
                            if (_aborted)
                            {
                                _pos[r] = from;
                                return false;
                            }
                        }
                    }
                    _pos[r] = from;
                }
            }
            return false;
        }

        /// <summary>Where robot <paramref name="r"/> stops sliding in direction <paramref name="d"/>.</summary>
        private int Slide(int r, int d)
        {
            int cell = _pos[r];
            int step = _step[d];
            int mask = 1 << d;
            while ((_blocked[cell] & mask) == 0)
            {
                int next = cell + step;
                if (next == _pos[0] || next == _pos[1] || next == _pos[2] || next == _pos[3])
                    break;
                cell = next;
            }
            return cell;
        }

        private int Heuristic()
        {
            if (_targetRobot >= 0)
                return _lowerBound[_pos[_targetRobot]];

            int best = Unreachable;
            for (int r = 0; r < Robot.Count; r++)
                best = Math.Min(best, _lowerBound[_pos[r]]);
            return best;
        }

        /// <summary>
        /// Packs the position into 32 bits. Robots that can't finish are interchangeable, so they're
        /// sorted; for a wildcard target all four are.
        /// </summary>
        private uint Key()
        {
            if (_targetRobot < 0)
                return Pack(_pos[0], _pos[1], _pos[2], _pos[3]);

            int a = -1, b = -1, c = -1;
            for (int r = 0; r < Robot.Count; r++)
            {
                if (r == _targetRobot) continue;
                if (a < 0) a = _pos[r]; else if (b < 0) b = _pos[r]; else c = _pos[r];
            }
            Sort3(ref a, ref b, ref c);
            return ((uint)_pos[_targetRobot] << 24) | ((uint)a << 16) | ((uint)b << 8) | (uint)c;
        }

        private static uint Pack(int a, int b, int c, int d)
        {
            // Sort four values with a small fixed network.
            if (a > b) Swap(ref a, ref b);
            if (c > d) Swap(ref c, ref d);
            if (a > c) Swap(ref a, ref c);
            if (b > d) Swap(ref b, ref d);
            if (b > c) Swap(ref b, ref c);
            return ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | (uint)d;
        }

        private static void Sort3(ref int a, ref int b, ref int c)
        {
            if (a > b) Swap(ref a, ref b);
            if (b > c) Swap(ref b, ref c);
            if (a > b) Swap(ref a, ref b);
        }

        private static void Swap(ref int x, ref int y)
        {
            int t = x;
            x = y;
            y = t;
        }

        /// <summary>
        /// Breadth-first search outward from the target over "rook" moves that may stop on any cell before
        /// a wall. Line of sight is symmetric, so this gives each cell's minimum moves to the target.
        /// </summary>
        private byte[] ComputeLowerBounds(int target)
        {
            var bound = new byte[_cellCount];
            for (int i = 0; i < bound.Length; i++)
                bound[i] = Unreachable;

            var queue = new Queue<int>();
            bound[target] = 0;
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                byte next = (byte)(bound[cell] + 1);
                for (int d = 0; d < 4; d++)
                {
                    int mask = 1 << d;
                    int c = cell;
                    while ((_blocked[c] & mask) == 0)
                    {
                        c += _step[d];
                        if (bound[c] == Unreachable)
                        {
                            bound[c] = next;
                            queue.Enqueue(c);
                        }
                    }
                }
            }
            return bound;
        }

        private int Index(GridPos pos) => pos.Y * _width + pos.X;
    }
}
