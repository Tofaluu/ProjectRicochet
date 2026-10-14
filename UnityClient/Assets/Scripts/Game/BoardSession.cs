using System.Collections.Generic;
using SharedLogic;

namespace Ricochet.Client.Game
{
    /// <summary>What the board is showing; views pick the frame colour, banner, etc. from it.</summary>
    public enum BoardMode
    {
        /// <summary>Offline practice on a local board (N / H allowed).</summary>
        Practice,
        /// <summary>The player's own attempt at the online round.</summary>
        Round,
        /// <summary>A player's solution being replayed.</summary>
        PlayerReplay,
        /// <summary>The solver's shortest solution being replayed.</summary>
        ShortestReplay,
        /// <summary>The round ended without a replay to show (e.g. rejoined during the replays).</summary>
        RoundOver,
    }

    /// <summary>
    /// The board the player is looking at, as plain state: the position, their moves, any replay or
    /// countdown on it, and so what it shows (<see cref="Mode"/>). Every move goes through
    /// <see cref="GameRules"/>, and <see cref="Moves"/> is exactly what gets submitted to the server.
    /// <c>ScratchpadController</c> drives it (input, replay timing); views only read it.
    /// Times are passed in (seconds on any steady clock), so this has no Unity dependency.
    /// </summary>
    public sealed class BoardSession
    {
        private readonly List<Move> _moves = new List<Move>();
        private readonly List<RobotPositions> _history = new List<RobotPositions>();
        private float _countdownEnd = -1f;
        private bool _roundOver;
        // A finished unlocked replay still on the board (see FinishReplays).
        private bool _replayHeld;

        public RoundSetup Round { get; private set; }
        public RobotPositions Robots { get; private set; }
        public IReadOnlyList<Move> Moves => _moves;
        public RobotColor Selected { get; private set; } = RobotColor.Red;
        public bool IsSolved { get; private set; }

        /// <summary>Offline practice: the player may deal new boards and ask for the solution.</summary>
        public bool IsPractice { get; private set; } = true;
        /// <summary>The seed of an offline board (0 for a server board).</summary>
        public int Seed { get; private set; }
        /// <summary>Result of the offline "show shortest solution" request, if any.</summary>
        public string HintText { get; set; }

        /// <summary>
        /// The replay on the board (null when none). A locked replay stays here, on its final position,
        /// until the next board; see <see cref="ReplayFinished"/>.
        /// </summary>
        public ReplaySegment CurrentReplay { get; private set; }
        /// <summary>How many of <see cref="CurrentReplay"/>'s moves have been shown.</summary>
        public int ReplayStep { get; private set; }
        public bool ReplayFinished { get; private set; }
        public bool IsReplaying => CurrentReplay != null;
        /// <summary>A finished practice solution is still shown; the player's next action starts from the start.</summary>
        public bool IsHoldingReplay => _replayHeld;

        public bool IsCountingDown => _countdownEnd >= 0f;
        public string CountdownCaption { get; private set; }
        public float CountdownLeft(float now) => IsCountingDown ? System.Math.Max(0f, _countdownEnd - now) : 0f;

        public BoardMode Mode =>
            IsReplaying ? (CurrentReplay.IsShortest ? BoardMode.ShortestReplay : BoardMode.PlayerReplay)
            : _roundOver ? BoardMode.RoundOver
            : IsPractice ? BoardMode.Practice
            : BoardMode.Round;

        /// <summary>
        /// False while a replay, countdown or finished round has the board. A finished unlocked replay (the
        /// practice hint) stays shown but takes input: the player's next action starts again from the start.
        /// </summary>
        public bool AcceptsInput => Round != null && (!IsReplaying || _replayHeld) && !IsCountingDown && !_roundOver;

        /// <summary>Shows a new board (offline practice or the server's round) and clears everything on it.</summary>
        public void Load(RoundSetup round, bool practice, int seed = 0)
        {
            Round = round;
            IsPractice = practice;
            Seed = seed;
            HintText = null;
            _roundOver = false;
            _countdownEnd = -1f;
            StopReplay();
            Reset();
        }

        /// <summary>Robots back at the round's start, no moves (R).</summary>
        public void Reset()
        {
            if (_replayHeld)
                StopReplay();
            ResetBoard();
        }

        private void ResetBoard()
        {
            Robots = Round.Robots;
            _moves.Clear();
            _history.Clear();
            IsSolved = false;
            Selected = Round.Target.Robot ?? RobotColor.Red;
        }

        public void Select(RobotColor robot) => Selected = robot;

        public void SelectNext() => Selected = (RobotColor)(((int)Selected + 1) % Robot.Count);

        /// <summary>Moves the selected robot; false if it can't move or the board is already solved.</summary>
        public bool TryMove(Direction direction)
        {
            // A shown solution isn't the player's attempt: their first move starts from the beginning.
            if (_replayHeld)
                Reset();
            if (IsSolved)
                return false;
            var move = new Move(Selected, direction);
            if (!GameRules.TryMove(Round.Grid, Robots, move, out RobotPositions next))
                return false;

            _history.Add(Robots);
            _moves.Add(move);
            Robots = next;
            IsSolved = GameRules.ValidateSolution(Round.Grid, Round.Robots, Round.Target, _moves) == SolutionResult.Valid;
            return true;
        }

        public bool Undo()
        {
            if (_replayHeld)
            {
                Reset();
                return true;
            }
            if (_moves.Count == 0)
                return false;
            Robots = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            _moves.RemoveAt(_moves.Count - 1);
            IsSolved = false;
            return true;
        }

        // ---- Replays: driven step by step by the controller, which owns the timing ----

        /// <summary>Starts showing <paramref name="segment"/> from the round's start.</summary>
        public void BeginReplay(ReplaySegment segment)
        {
            CurrentReplay = segment;
            ReplayStep = 0;
            ReplayFinished = false;
            _replayHeld = false;
            ResetBoard();
        }

        /// <summary>Plays the replay's next move (if it can be played; it always can for a valid solution).</summary>
        public void ReplayNextMove()
        {
            Move move = CurrentReplay.Moves[ReplayStep];
            Selected = move.Robot;
            if (GameRules.TryMove(Round.Grid, Robots, move, out RobotPositions next))
            {
                _moves.Add(move);
                Robots = next;
            }
            ReplayStep++;
        }

        /// <summary>
        /// The last replay is over and stays on the board. Locked: until the next board; otherwise until the
        /// player acts (a move, undo or R starts again from the round's start, see <see cref="AcceptsInput"/>).
        /// </summary>
        public void FinishReplays(bool lockUntilNextBoard)
        {
            ReplayFinished = true;
            _replayHeld = !lockUntilNextBoard;
        }

        public void StopReplay()
        {
            CurrentReplay = null;
            ReplayStep = 0;
            ReplayFinished = false;
            _replayHeld = false;
        }

        /// <summary>The round is over without a replay to show: the board stays as it is, locked.</summary>
        public void EndRound() => _roundOver = true;

        /// <summary>Dims the board under a countdown until the next board arrives (<see cref="Load"/>).</summary>
        public void StartCountdown(float now, float seconds, string caption)
        {
            _countdownEnd = now + seconds;
            CountdownCaption = caption;
        }

        public void HideCountdown() => _countdownEnd = -1f;
    }
}
