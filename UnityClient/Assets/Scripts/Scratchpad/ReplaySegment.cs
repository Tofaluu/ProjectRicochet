using System.Collections.Generic;
using SharedLogic;

namespace Ricochet.Client.Scratchpad
{
    /// <summary>One solution to replay from the round's start, e.g. "Bob's solution" or "Shortest possible".</summary>
    public sealed class ReplaySegment
    {
        public string Label { get; }
        public IReadOnlyList<Move> Moves { get; }
        public float StepSeconds { get; }

        public ReplaySegment(string label, IReadOnlyList<Move> moves, float stepSeconds)
        {
            Label = label;
            Moves = moves;
            StepSeconds = stepSeconds;
        }
    }
}
