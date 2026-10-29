using System;
using System.Collections;
using System.Collections.Generic;
using Ricochet.Client.Game;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.App
{
    /// <summary>
    /// The daily puzzle's flow without any widgets: <see cref="Open"/> loads today's board (the server starts the player's
    /// clock), reaching the target sends the solution if it beats their best (else the "not shorter" sound), and the board
    /// goes back to the start so they can keep improving until midnight Eastern; a perfect solve locks it. State lives in
    /// <see cref="DailyState"/>; views read <see cref="State"/> and <see cref="Notice"/>. Joining a lobby ends it.
    /// Past puzzles (<see cref="Archive"/>): a calendar of every puzzle so far; picking a day puts its board up to play again
    /// (no clock, never on that day's leaderboard or the streak), with the player's own solution and the shortest to watch.
    /// </summary>
    public class DailyController : MonoBehaviour
    {
        private const float SolvedResetDelay = 0.9f;
        private const string ShareLink = "https://ricochet.thomasliu.dev";
        // The last streak and puzzle seen, for the home screen's card before today's puzzle is opened (opening it
        // starts the clock, so it isn't fetched just to show the card).
        private const string StreakPrefKey = "Ricochet.DailyStreak";
        private const string NumberPrefKey = "Ricochet.DailyNumber";

        private GameController _game;
        // The practice board the daily puzzle took over (brought back on leaving), and the player's daily attempt (brought
        // back on returning), so switching screens shows the right board at once instead of a fresh one.
        private ScratchpadController.SavedBoard _practice, _attempt;
        private string _notice;
        private float _noticeUntil;

        public DailyState State { get; } = new DailyState();
        public DailyArchive Archive { get; } = new DailyArchive();

        /// <summary>A short message to show for a moment ("Copied!", "Not shorter than your best"), else null.</summary>
        public string Notice => Time.time < _noticeUntil ? _notice : null;

        /// <summary>The streak as last seen (0 if never played on this device).</summary>
        public int KnownStreak => State.Number > 0 ? State.Streak : PlayerPrefs.GetInt(StreakPrefKey, 0);
        public int KnownNumber => State.Number > 0 ? State.Number : PlayerPrefs.GetInt(NumberPrefKey, 0);

        private ScratchpadController Scratchpad => _game.Scratchpad;
        private NetworkManager Network => _game.Network;
        private bool OnDailyBoard => State.Stage == DailyStage.Playing && Scratchpad.Session.Round == State.Round;
        private bool OnPastBoard => Archive.IsOpen && Archive.Past != null && Scratchpad.Session.Round == Archive.Past.Round;

        /// <summary>The selected past puzzle's board is on screen.</summary>
        // While the next past puzzle loads, the board shown (the last one picked, or today's when coming from it) stays up
        // rather than blanking for a moment; coming from practice it stays hidden instead (no flash of the practice board).
        public bool ShowingPastBoard => OnPastBoard || (_holdBoard && Archive.Past == null);
        private bool _holdBoard;

        /// <summary>The daily puzzle's board is on screen (false while it loads or failed: the board stays hidden then).</summary>
        public bool ShowingBoard => OnDailyBoard;

        public void Init(GameController game)
        {
            _game = game;
            Scratchpad.Solved += OnSolved;
        }

        private void OnDestroy()
        {
            if (_game != null && Scratchpad != null)
                Scratchpad.Solved -= OnSolved;
        }

        // ---- Flow ----

        public async void Open()
        {
            if (State.Stage == DailyStage.Loading)
                return;
            if (Archive.IsOpen)
                Archive.Close(); // the practice board it saved stays the one to go back to
            else if (!State.IsActive)
                _practice = Scratchpad.SaveBoard();
            // Opened earlier this visit: show it straight away (the server already started the clock).
            if (!State.IsActive && State.CanReopen(Time.time))
            {
                State.Reopen();
                UsageTracker.Track("daily_open");
                ShowBoard(_attempt != null && _attempt.Round == State.Round ? _attempt : null);
                if (State.HasSolved)
                    RefreshLeaderboard();
                return;
            }
            State.BeginLoading();
            try
            {
                Network.ServerUrl = _game.ServerUrl;
                DailyPuzzleInfo info = await Network.GetDailyAsync(GuestIdentity.Id, _game.PlayerName);
                if (State.Stage != DailyStage.Loading || Network.IsInLobby)
                    return; // closed, or joined a lobby, meanwhile
                State.OnLoaded(info, Time.time);
                Remember();
                UsageTracker.Track("daily_open");
                _attempt = null;
                ShowBoard(null);
                if (State.HasSolved)
                    RefreshLeaderboard();
                RefreshCalendar();
            }
            catch (Exception e)
            {
                State.OnFailed(NetworkManager.DescribeError(e));
            }
        }

        // Today's board: the player's attempt as they left it, or from the start. Already perfect: their solution plays,
        // then the board stays on the solved position (locked: nothing to improve).
        private void ShowBoard(ScratchpadController.SavedBoard attempt)
        {
            if (State.IsPerfect && State.BestSolution.Length > 0)
            {
                Scratchpad.ShowDaily(State.Round);
                Scratchpad.PlayReplays(new[] { new ReplaySegment("Your solution", State.BestSolution, 0.6f) }, 0.5f,
                    lockUntilNextBoard: true, controllable: true); // their own solution: pause and step freely
            }
            else if (attempt != null)
                Scratchpad.RestoreBoard(attempt);
            else
                Scratchpad.ShowDaily(State.Round);
        }

        public void Close(bool backToPractice)
        {
            bool wasOnBoard = OnDailyBoard || OnPastBoard;
            if (OnDailyBoard)
                _attempt = Scratchpad.SaveBoard();
            State.Close();
            Archive.Close();
            if (wasOnBoard)
                Scratchpad.Session.Title = null;
            if (backToPractice)
            {
                if (_practice != null)
                    Scratchpad.RestoreBoard(_practice);
                else
                    Scratchpad.PlayOffline();
            }
            _practice = null;
        }

        public void CopyResult()
        {
            Clipboard.Copy(State.ShareText(ShareLink));
            Show("Copied! Paste it anywhere.");
        }

        private void OnSolved(int moves)
        {
            if (OnPastBoard)
            {
                OnPastSolved(moves);
                return;
            }
            if (!OnDailyBoard)
                return;
            Move[] solution = new List<Move>(Scratchpad.Session.Moves).ToArray();
            if (!State.IsImprovement(moves))
            {
                Scratchpad.View.MarkNotShorter();
                Show($"Not shorter than your best ({State.BestMoves}). Try again!");
            }
            else
            {
                Submit(solution);
                if (moves <= State.ShortestLength)
                {
                    // The best possible: the puzzle is done; the solved board stays, locked (after the burst, which
                    // celebrates the player's own solve only while the board is still theirs).
                    StartCoroutine(LockAfterBurst(State.Round));
                    return;
                }
            }
            // Back to the start to look for a shorter one (a longer solution is a dead end).
            Scratchpad.ResetAfter(SolvedResetDelay);
        }

        private IEnumerator LockAfterBurst(RoundSetup round)
        {
            yield return new WaitForSeconds(SolvedResetDelay);
            if (Scratchpad.Session.Round == round && (State.Stage == DailyStage.Playing || OnPastBoard))
                Scratchpad.EndRound();
        }

        private async void Submit(Move[] solution)
        {
            State.OnSubmitting();
            try
            {
                DailyResult result = await Network.SubmitDailyAsync(GuestIdentity.Id, State.Number, solution);
                bool firstSolve = !State.HasSolved;
                State.OnSubmitted(result);
                Remember();
                if (firstSolve && result.Improved)
                    UsageTracker.Track("daily_solve");
                RefreshLeaderboard();
                RefreshCalendar();
                if (firstSolve && Archive.Streaks != null)
                    RefreshStreaks();
            }
            catch (Exception e)
            {
                string message = NetworkManager.DescribeError(e);
                if (message.Contains("is over"))
                {
                    // Midnight passed while playing: today's puzzle instead.
                    Show("A new daily puzzle is out!");
                    Open();
                    return;
                }
                State.OnFailed(message);
            }
        }

        private async void RefreshLeaderboard()
        {
            try
            {
                State.OnLeaderboard(await Network.GetDailyLeaderboardAsync(GuestIdentity.Id, State.Number));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Daily] Leaderboard failed: " + NetworkManager.DescribeError(e));
            }
        }

        // ---- Past puzzles ----

        /// <summary>
        /// The past-puzzles screen, with <paramref name="number"/> (else the latest past day) on the board. Coming from
        /// today's puzzle keeps the player's attempt for later, like any screen switch.
        /// </summary>
        public void OpenArchive(int number = 0)
        {
            _holdBoard = OnDailyBoard || OnPastBoard;
            if (State.IsActive)
            {
                if (OnDailyBoard)
                    _attempt = Scratchpad.SaveBoard();
                State.Close();
            }
            else if (!Archive.IsOpen)
                _practice = Scratchpad.SaveBoard();
            Archive.Open();
            if (number == 0 && Archive.Today > 1)
                number = Archive.Today - 1; // the calendar is known already (the daily screen loaded it): yesterday, at once
            if (number > 0)
                SelectPast(number);
            LoadCalendar(number);
        }

        /// <summary>Puts a past day's puzzle on the board (today's goes back to the daily screen).</summary>
        public async void SelectPast(int number)
        {
            if (!Archive.IsOpen || number < 1)
                return;
            if (Archive.Today > 0 && number >= Archive.Today)
            {
                Open();
                return;
            }
            if (Archive.Selected == number && (Archive.LoadingPast || Archive.Past?.Number == number))
                return;
            Archive.Select(number);
            try
            {
                // The day's leaderboard comes with it, so the card and the board change together.
                DailyPastInfo info = await Network.GetPastDailyAsync(GuestIdentity.Id, number);
                DailyLeaderboard board = null;
                try
                {
                    board = await Network.GetDailyLeaderboardAsync(GuestIdentity.Id, number);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Daily] Past leaderboard failed: " + NetworkManager.DescribeError(e));
                }
                if (!Archive.IsOpen || Archive.Selected != number || Network.IsInLobby)
                    return;
                Archive.OnPast(info);
                Archive.Past.OnLeaderboard(board);
                Scratchpad.ShowDaily(Archive.Past.Round);
                _holdBoard = true;
            }
            catch (Exception e)
            {
                if (Archive.Selected == number)
                    Archive.OnFailed(NetworkManager.DescribeError(e));
            }
        }

        private async void LoadCalendar(int selecting)
        {
            try
            {
                Network.ServerUrl = _game.ServerUrl;
                Archive.OnCalendar(await Network.GetDailyCalendarAsync(GuestIdentity.Id));
                // Nothing picked yet: the latest past day.
                if (Archive.IsOpen && selecting == 0 && Archive.Selected == 0 && Archive.Today > 1)
                    SelectPast(Archive.Today - 1);
            }
            catch (Exception e)
            {
                Archive.OnFailed(NetworkManager.DescribeError(e));
            }
        }

        private async void RefreshCalendar()
        {
            try
            {
                Archive.OnCalendar(await Network.GetDailyCalendarAsync(GuestIdentity.Id));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Daily] Calendar failed: " + NetworkManager.DescribeError(e));
            }
        }

        public async void RefreshStreaks()
        {
            try
            {
                Network.ServerUrl = _game.ServerUrl;
                Archive.OnStreaks(await Network.GetStreakLeaderboardAsync(GuestIdentity.Id));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Daily] Streaks failed: " + NetworkManager.DescribeError(e));
            }
        }

        /// <summary>Plays the player's own best solution of the selected past puzzle (on the day or later).</summary>
        public void WatchMine()
        {
            PastPuzzle past = Archive.Past;
            if (past == null || past.MySolution.Length == 0)
                return;
            ShowPastBoard();
            Scratchpad.PlayReplays(new[] { new ReplaySegment("Your solution", past.MySolution, 0.6f) }, 0.5f,
                lockUntilNextBoard: false, controllable: true);
        }

        /// <summary>Plays the selected past puzzle's shortest solution (asking the server the first time: the day is then marked as seen).</summary>
        public async void ShowShortest()
        {
            PastPuzzle past = Archive.Past;
            if (past == null)
                return;
            if (past.Shortest == null)
            {
                try
                {
                    Archive.OnSolution(await Network.ShowDailySolutionAsync(GuestIdentity.Id, past.Number));
                }
                catch (Exception e)
                {
                    Show(NetworkManager.DescribeError(e));
                    return;
                }
                if (Archive.Past != past || past.Shortest == null)
                    return;
            }
            ShowPastBoard();
            Scratchpad.PlayReplays(new[] { new ReplaySegment("Shortest solution", past.Shortest, 0.6f, isShortest: true) }, 0.5f,
                lockUntilNextBoard: false, controllable: true);
        }

        /// <summary>The selected past puzzle from the start (after a perfect solve locked it, or to try again).</summary>
        public void RestartPast()
        {
            if (Archive.Past != null)
                Scratchpad.ShowDaily(Archive.Past.Round);
        }

        // The board may have moved on (a lobby, another screen): put the past puzzle back first.
        private void ShowPastBoard()
        {
            if (!OnPastBoard)
                Scratchpad.ShowDaily(Archive.Past.Round);
        }

        private void OnPastSolved(int moves)
        {
            PastPuzzle past = Archive.Past;
            Move[] solution = new List<Move>(Scratchpad.Session.Moves).ToArray();
            if (!past.IsImprovement(moves))
            {
                Scratchpad.View.MarkNotShorter();
                Show($"Not shorter than your best later play ({past.LaterMoves}).");
            }
            else
            {
                SubmitPast(past, solution);
                if (moves <= past.ShortestLength)
                {
                    StartCoroutine(LockAfterBurst(past.Round));
                    return;
                }
            }
            Scratchpad.ResetAfter(SolvedResetDelay);
        }

        private async void SubmitPast(PastPuzzle past, Move[] solution)
        {
            past.OnSubmitting();
            try
            {
                DailyPastResult result = await Network.SubmitPastDailyAsync(GuestIdentity.Id, past.Number, solution);
                if (Archive.Past == past)
                    Archive.OnSubmitted(result);
                else
                    past.OnSubmitted(result);
            }
            catch (Exception e)
            {
                past.OnSubmitFailed();
                Show(NetworkManager.DescribeError(e));
            }
        }

        private void Remember()
        {
            PlayerPrefs.SetInt(StreakPrefKey, State.Streak);
            PlayerPrefs.SetInt(NumberPrefKey, State.Number);
        }

        private void Show(string notice)
        {
            _notice = notice;
            _noticeUntil = Time.time + 2.5f;
        }

        private void Update()
        {
            if (_game == null)
                return;
            // A lobby takes the board over.
            if ((State.IsActive || Archive.IsOpen) && Network.IsInLobby)
                Close(backToPractice: false);
            if (OnPastBoard)
            {
                PastPuzzle past = Archive.Past;
                Scratchpad.Session.Title = past.IsPerfect ? $"Daily #{past.Number}  ·  perfect"
                    : past.BestMoves > 0 ? $"Daily #{past.Number}  ·  best {past.BestMoves}"
                    : $"Daily #{past.Number}  ·  {past.Date:MMM d}";
                return;
            }
            if (!OnDailyBoard)
                return;
            Scratchpad.Session.Title = State.IsPerfect ? $"Daily #{State.Number}  ·  perfect!"
                : State.HasSolved ? $"Daily #{State.Number}  ·  best {State.BestMoves}"
                : $"Daily #{State.Number}  ·  {DailyState.Clock(State.Elapsed(Time.time))}";
            if (State.UntilNext(Time.time) <= 0f && State.Stage == DailyStage.Playing)
            {
                Show("A new daily puzzle is out!");
                Open();
            }
        }
    }
}
