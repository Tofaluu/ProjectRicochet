using System.Collections.Generic;
using SharedLogic;

namespace Ricochet.Client.Game
{
    /// <summary>One solution to replay from the round's start, e.g. "Bob's solution" or "Shortest possible".</summary>
    public sealed class ReplaySegment
    {
        public string Label { get; }
        public IReadOnlyList<Move> Moves { get; }
        public float StepSeconds { get; }
        /// <summary>The solver's shortest solution (drawn in the "algorithm" colour) rather than a player's.</summary>
        public bool IsShortest { get; }

        public ReplaySegment(string label, IReadOnlyList<Move> moves, float stepSeconds, bool isShortest = false)
        {
            Label = label;
            Moves = moves;
            StepSeconds = stepSeconds;
            IsShortest = isShortest;
        }
    }
}
