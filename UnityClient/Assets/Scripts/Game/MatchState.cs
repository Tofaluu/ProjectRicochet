using System.Collections.Generic;
using System.Linq;
using SharedLogic;

namespace Ricochet.Client.Game
{
    /// <summary>Where the lobby is, from this player's point of view.</summary>
    public enum MatchStage
    {
        NotInLobby,
        /// <summary>Waiting for the host to start, or the results of the last match (free practice).</summary>
        BetweenMatches,
        /// <summary>The match started but the first board hasn't arrived yet.</summary>
        Dealing,
        /// <summary>The next board is dealt when the countdown ends.</summary>
        Countdown,
        Solving,
        /// <summary>The replays of the round just played.</summary>
        RoundOver,
    }

    /// <summary>What the "done" vote means right now (the leader has none: they aren't part of the vote).</summary>
    public enum DoneAction
    {
        None,
        /// <summary>Nobody has solved it yet: give up on the board.</summary>
        SkipBoard,
        /// <summary>The timer is running: end the round early.</summary>
        ImDone,
        /// <summary>The round is over: skip the rest of the replays.</summary>
        SkipReplay,
    }

    /// <summary>What a <see cref="MatchState.OnLobbyState"/> changed that the board has to follow.</summary>
    public enum LobbyChange
    {
        None,
        /// <summary>Just joined between matches, or the match just ended: free practice on a local board.</summary>
        BetweenMatches,
        /// <summary>The host pressed Start; the countdown and first board follow.</summary>
        MatchStarted,
        /// <summary>The board on screen can't be solved any more and no replay was shown (rejoined late).</summary>
        RoundOver,
    }

    /// <summary>
    /// This player's view of the lobby, match and round, built from the server's messages, plus the
    /// decisions that follow from it (what the vote means, whether a solution is sent, the messages to
    /// show). Plain C#: views (<c>App/AppUI</c>) read it and decide how it looks. Times are passed in
    /// (seconds on any steady clock), so this has no Unity dependency and is unit-tested.
    /// </summary>
    /// <summary>A place in a finished match's final standings (kept after the player leaves).</summary>
    public sealed class FinalPlace
    {
        public string Name { get; }
        public int Score { get; }
        public bool Left { get; set; }

        public FinalPlace(string name, int score)
        {
            Name = name;
            Score = score;
        }
    }

    public sealed class MatchState
    {
        private float _countdownEnd = -1f;
        private float _timerEnd = -1f;
        private float _timerExtendedUntil = -1f;
        private float _limitEnd = -1f;
        private HashSet<string> _playerNames = new HashSet<string>();
        private readonly List<HistoryRound> _history = new List<HistoryRound>();

        /// <summary>How long "+time" is shown after the timer was topped up.</summary>
        public const float TimeAddedNoticeSeconds = 2f;

        public string MyName { get; private set; }

        // ---- Lobby and match (from the latest LobbyState) ----

        public bool InLobby { get; private set; }
        public string LobbyCode { get; private set; }
        public MatchPhase Phase { get; private set; }
        public string Host { get; private set; }
        public bool IAmHost => Host != null && Host == MyName;
        public MatchSettings Settings { get; private set; }
        /// <summary>Round number within the match (the next board is <c>MatchRound + 1</c> during a countdown).</summary>
        public int MatchRound { get; private set; }
        public string MatchWinner { get; private set; }
        /// <summary>Highest score first; ties stay in join order.</summary>
        public IReadOnlyList<PlayerInfo> Standings { get; private set; } = new PlayerInfo[0];

        // ---- The round on screen ----

        /// <summary>The server's (wire) round number of the board on screen; 0 when there's none.</summary>
        public int Round { get; private set; }
        public bool RoundOver { get; private set; }
        public bool TimerRunning => _timerEnd >= 0f;
        /// <summary>Nobody has solved the board yet and the round has a time limit.</summary>
        public bool HasTimeLimit => _limitEnd >= 0f;
        public string BestPlayer { get; private set; }
        public int BestMoves { get; private set; }
        /// <summary>The best's moves, when the lobby shows them (<see cref="MatchSettings.ShowBestSolution"/>); else null.</summary>
        public IReadOnlyList<Move> BestSolution { get; private set; }
        /// <summary>Someone else leads and the lobby shows their solution: offer to watch it.</summary>
        public bool CanWatchBest => Stage == MatchStage.Solving && BestSolution != null && BestSolution.Count > 0 && BestPlayer != MyName;
        public bool IAmLeading { get; private set; }
        public bool IAmDone { get; private set; }
        /// <summary>
        /// The vote among the players who can vote: connected ones, minus the leader during a round (ending it early
        /// is the others' call); skipping the replays is everyone's, the winner's too.
        /// </summary>
        public int DoneCount => Voters.Count(p => p.Done);
        public int VoterCount => Voters.Count();
        private IEnumerable<PlayerInfo> Voters =>
            _players.Where(p => p.Connected && (Stage == MatchStage.RoundOver || !p.Leading));
        private PlayerInfo[] _players = new PlayerInfo[0];

        /// <summary>The current (or just finished) match's boards with everyone's solutions, in order.</summary>
        public IReadOnlyList<HistoryRound> History => _history;

        /// <summary>The latest message for the player (rejections, the lead changing, connection trouble).</summary>
        public string Feedback { get; private set; } = "";

        /// <summary>The finished round's headline ("Bob wins the round") and its detail ("5 moves, the shortest possible").</summary>
        public string ResultTitle { get; private set; }
        public string ResultDetail { get; private set; }

        /// <summary>
        /// The finished match's final scores, highest first, as they stood when it ended: players who leave afterwards stay
        /// (marked <see cref="FinalPlace.Left"/>). Empty outside the results.
        /// </summary>
        public IReadOnlyList<FinalPlace> FinalStandings => _finalStandings;
        private readonly List<FinalPlace> _finalStandings = new List<FinalPlace>();

        public MatchStage Stage =>
            !InLobby ? MatchStage.NotInLobby
            : Phase != MatchPhase.Playing ? MatchStage.BetweenMatches
            : _countdownEnd >= 0f ? MatchStage.Countdown
            : Round == 0 ? MatchStage.Dealing
            : RoundOver ? MatchStage.RoundOver
            : MatchStage.Solving;

        public float CountdownLeft(float now) => _countdownEnd >= 0f ? System.Math.Max(0f, _countdownEnd - now) : 0f;
        public float TimeLeft(float now) => TimerRunning ? System.Math.Max(0f, _timerEnd - now) : 0f;
        public float TimeLimitLeft(float now) => HasTimeLimit ? System.Math.Max(0f, _limitEnd - now) : 0f;
        /// <summary>A better solution just bought everyone more time.</summary>
        public bool TimeJustAdded(float now) => now < _timerExtendedUntil;

        public DoneAction DoneAction
        {
            get
            {
                MatchStage stage = Stage;
                if ((IAmLeading && stage == MatchStage.Solving) || (stage != MatchStage.Solving && stage != MatchStage.RoundOver))
                    return DoneAction.None;
                if (stage == MatchStage.RoundOver)
                    return DoneAction.SkipReplay;
                return TimerRunning ? DoneAction.ImDone : DoneAction.SkipBoard;
            }
        }


        // ---- Server messages ----

        /// <summary>
        /// The join call's answer: this player's name as the lobby shows it. It can arrive after the first
        /// <see cref="OnLobbyState"/> (sent while joining), which then didn't know who we are.
        /// </summary>
        public void OnJoined(string myName) => MyName = myName;

        public LobbyChange OnLobbyState(LobbyState state, string myName)
        {
            MyName = myName;
            bool first = !InLobby;
            MatchPhase previous = Phase;
            if (first)
                _history.Clear(); // a mid-match joiner gets the match's boards so far (OnMatchHistory)
            InLobby = true;
            LobbyCode = state.Code;
            Phase = state.Phase;
            Host = state.Host;
            Settings = state.Settings;
            MatchRound = state.MatchRound;
            MatchWinner = state.MatchWinner;
            Standings = state.Players.OrderBy(p => -p.Score).ToArray(); // OrderBy is stable

            _playerNames = new HashSet<string>(state.Players.Select(p => p.Name));
            LobbyChange change = ApplyPhase(state, first, previous);
            UpdateFinalStandings(first || previous != MatchPhase.Finished);

            PlayerInfo me = state.Players.FirstOrDefault(p => p.Name == myName);
            IAmDone = me != null && me.Done;
            IAmLeading = me != null && me.Leading;
            _players = state.Players;
            return change;
        }

        private LobbyChange ApplyPhase(LobbyState state, bool first, MatchPhase previous)
        {
            if (Phase != MatchPhase.Playing && (first || previous == MatchPhase.Playing))
            {
                ClearRound();
                Feedback = ""; // the results (or the waiting room) say what's next
                return LobbyChange.BetweenMatches;
            }
            if (Phase == MatchPhase.Playing && !first && previous != MatchPhase.Playing)
            {
                ClearRound();
                _history.Clear();
                Feedback = "";
                return LobbyChange.MatchStarted;
            }
            // Normally RoundEnded said so already; this is for a board caught up on after the round ended.
            if (state.RoundOver && Round > 0 && !RoundOver)
            {
                RoundOver = true;
                _timerEnd = -1f;
                BestPlayer = null;
                ResultTitle = "Round over";
                ResultDetail = "The next board is on its way.";
                return LobbyChange.RoundOver;
            }
            return LobbyChange.None;
        }

        public void OnCountdown(float seconds, float now) => _countdownEnd = now + seconds;

        public void OnRoundStarted(int round)
        {
            ClearRound();
            Round = round;
            Feedback = "";
        }

        /// <returns>False if it's for another round.</returns>
        public bool OnTimerUpdated(int round, float secondsRemaining, float now)
        {
            if (round != Round)
                return false;
            // A later update means a better solution bought everyone more time.
            if (TimerRunning)
                _timerExtendedUntil = now + TimeAddedNoticeSeconds;
            _timerEnd = now + secondsRemaining;
            _limitEnd = -1f; // the normal timer replaces the round's time limit
            return true;
        }

        /// <returns>False if it's for another round (or the board was already solved).</returns>
        public bool OnRoundTimeLimit(int round, float secondsRemaining, float now)
        {
            if (round != Round || TimerRunning || RoundOver)
                return false;
            _limitEnd = now + secondsRemaining;
            return true;
        }

        /// <summary>
        /// A new best solution. When someone else's beats (or matches) the solution still sitting on this
        /// player's board, the board should go back to the start so they can look for a shorter one;
        /// boards mid-attempt, or holding an even shorter solution, are left alone.
        /// </summary>
        /// <param name="boardSolution">Moves of the solution on this player's board, or -1 if it isn't solved.</param>
        /// <returns>True if the board should be reset.</returns>
        /// <param name="shownMoves">The best's moves if the lobby shows them, else null or empty.</param>
        public bool OnNewBest(int round, string player, int moves, int boardSolution, IReadOnlyList<Move> shownMoves = null)
        {
            if (round != Round)
                return false;
            BestPlayer = player;
            BestMoves = moves;
            BestSolution = shownMoves != null && shownMoves.Count > 0 ? shownMoves : null;
            // Our own solution was accepted; the board already went back to the start when it was sent.
            if (player == MyName)
            {
                Feedback = $"You lead with {moves} moves! Can you find a shorter one?";
                return false;
            }
            if (boardSolution < 0 || boardSolution < moves)
                return false;
            Feedback = $"{player} found {moves} moves — your board was reset, try to beat it!";
            return true;
        }

        public void OnRejected(string reason) => Feedback = "Rejected: " + reason;

        /// <summary>
        /// The round's result. Returns the replays to show: the winner's solution, then the shortest if
        /// it's shorter (only the shortest when nobody solved it). Null if it's for another round.
        /// </summary>
        public IReadOnlyList<ReplaySegment> OnRoundEnded(RoundResult result, Move[] winnerMoves, Move[] shortest)
        {
            if (result.RoundNumber != Round)
                return null;
            if (result.Record != null)
                AddToHistory(HistoryRound.From(result.Record));
            RoundOver = true;
            BestSolution = null;
            _timerEnd = -1f;
            _limitEnd = -1f;
            Feedback = ""; // the round's card tells the result

            string winner = result.WinnerName;
            var replays = new List<ReplaySegment>();
            if (winnerMoves.Length > 0)
            {
                int moves = winnerMoves.Length;
                bool wasShortest = shortest != null && moves <= shortest.Length;
                // The best solver only scores if they still have a seat.
                if (!_playerNames.Contains(winner))
                {
                    ResultTitle = "No point this round";
                    ResultDetail = $"{winner} had the best solution ({moves} moves) but left.";
                }
                else
                {
                    ResultTitle = winner == MyName ? "You win the round" : $"{winner} wins the round";
                    ResultDetail = wasShortest ? $"{moves} moves, the shortest possible"
                        : shortest != null ? $"{moves} moves. The shortest was {shortest.Length}."
                        : $"{moves} moves";
                }

                string whose = winner == MyName ? "Your solution" : $"{winner}'s solution";
                replays.Add(new ReplaySegment(whose, winnerMoves, (float)result.ReplayStepSeconds));
                if (shortest != null && !wasShortest)
                    replays.Add(new ReplaySegment("Shortest possible", shortest, (float)result.ShortestReplayStepSeconds, isShortest: true));
            }
            else
            {
                ResultTitle = result.OutOfTime ? "Time's up" : "Board skipped";
                ResultDetail = shortest != null ? $"Nobody solved it. It could be done in {shortest.Length} moves." : "Nobody solved it.";
                if (shortest != null)
                    replays.Add(new ReplaySegment("Shortest possible", shortest, (float)result.ShortestReplayStepSeconds, isShortest: true));
            }

            string matchWinner = result.MatchWinnerName;
            if (matchWinner != null)
                ResultTitle = matchWinner == MyName ? "You win the match!" : $"{matchWinner} wins the match!";
            return replays;
        }

        /// <summary>The match's boards so far, sent on (re)joining mid-match or on the results.</summary>
        public void OnMatchHistory(IEnumerable<RoundRecord> rounds)
        {
            _history.Clear();
            foreach (RoundRecord record in rounds)
                AddToHistory(HistoryRound.From(record));
        }

        private void AddToHistory(HistoryRound round)
        {
            // A rejoin's catch-up may overlap a RoundEnded.
            _history.RemoveAll(r => r.MatchRound == round.MatchRound);
            _history.Add(round);
            _history.Sort((a, b) => a.MatchRound.CompareTo(b.MatchRound));
        }

        public void OnRequestFailed(string message, bool reconnecting)
        {
            // A request cut off by the dropped connection; the reconnect message says more.
            if (!reconnecting)
                Feedback = message;
        }

        /// <summary>The connection status changed (see <c>NetworkManager.StatusChanged</c>).</summary>
        /// <returns>True if this player is no longer in the lobby (left, or the connection gave up).</returns>
        public bool OnConnectionStatus(string status, bool inLobby, bool reconnecting)
        {
            if (!InLobby)
                return false;
            if (!inLobby)
            {
                Clear();
                return true;
            }
            // Still in the lobby while reconnecting (the server keeps the seat): just say what's happening.
            if (reconnecting)
                Feedback = status;
            else if (status == "Reconnected")
                Feedback = "Reconnected!";
            return false;
        }

        // ---- This player's actions ----

        /// <summary>
        /// The player's board reached the target with <paramref name="moveCount"/> moves during the round
        /// (<see cref="MatchStage.Solving"/>); either way the board then goes back to the start. Only strictly
        /// shorter solutions can take the lead, so a sure rejection isn't sent; the message says why instead.
        /// </summary>
        /// <returns>True if the solution should be sent.</returns>
        public bool OnSolved(int moveCount)
        {
            if (BestPlayer != null && moveCount >= BestMoves)
            {
                Feedback = BestPlayer == MyName
                    ? $"{moveCount} moves doesn't beat your own {BestMoves}. Try for fewer!"
                    : $"{moveCount} moves doesn't beat {BestPlayer}'s {BestMoves}. Try for fewer!";
                return false;
            }
            Feedback = $"Sent {moveCount} moves...";
            return true;
        }

        /// <summary>Flips this player's vote for instant feedback (the next LobbyState confirms it).</summary>
        /// <returns>The new vote, to send.</returns>
        public bool ToggleDone() => IAmDone = !IAmDone;

        public void OnStartRequested() => Feedback = "Starting...";

        // The results' standings: taken when the match ends (or on joining its results), then only marks who has left.
        private void UpdateFinalStandings(bool fresh)
        {
            if (Phase != MatchPhase.Finished)
            {
                _finalStandings.Clear();
                return;
            }
            if (fresh)
            {
                _finalStandings.Clear();
                _finalStandings.AddRange(Standings.Select(p => new FinalPlace(p.Name, p.Score)));
            }
            foreach (FinalPlace place in _finalStandings)
                place.Left = !_playerNames.Contains(place.Name);
        }

        private void ClearRound()
        {
            Round = 0;
            RoundOver = false;
            ResultTitle = null;
            ResultDetail = null;
            _countdownEnd = -1f;
            _timerEnd = -1f;
            _timerExtendedUntil = -1f;
            _limitEnd = -1f;
            BestPlayer = null;
            BestSolution = null;
            IAmDone = false;
            IAmLeading = false;
        }

        private void Clear()
        {
            ClearRound();
            InLobby = false;
            Host = null;
            Settings = null;
            MatchWinner = null;
            Standings = new PlayerInfo[0];
            _players = new PlayerInfo[0];
            Phase = MatchPhase.Waiting;
            _history.Clear();
        }
    }
}
