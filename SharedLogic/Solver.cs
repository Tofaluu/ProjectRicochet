using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SharedLogic
{
    public sealed class SolverResult
    {
        /// <summary>A shortest solution, or null if none was found within the limits.</summary>
        public Move[] Moves { get; }

        /// <summary>True if the search gave up (move, node or time limit), so a solution may still exist.</summary>
        public bool HitLimit { get; }

        /// <summary>Positions examined; a rough measure of how hard the board was.</summary>
        public long NodesSearched { get; }

        public TimeSpan Elapsed { get; }

        public bool Solved => Moves != null;

        public SolverResult(Move[] moves, bool hitLimit, long nodesSearched, TimeSpan elapsed)
        {
            Moves = moves;
            HitLimit = hitLimit;
            NodesSearched = nodesSearched;
            Elapsed = elapsed;
        }
    }

    /// <summary>
    /// Finds a shortest solution (fewest moves) that <see cref="GameRules.ValidateSolution"/> accepts.
    /// </summary>
    /// <remarks>
    /// Iterative deepening: look for any 1-move solution, then 2, then 3..., so the first one found is
    /// the shortest. Each depth is a depth-first search over every robot and direction, cut down by:
    /// <list type="bullet">
    /// <item>A lower bound per cell: how many straight-line moves the target robot needs to reach the
    /// target if it could stop anywhere. A branch is dropped when moves used + bound exceed the limit.</item>
    /// <item>Only robots the target accepts are tried for the final move of a branch.</item>
    /// <item>A table of positions already searched with at least as many moves left, kept across depths.
    /// Robots that can't finish are interchangeable, so positions differing only by swapping them share
    /// an entry. A whole position packs into one 32-bit key (boards are at most 256 cells).</item>
    /// </list>
    /// Optionally only some robots may move (the rest stand still as blockers): <see cref="FewestRobots"/> uses it.
    /// </remarks>
    public sealed class Solver
    {
        public const int DefaultMaxMoves = 30;
        public const long DefaultMaxNodes = 500_000_000;
        /// <summary>Caps the position table: at most ~80 MB (it only grows as needed).</summary>
        public const int DefaultMaxTableEntries = 1 << 23;
        /// <summary>Every robot may move: one bit per <see cref="RobotColor"/> (1 &lt;&lt; (int)color).</summary>
        public const int AllRobots = (1 << Robot.Count) - 1;

        private const int Unreachable = 255;

        private readonly int _width;
        private readonly int _cellCount;
        // _stop[d * cellCount + cell]: where a robot slides from 'cell' in direction d if no robot is in the way.
        private readonly byte[] _stop;
        private readonly int[] _step;

        private Solver(GridState grid)
        {
            _width = grid.Width;
            _cellCount = grid.Width * grid.Height;
            if (_cellCount > 256)
                throw new NotSupportedException("The solver supports boards of up to 256 cells.");

            _step = new[] { _width, 1, -_width, -1 }; // Up, Right, Down, Left as cell-index offsets
            _stop = new byte[4 * _cellCount];
            for (int d = 0; d < 4; d++)
            {
                for (int i = 0; i < _cellCount; i++)
                {
                    var pos = new GridPos(i % _width, i / _width);
                    while (!grid.IsBlocked(pos, (Direction)d))
                        pos = pos.Step((Direction)d);
                    _stop[d * _cellCount + i] = (byte)Index(pos);
                }
            }
        }

        /// <param name="maxMoves">Give up on solutions longer than this.</param>
        /// <param name="maxNodes">Give up after examining this many positions.</param>
        /// <param name="timeLimit">Give up after this long (null = no time limit).</param>
        /// <param name="movableRobots">The robots that may move (bits, see <see cref="AllRobots"/>); the rest stand still.</param>
        public static SolverResult Solve(GridState grid, RobotPositions start, Target target,
            int maxMoves = DefaultMaxMoves, long maxNodes = DefaultMaxNodes, TimeSpan? timeLimit = null,
            int movableRobots = AllRobots)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            return new Solver(grid).Run(start, target, maxMoves, maxNodes, timeLimit, movableRobots & AllRobots);
        }

        public static SolverResult Solve(RoundSetup round,
            int maxMoves = DefaultMaxMoves, long maxNodes = DefaultMaxNodes, TimeSpan? timeLimit = null,
            int movableRobots = AllRobots)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            return Solve(round.Grid, round.Robots, round.Target, maxMoves, maxNodes, timeLimit, movableRobots);
        }

        /// <summary>
        /// The fewest robots that can solve <paramref name="round"/> in <paramref name="length"/> moves (its shortest
        /// solution's length): 1 if the target robot can do it alone, 4 if every shortest solution moves all four. A
        /// group the solver gives up on (<paramref name="timeLimit"/>) counts as able to, so the answer never overstates.
        /// </summary>
        public static int FewestRobots(RoundSetup round, int length, TimeSpan? timeLimit = null)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            for (int count = 1; count < Robot.Count; count++)
            {
                for (int group = 1; group < AllRobots; group++)
                {
                    if (BitCount(group) != count || !CanFinish(round.Target, group))
                        continue;
                    // HitLimit also means "none within length"; only a search cut short (nodes, time) leaves it open.
                    var solver = new Solver(round.Grid);
                    SolverResult result = solver.Run(round.Robots, round.Target, length, DefaultMaxNodes, timeLimit, group);
                    if (result.Solved || solver._aborted)
                        return count;
                }
            }
            return Robot.Count;
        }

        private static int BitCount(int bits)
        {
            int count = 0;
            for (; bits != 0; bits &= bits - 1)
                count++;
            return count;
        }

        private static bool CanFinish(Target target, int robots) =>
            target.Robot.HasValue ? (robots & (1 << (int)target.Robot.Value)) != 0 : robots != 0;

        // ---- Search state (one search per Solver instance) ----

        private readonly int[] _pos = new int[Robot.Count];
        private readonly int[] _robotOrder = new int[Robot.Count];
        private int _finishers; // how many robots at the start of _robotOrder can finish the puzzle
        private int _movable;   // how many robots at the start of _robotOrder may move (the finishers first)
        private bool _restricted; // some robots stand still
        private int _targetCell;
        private int _targetRobot; // -1 = any robot
        private byte[] _lowerBound;
        private PositionTable _seen;
        private Move[] _path;
        private int _foundLength;
        private long _nodes;
        private long _maxNodes;
        private Stopwatch _clock;
        private long _deadlineTicks;
        private bool _aborted;

        private SolverResult Run(RobotPositions start, Target target, int maxMoves, long maxNodes, TimeSpan? timeLimit, int movableRobots)
        {
            _clock = Stopwatch.StartNew();
            _deadlineTicks = timeLimit.HasValue ? (long)(timeLimit.Value.TotalSeconds * Stopwatch.Frequency) : long.MaxValue;
            for (int r = 0; r < Robot.Count; r++)
                _pos[r] = Index(start[(RobotColor)r]);
            _targetCell = Index(target.Position);
            _targetRobot = target.Robot.HasValue ? (int)target.Robot.Value : -1;

            // The robot(s) that can finish go first: they're tried first, and they're the only ones
            // worth trying for the last move of a branch. Then the other robots that may move, then those standing still.
            _restricted = movableRobots != AllRobots;
            int n = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int r = 0; r < Robot.Count; r++)
                {
                    bool movable = (movableRobots & (1 << r)) != 0;
                    bool finishes = _targetRobot < 0 || _targetRobot == r;
                    if (pass == 0 ? movable && finishes : pass == 1 ? movable && !finishes : !movable)
                        _robotOrder[n++] = r;
                }
                if (pass == 0)
                    _finishers = n;
                else if (pass == 1)
                    _movable = n;
            }
            if (_finishers == 0)
                return new SolverResult(null, false, 0, _clock.Elapsed); // no robot that may move can finish

            _lowerBound = ComputeLowerBounds(_targetCell);
            _maxNodes = maxNodes;
            _path = new Move[Math.Max(maxMoves, 1)];
            _seen = new PositionTable(DefaultMaxTableEntries);

            // Every solution has at least one move, even if a robot already sits on the target.
            int first = Math.Max(1, Heuristic());
            if (first >= Unreachable)
                return Result(null, false);

            // The position table stays valid across depths: an entry means "searched with this many
            // moves left and found nothing", which is still true when the overall limit grows.
            for (int limit = first; limit <= maxMoves; limit++)
            {
                if (Search(0, limit))
                {
                    var moves = new Move[_foundLength];
                    Array.Copy(_path, moves, _foundLength);
                    return Result(moves, false);
                }
                if (_aborted)
                    break;
            }
            return Result(null, true);
        }

        private SolverResult Result(Move[] moves, bool hitLimit) =>
            new SolverResult(moves, hitLimit, _nodes, _clock.Elapsed);

        /// <summary>Tries every move from the current position; true once a solution is in <see cref="_path"/>.</summary>
        private bool Search(int depth, int limit)
        {
            if (++_nodes > _maxNodes || ((_nodes & 0xFFF) == 0 && _clock.ElapsedTicks > _deadlineTicks))
            {
                _aborted = true;
                return false;
            }

            int remaining = limit - depth - 1; // moves left after the one we're about to make
            // On the last move only a robot that can finish is worth moving.
            int robotsToTry = remaining == 0 ? _finishers : _movable;
            for (int i = 0; i < robotsToTry; i++)
            {
                int r = _robotOrder[i];
                int from = _pos[r];
                for (int d = 0; d < 4; d++)
                {
                    int to = Slide(r, d);
                    if (to == from)
                        continue;

                    _path[depth] = new Move((RobotColor)r, (Direction)d);

                    // The last move must bring an accepted robot onto the target.
                    if (to == _targetCell && (_targetRobot < 0 || _targetRobot == r))
                    {
                        _foundLength = depth + 1;
                        return true;
                    }
                    if (remaining == 0)
                        continue;

                    _pos[r] = to;
                    if (Math.Max(1, Heuristic()) <= remaining && _seen.TryClaim(Key(), (byte)remaining))
                    {
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
                    _pos[r] = from;
                }
            }
            return false;
        }

        /// <summary>Where robot <paramref name="r"/> stops sliding in direction <paramref name="d"/>.</summary>
        private int Slide(int r, int d)
        {
            int from = _pos[r];
            int stop = _stop[d * _cellCount + from];
            if (stop == from)
                return from;

            // Stop short of the nearest robot between here and the wall.
            int step = _step[d];
            for (int q = 0; q < Robot.Count; q++)
            {
                if (q == r)
                    continue;
                int other = _pos[q];
                bool between = step > 0
                    ? other > from && other <= stop
                    : other < from && other >= stop;
                // Up/Down move a whole row per step; the robot must also be in the same column.
                if (between && (step == 1 || step == -1 || (other - from) % step == 0))
                {
                    int blocked = other - step;
                    if (step > 0 ? blocked < stop : blocked > stop)
                        stop = blocked;
                }
            }
            return stop;
        }

        private int Heuristic()
        {
            if (_targetRobot >= 0)
                return _lowerBound[_pos[_targetRobot]];

            int best = Unreachable;
            for (int i = 0; i < _finishers; i++)
                best = Math.Min(best, _lowerBound[_pos[_robotOrder[i]]]);
            return best;
        }

        /// <summary>
        /// Packs the position into 32 bits. Robots that can't finish are interchangeable, so they're
        /// sorted; for a wildcard target all four are.
        /// </summary>
        private uint Key()
        {
            // With robots standing still, swapping one for a moving one isn't the same position: no sorting.
            if (_restricted)
                return ((uint)_pos[0] << 24) | ((uint)_pos[1] << 16) | ((uint)_pos[2] << 8) | (uint)_pos[3];
            if (_targetRobot < 0)
                return Pack(_pos[0], _pos[1], _pos[2], _pos[3]);

            int a = _pos[_robotOrder[1]], b = _pos[_robotOrder[2]], c = _pos[_robotOrder[3]];
            if (a > b) Swap(ref a, ref b);
            if (b > c) Swap(ref b, ref c);
            if (a > b) Swap(ref a, ref b);
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
                    int stop = _stop[d * _cellCount + cell];
                    for (int c = cell; c != stop;)
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

        /// <summary>
        /// Open-addressing hash table from packed position to "moves left when last searched". Grows up to
        /// a cap; once full it stops remembering new positions, which only costs speed, never correctness.
        /// </summary>
        private sealed class PositionTable
        {
            private const uint Empty = 0xFFFFFFFF; // never a real key: four robots can't share cell 255

            private uint[] _keys;
            private byte[] _values;
            private int _count;
            private readonly int _maxEntries;

            public PositionTable(int maxEntries)
            {
                _maxEntries = maxEntries;
                Allocate(1 << 16);
            }

            /// <summary>
            /// Records that <paramref name="key"/> is being searched with <paramref name="remaining"/> moves
            /// left. False if it was already searched with at least that many, so it can be skipped.
            /// </summary>
            public bool TryClaim(uint key, byte remaining)
            {
                int mask = _keys.Length - 1;
                int slot = (int)(Mix(key) & (uint)mask);
                while (true)
                {
                    uint existing = _keys[slot];
                    if (existing == key)
                    {
                        if (_values[slot] >= remaining)
                            return false;
                        _values[slot] = remaining;
                        return true;
                    }
                    if (existing == Empty)
                    {
                        if (_count >= _maxEntries)
                            return true; // full: search it without remembering
                        _keys[slot] = key;
                        _values[slot] = remaining;
                        if (++_count * 2 > _keys.Length && _keys.Length < _maxEntries * 2)
                            Grow();
                        return true;
                    }
                    slot = (slot + 1) & mask;
                }
            }

            private void Allocate(int size)
            {
                _keys = new uint[size];
                _values = new byte[size];
                for (int i = 0; i < size; i++)
                    _keys[i] = Empty;
            }

            private void Grow()
            {
                uint[] oldKeys = _keys;
                byte[] oldValues = _values;
                Allocate(oldKeys.Length * 2);
                int mask = _keys.Length - 1;
                for (int i = 0; i < oldKeys.Length; i++)
                {
                    if (oldKeys[i] == Empty)
                        continue;
                    int slot = (int)(Mix(oldKeys[i]) & (uint)mask);
                    while (_keys[slot] != Empty)
                        slot = (slot + 1) & mask;
                    _keys[slot] = oldKeys[i];
                    _values[slot] = oldValues[i];
                }
            }

            private static uint Mix(uint x)
            {
                // Integer hash (finalizer from MurmurHash3) so nearby positions spread across the table.
                x ^= x >> 16;
                x *= 0x85EBCA6B;
                x ^= x >> 13;
                x *= 0xC2B2AE35;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
