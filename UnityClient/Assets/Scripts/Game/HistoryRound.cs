using System.Collections.Generic;
using SharedLogic;

namespace Ricochet.Client.Game
{
    /// <summary>One player's best solution for a board in the match history.</summary>
    public sealed class HistorySolution
    {
        public string Name { get; }
        public IReadOnlyList<Move> Moves { get; }

        public HistorySolution(string name, IReadOnlyList<Move> moves)
        {
            Name = name;
            Moves = moves;
        }
    }

    /// <summary>A finished board of the match (from the server's <see cref="RoundRecord"/>), decoded.</summary>
    public sealed class HistoryRound
    {
        public int MatchRound { get; }
        public RoundSetup Board { get; }
        /// <summary>Who took the point; null if nobody solved it.</summary>
        public string WinnerName { get; }
        /// <summary>Each player's best, shortest first.</summary>
        public IReadOnlyList<HistorySolution> Solutions { get; }
        /// <summary>A shortest solution, or null if the solver gave up on this board.</summary>
        public IReadOnlyList<Move> Shortest { get; }

        private HistoryRound(int matchRound, RoundSetup board, string winnerName,
            IReadOnlyList<HistorySolution> solutions, IReadOnlyList<Move> shortest)
        {
            MatchRound = matchRound;
            Board = board;
            WinnerName = winnerName;
            Solutions = solutions;
            Shortest = shortest;
        }

        public static HistoryRound From(RoundRecord record)
        {
            var solutions = new List<HistorySolution>();
            foreach (PlayerSolution solution in record.Solutions ?? new PlayerSolution[0])
                solutions.Add(new HistorySolution(solution.Name, MoveCodec.Decode(solution.Moves ?? new byte[0])));
            Move[] shortest = record.ShortestMoves != null && record.ShortestMoves.Length > 0
                ? MoveCodec.Decode(record.ShortestMoves)
                : null;
            return new HistoryRound(record.MatchRound, BoardCodec.Decode(record.Board), record.WinnerName, solutions, shortest);
        }
    }
}
