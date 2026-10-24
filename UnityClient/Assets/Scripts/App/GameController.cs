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
    /// Lobbies and matches without any widgets: feeds the <see cref="NetworkManager"/>'s messages into
    /// <see cref="MatchState"/> (which decides what's going on), keeps the <see cref="ScratchpadController"/> board in step
    /// (rounds, countdowns, replays, solving) and carries out the player's actions (join, leave, start, settings, votes).
    /// Views read <see cref="Match"/> and call the actions. Reaching the target during a round sends the solution if it
    /// beats the best (no button).
    /// </summary>
    public class GameController : MonoBehaviour
    {
        // Time to see the robot land before a solved board goes back to the start.
        private const float SolvedResetDelay = 0.9f;
        private const string NamePrefKey = "Ricochet.PlayerName";
        // Letters that can't be misread for each other (no I/O/0/1).
        private const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public MatchState Match { get; } = new MatchState();
        public NetworkManager Network { get; private set; }
        public ScratchpadController Scratchpad { get; private set; }

        /// <summary>The connection's latest status line ("Connecting...", "Join failed: ...").</summary>
        public string Status { get; private set; } = "";

        /// <summary>The game server (the cloud server unless given <c>?server=</c> / <c>-server</c>).</summary>
        public string ServerUrl { get; set; } = NetworkManager.DefaultServer;

        /// <summary>Started from a lobby link (?lobby= / -lobby): straight into a match, so no first-time tutorial.</summary>
        public bool LaunchedIntoLobby { get; private set; }
        /// <summary>?tutorial=0 / -notutorial: never start the first-time tutorial (automated tests use fresh profiles).</summary>
        public bool SkipTutorial { get; private set; }

        /// <summary>The name shown in lobbies and on the daily leaderboard (remembered on this device).</summary>
        public string PlayerName
        {
            get => PlayerPrefs.GetString(NamePrefKey, "");
            set => PlayerPrefs.SetString(NamePrefKey, (value ?? "").Trim());
        }

        private BoardSession Board => Scratchpad.Session;

        public void Init(NetworkManager network, ScratchpadController scratchpad)
        {
            Network = network;
            Scratchpad = scratchpad;
            Network.StatusChanged += OnStatusChanged;
            Network.Joined += OnJoined;
            Network.LobbyStateChanged += OnLobbyState;
            Network.RoundStarted += OnRoundStarted;
            Network.RoundCountdown += OnRoundCountdown;
            Network.TimerUpdated += OnTimerUpdated;
            Network.RoundTimeLimit += OnRoundTimeLimit;
            Network.NewBestSolution += OnNewBest;
            Network.MatchHistory += OnMatchHistory;
            Network.SolutionRejected += OnRejected;
            Network.RoundEnded += OnRoundEnded;
            Network.RequestFailed += OnRequestFailed;
            Scratchpad.Solved += OnBoardSolved;
        }

        private void OnDestroy()
        {
            if (Network == null)
                return;
            Network.StatusChanged -= OnStatusChanged;
            Network.Joined -= OnJoined;
            Network.LobbyStateChanged -= OnLobbyState;
            Network.RoundStarted -= OnRoundStarted;
            Network.RoundCountdown -= OnRoundCountdown;
            Network.TimerUpdated -= OnTimerUpdated;
            Network.RoundTimeLimit -= OnRoundTimeLimit;
            Network.NewBestSolution -= OnNewBest;
            Network.MatchHistory -= OnMatchHistory;
            Network.SolutionRejected -= OnRejected;
            Network.RoundEnded -= OnRoundEnded;
            Network.RequestFailed -= OnRequestFailed;
            Scratchpad.Solved -= OnBoardSolved;
        }

        // Optional overrides, e.g. for quick testing or a link to a lobby. Desktop command line:
        //   Ricochet.exe -name Alice -lobby TEST -server http://localhost:5022 -internal
        // Browser: the page's query string, e.g. https://ricochet.thomasliu.dev/?lobby=TEST&name=Alice
        // Passing a lobby joins straight away.
        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string name = QueryValue(Application.absoluteURL, "name");
            string server = QueryValue(Application.absoluteURL, "server");
            string lobby = QueryValue(Application.absoluteURL, "lobby");
            string internalFlag = QueryValue(Application.absoluteURL, "internal");
            string tutorialFlag = QueryValue(Application.absoluteURL, "tutorial");
#else
            string[] args = Environment.GetCommandLineArgs();
            string name = ArgValue(args, "-name");
            string server = ArgValue(args, "-server");
            string lobby = ArgValue(args, "-lobby");
            string internalFlag = Array.IndexOf(args, "-internal") >= 0 ? "1" : null;
            string tutorialFlag = Array.IndexOf(args, "-notutorial") >= 0 ? "0" : null;
#endif
            if (server != null)
                ServerUrl = server;
            // The developer's own devices: remembered, so it only has to be in the address once.
            if (internalFlag == "1" || internalFlag == "0")
                UsageTracker.SetInternal(internalFlag == "1");
            UsageTracker.Track("visit");
            LaunchedIntoLobby = lobby != null;
            SkipTutorial = tutorialFlag == "0";
            if (lobby != null)
                Join(lobby, name ?? PlayerName, save: false); // a test join doesn't overwrite the saved name
            else if (name != null && PlayerName.Length == 0)
                PlayerName = name;
        }

        public static string QueryValue(string url, string key)
        {
            int start = url.IndexOf('?');
            if (start < 0)
                return null;
            int end = url.IndexOf('#');
            string query = end > start ? url.Substring(start + 1, end - start - 1) : url.Substring(start + 1);
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && string.Equals(pair.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                {
                    string value = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')).Trim();
                    return value.Length > 0 ? value : null;
                }
            }
            return null;
        }

        private static string ArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        // ---- Player actions ----

        /// <summary>Joins (or creates) the lobby with this code. False with <see cref="Status"/> set if it can't try.</summary>
        public bool Join(string code, string name, bool save = true)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                Status = "Enter a lobby code.";
                return false;
            }
            if (save)
                PlayerName = name;
            Network.ServerUrl = ServerUrl;
            Network.JoinLobby(name, code);
            UsageTracker.Track("lobby_join");
            return true;
        }

        /// <summary>A new lobby: a random code nobody is likely to be using, joined straight away (it's created on joining).</summary>
        public void CreateLobby(string name)
        {
            var code = new char[4];
            for (int i = 0; i < code.Length; i++)
                code[i] = CodeLetters[UnityEngine.Random.Range(0, CodeLetters.Length)];
            Join(new string(code), name);
        }

        public void Leave() => Network.LeaveLobby();

        public void StartMatch()
        {
            Network.StartMatch();
            Match.OnStartRequested();
        }

        /// <summary>Toggles this player's vote to end the round early (or skip the replays); the server acts once everyone has voted.</summary>
        public void ToggleDone()
        {
            bool done = Match.ToggleDone();
            if (done)
                Scratchpad.View.PlaySkip();
            Network.SetDone(done);
        }

        /// <summary>Plays the current best (shown by the lobby) on this player's board, then puts their attempt back.</summary>
        public void WatchBest()
        {
            if (Match.CanWatchBest)
                Scratchpad.WatchSolution(new ReplaySegment($"{Match.BestPlayer}'s best", Match.BestSolution, 0.6f));
        }

        /// <summary>
        /// The host's new settings (the server clamps them and echoes them back in the next LobbyState). A minimum above
        /// the maximum pulls the maximum up with it, and a maximum below the minimum the other way.
        /// </summary>
        public void UpdateSettings(MatchSettings edited)
        {
            MatchSettings settings = Match.Settings;
            if (!Match.IAmHost || settings == null)
                return;
            if (edited.MinMoves > edited.MaxMoves)
            {
                if (edited.MinMoves != settings.MinMoves)
                    edited.MaxMoves = edited.MinMoves;
                else
                    edited.MinMoves = edited.MaxMoves;
            }
            Network.UpdateSettings(edited);
        }

        public static MatchSettings Copy(MatchSettings settings) => new MatchSettings
        {
            Mode = settings.Mode,
            PointsToWin = settings.PointsToWin,
            FirstTimerSeconds = settings.FirstTimerSeconds,
            ExtensionSeconds = settings.ExtensionSeconds,
            RoundLimitSeconds = settings.RoundLimitSeconds,
            ShowBestSolution = settings.ShowBestSolution,
            MinMoves = settings.MinMoves,
            MaxMoves = settings.MaxMoves,
        };

        // Reaching the target sends the solution if it can take the lead; either way the board then goes
        // back to the start (a longer solution is a dead end: any continuation of it is longer still).
        private void OnBoardSolved(int moves)
        {
            if (!Match.InLobby || Match.Stage != MatchStage.Solving)
            {
                if (Board.IsPractice)
                    UsageTracker.Track("practice_solve");
                return; // practice (or the daily puzzle, which has its own controller)
            }
            Move[] solution = new List<Move>(Board.Moves).ToArray();
            if (Match.OnSolved(moves))
            {
                Network.SubmitSolution(solution);
            }
            else
            {
                Network.RecordSolution(solution); // still the player's own best for the match history
                Scratchpad.View.MarkNotShorter();
            }
            // Not once the round is over (this solve may have ended it): the solved board stays until the replays.
            Scratchpad.ResetAfter(SolvedResetDelay, () => Match.InLobby && Match.Stage == MatchStage.Solving);
        }

        // ---- Server messages: update the state, keep the board in step ----

        private void OnStatusChanged(string status)
        {
            Status = status;
            // Left the lobby or lost the connection: back to an offline practice board.
            if (Match.OnConnectionStatus(status, Network.IsInLobby, Network.IsReconnecting))
                Scratchpad.PlayOffline();
        }

        private void OnJoined(string myName) => Match.OnJoined(myName);

        private void OnLobbyState(LobbyState state)
        {
            LobbyChange change = Match.OnLobbyState(state, Network.PlayerName);
            switch (change)
            {
                case LobbyChange.BetweenMatches:
                    // Free practice until the next Start (keeps a practice board the player is on).
                    if (!Board.IsPractice)
                        Scratchpad.PlayOffline();
                    break;
                case LobbyChange.RoundOver:
                    Scratchpad.EndRound();
                    break;
            }
        }

        // The next board is dealt in a few seconds: after Start, or after the previous round's replays.
        private void OnRoundCountdown(float seconds)
        {
            Match.OnCountdown(seconds, Time.time);
            Scratchpad.ShowCountdown(seconds, $"Round {Match.MatchRound + 1} starts in");
        }

        private void OnRoundStarted(int round, RoundSetup setup)
        {
            Match.OnRoundStarted(round);
            Scratchpad.LoadRound(setup);
        }

        private void OnTimerUpdated(int round, float secondsRemaining) => Match.OnTimerUpdated(round, secondsRemaining, Time.time);

        private void OnRoundTimeLimit(int round, float secondsRemaining) => Match.OnRoundTimeLimit(round, secondsRemaining, Time.time);

        private void OnMatchHistory(RoundRecord[] rounds) => Match.OnMatchHistory(rounds);

        private void OnNewBest(int round, string player, int moves, Move[] shown)
        {
            if (player != Match.MyName && round == Network.CurrentRound)
                Scratchpad.View.PlayOpponentSolved();
            int onBoard = Board.IsSolved && !Board.IsReplaying ? Board.Moves.Count : -1;
            if (Match.OnNewBest(round, player, moves, onBoard, shown))
                Scratchpad.ResetToStart();
        }

        private void OnRejected(string reason) => Match.OnRejected(reason);

        private void OnRequestFailed(string message) => Match.OnRequestFailed(message, Network.IsReconnecting);

        // The server waits for these replays before dealing the next board.
        private void OnRoundEnded(RoundResult result, Move[] winnerMoves, Move[] shortest)
        {
            IReadOnlyList<ReplaySegment> replays = Match.OnRoundEnded(result, winnerMoves, shortest);
            if (replays == null)
                return;
            if (result.WinnerName == null && !result.OutOfTime)
                Scratchpad.View.PlaySkip(); // everyone skipped the board
            if (replays.Count > 0)
                StartCoroutine(PlayReplaysAfter((float)result.SolvedPauseSeconds, replays, (float)result.ReplayPauseSeconds));
            else
                Scratchpad.EndRound();
        }

        // A solution that ended the round at once gets a moment for its slide and burst before the replays.
        private IEnumerator PlayReplaysAfter(float delay, IReadOnlyList<ReplaySegment> replays, float pause)
        {
            if (delay > 0f)
            {
                int round = Match.Round;
                yield return new WaitForSeconds(delay);
                if (!Match.InLobby || Match.Round != round || Match.Stage != MatchStage.RoundOver)
                    yield break; // left, or moved on meanwhile
            }
            Scratchpad.PlayReplays(replays, pause);
        }
    }
}
