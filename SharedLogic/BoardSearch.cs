using System;

namespace SharedLogic
{
    /// <summary>
    /// Looks for a board whose shortest solution has between <see cref="MinMoves"/> and <see cref="MaxMoves"/>
    /// moves (lobbies: a minimum only; practice: an exact length), one solve per <see cref="Step"/> so the caller
    /// decides how long to search (the server: a time budget on a background thread; the browser, which has no
    /// threads: a few steps per frame).
    /// </summary>
    /// <remarks>
    /// Long boards are rare (about 1 in 350 random boards reaches 14), so a board that falls short is nudged
    /// (<see cref="BoardGenerator.Nudge"/>: one robot moved or another target), keeping any change that isn't
    /// shorter (nor past the maximum). That climbs to long boards far faster than rerolling; after
    /// <see cref="ClimbSteps"/> nudges, or from a fresh board already past the maximum, it starts over. Boards
    /// the solver gives up on are skipped, so every board found has a known shortest solution.
    /// </remarks>
    public sealed class BoardSearch
    {
        public const int ClimbSteps = 40;

        private readonly Random _random;
        private readonly TimeSpan? _solverTimeLimit;
        private RoundSetup _current;
        private int _currentLength;
        private int _step;

        public BoardSearch(int minMoves, Random random, TimeSpan? solverTimeLimit, int maxMoves = int.MaxValue)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (maxMoves < minMoves) throw new ArgumentOutOfRangeException(nameof(maxMoves));
            MinMoves = minMoves;
            MaxMoves = maxMoves;
            _random = random;
            _solverTimeLimit = solverTimeLimit;
        }

        public int MinMoves { get; }
        public int MaxMoves { get; }

        /// <summary>Solves so far.</summary>
        public int Solves { get; private set; }

        /// <summary>A board in the range was found: it's <see cref="Longest"/>.</summary>
        public bool Found { get; private set; }

        /// <summary>
        /// The longest board found so far that isn't past <see cref="MaxMoves"/> (null until one is solved),
        /// for giving up early.
        /// </summary>
        public RoundSetup Longest { get; private set; }

        /// <summary><see cref="Longest"/>'s shortest solution.</summary>
        public Move[] LongestMoves { get; private set; }

        /// <summary>
        /// Generates or nudges one board and solves it. True once a board is in the range (then
        /// <see cref="Found"/>; further steps do nothing). <paramref name="noTimeLimit"/> lets the solver
        /// take as long as it needs (when nothing was solved in time and the caller can't wait for luck).
        /// </summary>
        public bool Step(bool noTimeLimit = false)
        {
            if (Found)
                return true;
            if (_current == null || _step > ClimbSteps)
            {
                _current = BoardGenerator.GenerateRound(_random);
                _currentLength = -1;
                _step = 0;
            }

            RoundSetup candidate = _step == 0 ? _current : BoardGenerator.Nudge(_current, _random);
            SolverResult solved = Solver.Solve(candidate, timeLimit: noTimeLimit ? null : _solverTimeLimit);
            Solves++;
            bool usable = solved.Solved && solved.Moves.Length <= MaxMoves;
            if (!usable && _step == 0)
            {
                _current = null; // too hard to solve in time, or already too long: a fresh board next
                return false;
            }
            _step++;
            if (usable && solved.Moves.Length >= _currentLength)
            {
                _current = candidate;
                _currentLength = solved.Moves.Length;
                if (Longest == null || _currentLength > LongestMoves.Length)
                {
                    Longest = candidate;
                    LongestMoves = solved.Moves;
                }
                if (_currentLength >= MinMoves)
                {
                    // The board that's in range (an earlier, equally long one may be Longest).
                    Longest = candidate;
                    LongestMoves = solved.Moves;
                    Found = true;
                }
            }
            return Found;
        }
    }
}
