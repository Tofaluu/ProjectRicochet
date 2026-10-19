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
        private string _notice;
        private float _noticeUntil;

        public DailyState State { get; } = new DailyState();

        /// <summary>A short message to show for a moment ("Copied!", "Not shorter than your best"), else null.</summary>
        public string Notice => Time.time < _noticeUntil ? _notice : null;

        /// <summary>The streak as last seen (0 if never played on this device).</summary>
        public int KnownStreak => State.Number > 0 ? State.Streak : PlayerPrefs.GetInt(StreakPrefKey, 0);
        public int KnownNumber => State.Number > 0 ? State.Number : PlayerPrefs.GetInt(NumberPrefKey, 0);

        private ScratchpadController Scratchpad => _game.Scratchpad;
        private NetworkManager Network => _game.Network;
        private bool OnDailyBoard => State.Stage == DailyStage.Playing && Scratchpad.Session.Round == State.Round;

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
                Scratchpad.ShowDaily(State.Round);
                if (State.HasSolved)
                    RefreshLeaderboard();
                // Already perfect: show how, then the board stays on the solved position (locked: nothing to improve).
                if (State.IsPerfect && State.BestSolution.Length > 0)
                    Scratchpad.PlayReplays(new[] { new ReplaySegment("Your solution", State.BestSolution, 0.6f) }, 0.5f,
                        lockUntilNextBoard: true, controllable: true); // their own solution: pause and step freely
            }
            catch (Exception e)
            {
                State.OnFailed(NetworkManager.DescribeError(e));
            }
        }

        public void Close(bool backToPractice)
        {
            bool wasOnBoard = OnDailyBoard;
            State.Close();
            if (wasOnBoard)
                Scratchpad.Session.Title = null;
            if (backToPractice)
                Scratchpad.PlayOffline();
        }

        public void CopyResult()
        {
            Clipboard.Copy(State.ShareText(ShareLink));
            Show("Copied! Paste it anywhere.");
        }

        private void OnSolved(int moves)
        {
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
            if (Scratchpad.Session.Round == round && State.Stage == DailyStage.Playing)
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
            if (State.IsActive && Network.IsInLobby)
                Close(backToPractice: false);
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
