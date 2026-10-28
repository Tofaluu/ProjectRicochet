namespace SharedLogic
{
    /// <summary>
    /// SignalR method names and message types shared by the server hub and the Unity client,
    /// so both sides always agree on the wire contract.
    /// </summary>
    /// <remarks>
    /// Boards travel as <see cref="BoardCodec"/> bytes and solutions as <see cref="MoveCodec"/>
    /// bytes (base64 in JSON), never as <see cref="Move"/> structs: System.Text.Json ignores
    /// fields by default, so a <see cref="Move"/> would arrive as all-default values.
    /// </remarks>
    public static class HubMethods
    {
        public const string HubPath = "/gamehub";

        /// <summary>
        /// Bump whenever the wire contract changes (method names, arguments, DTOs, codecs). The server
        /// refuses joins from any other version with a clear "update your game" message.
        /// </summary>
        public const int ProtocolVersion = 14;

        // Client -> server

        /// <summary>
        /// (string playerName, string lobbyCode, int protocolVersion, string playerId) returns string: the
        /// name as shown in the lobby, made unique (e.g. "Sam 2"). Clients use it to find themselves in
        /// <see cref="LobbyState"/>. Fails if protocolVersion != <see cref="ProtocolVersion"/>.
        /// playerId is a random id the client keeps for its whole run: joining again with it (e.g. after
        /// an automatic reconnect) takes back the player's seat and score. A dropped player's seat is
        /// kept for a while; joining with the same name also reclaims it (e.g. after restarting the game).
        /// </summary>
        public const string JoinLobby = "JoinLobby";

        /// <summary>() Leave the current lobby; same as disconnecting, but the connection stays open.</summary>
        public const string LeaveLobby = "LeaveLobby";

        /// <summary>
        /// () Host only, when no match is running: resets scores and starts a match with the lobby's
        /// <see cref="MatchSettings"/>. Also "Play again" after a match.
        /// </summary>
        public const string StartMatch = "StartMatch";

        /// <summary>(MatchSettings settings) Host only, between matches. Out-of-range values are clamped.</summary>
        public const string UpdateSettings = "UpdateSettings";

        /// <summary>
        /// (int roundNumber, byte[] moves). An attempt at the lead: accepted only if shorter than the current
        /// best. Either way a valid solution is kept as the player's own best for the match history.
        /// </summary>
        public const string SubmitSolution = "SubmitSolution";

        /// <summary>
        /// (int roundNumber, byte[] moves). A valid solution that doesn't beat the current best, kept only as
        /// the player's own best for the match history (<see cref="RoundRecord.Solutions"/>); no reply.
        /// </summary>
        public const string RecordSolution = "RecordSolution";

        /// <summary>
        /// (int roundNumber, bool done). Vote to end the round early; toggleable. The current leader
        /// can't vote and isn't waited on. When every other player is done the round ends now:
        /// skipped (no winner, no point) if nobody has solved it, otherwise as if the timer ran out.
        /// Re-checked whenever the lead changes, so taking the lead can end the round immediately.
        /// After the round ends the same vote means "skip the replay": once everyone except the winner
        /// has voted, the next board comes straight away.
        /// </summary>
        public const string SetDone = "SetDone";

        // Daily puzzle (no lobby needed). One board a day for everyone, resetting at midnight Eastern. guestId is a
        // random secret the client keeps on the device (16-64 letters, digits, '-' or '_'): it carries the player's
        // results and streak without an account. Errors (bad id, invalid solution, a day that's over) are HubExceptions.

        /// <summary>
        /// (string guestId, string name, int protocolVersion) returns <see cref="DailyPuzzleInfo"/>: today's puzzle
        /// and how the player stands on it. The day's first call starts the player's clock; name is shown on the
        /// leaderboard (updated each call). Fails if protocolVersion != <see cref="ProtocolVersion"/>.
        /// </summary>
        public const string GetDaily = "GetDaily";

        /// <summary>
        /// (string guestId, int number, byte[] moves) returns <see cref="DailyResult"/>. A solution to today's puzzle
        /// (number = <see cref="DailyPuzzleInfo.Number"/>). Players may keep improving until the reset; their best is
        /// the fewest moves, timed from first opening the puzzle (a later solution of the same length doesn't replace it).
        /// </summary>
        public const string SubmitDaily = "SubmitDaily";

        /// <summary>(string guestId, int number) returns <see cref="DailyLeaderboard"/>: the top players and the caller's rank.</summary>
        public const string GetDailyLeaderboard = "GetDailyLeaderboard";

        // Past puzzles (protocol 14). Any puzzle before today can be played again; those plays are kept apart from the
        // day's own results, so they never change its leaderboard or anyone's streak. Today's puzzle and its solution are
        // never sent through these.

        /// <summary>(string guestId) returns <see cref="DailyCalendar"/>: every puzzle so far and how the player did on it.</summary>
        public const string GetDailyCalendar = "GetDailyCalendar";

        /// <summary>(string guestId, int number) returns <see cref="DailyPastInfo"/>: a past puzzle (before today) to play again.</summary>
        public const string GetPastDaily = "GetPastDaily";

        /// <summary>
        /// (string guestId, int number, byte[] moves) returns <see cref="DailyPastResult"/>: a solution to a past puzzle,
        /// recorded as played later (the player's best later play is kept).
        /// </summary>
        public const string SubmitPastDaily = "SubmitPastDaily";

        /// <summary>
        /// (string guestId, int number) returns <see cref="DailySolution"/>: a past puzzle's shortest solution. The player's
        /// calendar marks the day as seen from then on.
        /// </summary>
        public const string ShowDailySolution = "ShowDailySolution";

        /// <summary>(string guestId) returns <see cref="StreakLeaderboard"/>: the longest current streaks.</summary>
        public const string GetStreakLeaderboard = "GetStreakLeaderboard";

        // Server -> client

        /// <summary>
        /// (LobbyState state). Sent to the whole lobby whenever players, scores, the host, the settings
        /// or the match phase change.
        /// </summary>
        public const string LobbyState = "LobbyState";

        /// <summary>
        /// (double seconds). The next board (the match's first, or the one after a round's replays) is
        /// dealt in this many seconds. Also sent to players who join during the countdown.
        /// </summary>
        public const string RoundCountdown = "RoundCountdown";

        /// <summary>(int roundNumber, byte[] board)</summary>
        public const string RoundStarted = "RoundStarted";

        /// <summary>
        /// (int roundNumber, double secondsRemaining). Sent when the first valid solution starts the
        /// countdown and whenever a better solution extends it. Replaces the round's time limit.
        /// </summary>
        public const string TimerUpdated = "TimerUpdated";

        /// <summary>
        /// (int roundNumber, double secondsRemaining). The round's time limit
        /// (<see cref="MatchSettings.RoundLimitSeconds"/>), sent after <see cref="RoundStarted"/> while
        /// nobody has solved the board. If it runs out first, the board ends unsolved (no point). The
        /// first solution switches to the normal timer (<see cref="TimerUpdated"/>) instead.
        /// </summary>
        public const string RoundTimeLimit = "RoundTimeLimit";

        /// <summary>
        /// (int roundNumber, string playerName, int moveCount, byte[] moves). moves is empty unless the lobby's
        /// <see cref="MatchSettings.ShowBestSolution"/> is on.
        /// </summary>
        public const string NewBestSolution = "NewBestSolution";

        /// <summary>(string reason). Sent only to the player whose submission was rejected.</summary>
        public const string SolutionRejected = "SolutionRejected";

        /// <summary>
        /// (RoundResult result). The round ended: timer ran out, everyone else was done, or someone
        /// matched the shortest possible solution. The next round's countdown starts after the replays
        /// (or sooner if everyone but the winner votes to skip).
        /// </summary>
        public const string RoundEnded = "RoundEnded";

        /// <summary>
        /// (RoundRecord[] rounds). The current (or just finished) match's boards so far, sent to a player who
        /// joins or rejoins mid-match or on the results; later boards arrive with each <see cref="RoundEnded"/>.
        /// </summary>
        public const string MatchHistory = "MatchHistory";
    }

    /// <summary>One player's best solution for a board (<see cref="MoveCodec"/> bytes).</summary>
    public class PlayerSolution
    {
        public string Name { get; set; }
        public byte[] Moves { get; set; }
    }

    /// <summary>A finished board of the match, for the match history: the board and everyone's solutions.</summary>
    public class RoundRecord
    {
        /// <summary>1-based within the match.</summary>
        public int MatchRound { get; set; }

        /// <summary><see cref="BoardCodec"/> bytes.</summary>
        public byte[] Board { get; set; }

        /// <summary>Who took the point; null if nobody solved it.</summary>
        public string WinnerName { get; set; }

        /// <summary>Each player's best solution, shortest first (equal lengths: whoever found it first).</summary>
        public PlayerSolution[] Solutions { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes of a shortest solution; empty if the solver gave up on this board.</summary>
        public byte[] ShortestMoves { get; set; }
    }

    /// <summary>
    /// How a round ended, and what to replay. Clients replay the winner's solution, then the shortest
    /// one if it's shorter (or only the shortest if nobody solved it), each from the round's start.
    /// </summary>
    public class RoundResult
    {
        public int RoundNumber { get; set; }

        /// <summary>The best solver (they only score if still in the lobby); null if nobody solved it.</summary>
        public string WinnerName { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes; empty if nobody solved it.</summary>
        public byte[] WinnerMoves { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes of a shortest solution; empty if the solver gave up on this board.</summary>
        public byte[] ShortestMoves { get; set; }

        public double ReplayStepSeconds { get; set; }
        public double ShortestReplayStepSeconds { get; set; }

        /// <summary>Pause on the starting position before each replay.</summary>
        public double ReplayPauseSeconds { get; set; }

        /// <summary>
        /// Pause before the replays when the round ended the moment a solution came in (the shortest possible, or
        /// the last player still in), so the solver's robot finishes its slide and burst first; 0 otherwise.
        /// </summary>
        public double SolvedPauseSeconds { get; set; }

        /// <summary>Nobody solved the board before the round's time limit ran out.</summary>
        public bool OutOfTime { get; set; }

        /// <summary>
        /// Set when this round's point won the match. The lobby switches to
        /// <see cref="MatchPhase.Finished"/> once the replays are over.
        /// </summary>
        public string MatchWinnerName { get; set; }

        /// <summary>This board for the match history, with everyone's solutions.</summary>
        public RoundRecord Record { get; set; }
    }

    public enum GameMode
    {
        /// <summary>Shortest solution when the timer runs out wins the round.</summary>
        Classic = 0,
    }

    public enum MatchPhase
    {
        /// <summary>Lobby just created: waiting for the host to start the first match.</summary>
        Waiting = 0,
        /// <summary>Rounds are being played.</summary>
        Playing = 1,
        /// <summary>Someone reached the points target; results until the host starts again.</summary>
        Finished = 2,
    }

    /// <summary>Chosen by the host between matches.</summary>
    public class MatchSettings
    {
        public const int MinPointsToWin = 1, MaxPointsToWin = 50;
        public const int MinTimerSeconds = 10, MaxTimerSeconds = 600;
        public const int MinRoundLimitSeconds = 60, MaxRoundLimitSeconds = 3600;
        // The range for both ends of the board length (MinMoves..MaxMoves). Boards of 16+ moves are too rare to
        // find while players wait (about 1 in 2400 random boards).
        public const int MinMinMoves = 2, MaxMinMoves = 15, DefaultMinMoves = 3, DefaultMaxMoves = MaxMinMoves;

        public GameMode Mode { get; set; }
        public int PointsToWin { get; set; }

        /// <summary>Timer started by the first valid solution of a round.</summary>
        public int FirstTimerSeconds { get; set; }

        /// <summary>Each better solution tops the timer up to at least this much.</summary>
        public int ExtensionSeconds { get; set; }

        /// <summary>
        /// Time to find a first solution before the board ends unsolved; 0 = no limit. Once someone
        /// solves it the normal timer takes over, so a late solution still gets its full countdown.
        /// </summary>
        public int RoundLimitSeconds { get; set; }

        /// <summary>
        /// When someone takes the lead, everyone gets the moves too (<see cref="HubMethods.NewBestSolution"/>)
        /// and can watch them on their own board. Only shorter solutions count, so copying gains nothing.
        /// </summary>
        public bool ShowBestSolution { get; set; }

        /// <summary>
        /// Boards are dealt with a shortest solution of <see cref="MinMoves"/> to <see cref="MaxMoves"/> moves. If
        /// the server can't find one in time it deals the longest it found that isn't past the maximum.
        /// </summary>
        public int MinMoves { get; set; }

        /// <summary>See <see cref="MinMoves"/>; 0 (unset) = <see cref="DefaultMaxMoves"/>; never below the minimum.</summary>
        public int MaxMoves { get; set; }

        /// <param name="maxMinMoves">The highest board length allowed (a dev server can allow longer boards); defaults to <see cref="MaxMinMoves"/>.</param>
        public MatchSettings Clamped(int maxMinMoves = MaxMinMoves)
        {
            return new MatchSettings
            {
                Mode = GameMode.Classic, // the only mode so far
                PointsToWin = Clamp(PointsToWin, MinPointsToWin, MaxPointsToWin),
                FirstTimerSeconds = Clamp(FirstTimerSeconds, MinTimerSeconds, MaxTimerSeconds),
                ExtensionSeconds = Clamp(ExtensionSeconds, MinTimerSeconds, MaxTimerSeconds),
                RoundLimitSeconds = RoundLimitSeconds <= 0 ? 0 : Clamp(RoundLimitSeconds, MinRoundLimitSeconds, MaxRoundLimitSeconds),
                ShowBestSolution = ShowBestSolution,
                MinMoves = Clamp(MinMoves, MinMinMoves, maxMinMoves),
                MaxMoves = Clamp(MaxMoves <= 0 ? DefaultMaxMoves : MaxMoves, Clamp(MinMoves, MinMinMoves, maxMinMoves), maxMinMoves),
            };
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }

    public class PlayerInfo
    {
        public string Name { get; set; }
        public int Score { get; set; }

        /// <summary>Has voted to end this round early (see <see cref="HubMethods.SetDone"/>).</summary>
        public bool Done { get; set; }

        /// <summary>Holds the current best solution this round.</summary>
        public bool Leading { get; set; }

        /// <summary>False while their connection is lost; the seat is kept for them to come back.</summary>
        public bool Connected { get; set; }
    }

    public class LobbyState
    {
        public string Code { get; set; }

        /// <summary>In join order.</summary>
        public PlayerInfo[] Players { get; set; }

        /// <summary>Name of the host, who starts matches and picks the settings. Passes to the next player if they leave.</summary>
        public string Host { get; set; }

        public MatchPhase Phase { get; set; }
        public MatchSettings Settings { get; set; }

        /// <summary>Round number within the current match (1-based; 0 before the first board).</summary>
        public int MatchRound { get; set; }

        /// <summary>Set while <see cref="Phase"/> is <see cref="MatchPhase.Finished"/>.</summary>
        public string MatchWinner { get; set; }

        /// <summary>
        /// True from <see cref="HubMethods.RoundEnded"/> until the next board (replays, then the countdown):
        /// the current board can't be solved any more. Tells a player who (re)joins then.
        /// </summary>
        public bool RoundOver { get; set; }
    }

    /// <summary>Today's daily puzzle and how the asking player stands on it (<see cref="HubMethods.GetDaily"/>).</summary>
    public class DailyPuzzleInfo
    {
        /// <summary>Puzzle #1 was the launch day; one per day after that.</summary>
        public int Number { get; set; }

        /// <summary>The puzzle's day in Eastern time, "yyyy-MM-dd".</summary>
        public string Date { get; set; }

        /// <summary><see cref="BoardCodec"/> bytes.</summary>
        public byte[] Board { get; set; }

        /// <summary>The shortest possible solution's length (the solution itself is never sent).</summary>
        public int ShortestLength { get; set; }

        /// <summary>Since the player first opened today's puzzle (their clock).</summary>
        public double SecondsSinceOpened { get; set; }

        /// <summary>Until the next puzzle (midnight Eastern).</summary>
        public double SecondsUntilNext { get; set; }

        /// <summary>The player's best today: moves (0 = not solved yet) and the time it took.</summary>
        public int BestMoves { get; set; }
        public double BestSeconds { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes of the player's own best solution (empty until solved), for their share text.</summary>
        public byte[] BestSolution { get; set; }

        /// <summary>Days in a row solved, counting today if solved, else up to yesterday.</summary>
        public int Streak { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.SubmitDaily"/>.</summary>
    public class DailyResult
    {
        /// <summary>This solution: its length and the time since the player first opened the puzzle.</summary>
        public int Moves { get; set; }
        public double Seconds { get; set; }

        /// <summary>It's the player's new best (their first solve, or fewer moves than before).</summary>
        public bool Improved { get; set; }

        /// <summary>The player's best after this solution, and its moves (<see cref="MoveCodec"/> bytes).</summary>
        public int BestMoves { get; set; }
        public double BestSeconds { get; set; }
        public byte[] BestSolution { get; set; }

        /// <summary>The player's place among today's solvers (1 = first) and how many have solved it.</summary>
        public int Rank { get; set; }
        public int Players { get; set; }

        public int Streak { get; set; }
        public int ShortestLength { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.GetDailyLeaderboard"/>: fewest moves first, then fastest.</summary>
    public class DailyLeaderboard
    {
        public int Number { get; set; }
        public DailyLeaderboardEntry[] Top { get; set; }

        /// <summary>The asking player's place (0 = not solved yet) and how many have solved it.</summary>
        public int MyRank { get; set; }
        public int Players { get; set; }
    }

    public class DailyLeaderboardEntry
    {
        public string Name { get; set; }
        public int Moves { get; set; }
        public double Seconds { get; set; }

        /// <summary>The asking player.</summary>
        public bool IsMe { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.GetDailyCalendar"/>.</summary>
    public class DailyCalendar
    {
        /// <summary>Today's puzzle number (its day is in <see cref="Days"/> too, but can't be played from the calendar).</summary>
        public int Today { get; set; }

        /// <summary>Every puzzle from #1 to today, oldest first. Days the server has no puzzle for are left out.</summary>
        public DailyDay[] Days { get; set; }
    }

    /// <summary>One day of <see cref="DailyCalendar"/>, from the asking player's side.</summary>
    public class DailyDay
    {
        public int Number { get; set; }

        /// <summary>"yyyy-MM-dd", Eastern time.</summary>
        public string Date { get; set; }

        public int ShortestLength { get; set; }

        /// <summary>The player opened it on its day; <see cref="BestMoves"/> is their best then (0 = not solved).</summary>
        public bool Opened { get; set; }
        public int BestMoves { get; set; }

        /// <summary>Their best when playing it later (0 = not solved later), and whether they've looked at its solution.</summary>
        public int LaterMoves { get; set; }
        public bool SawSolution { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.GetPastDaily"/>: a past puzzle and the player's results on it.</summary>
    public class DailyPastInfo
    {
        public int Number { get; set; }
        public string Date { get; set; }

        /// <summary><see cref="BoardCodec"/> bytes.</summary>
        public byte[] Board { get; set; }

        public int ShortestLength { get; set; }

        /// <summary>The player's result on the day (moves 0 = not solved then), with their solution's <see cref="MoveCodec"/> bytes.</summary>
        public bool Opened { get; set; }
        public int BestMoves { get; set; }
        public double BestSeconds { get; set; }
        public byte[] BestSolution { get; set; }

        /// <summary>Their best when playing it later (0 = none yet) and its moves.</summary>
        public int LaterMoves { get; set; }
        public byte[] LaterSolution { get; set; }

        public bool SawSolution { get; set; }

        /// <summary>How many solved it on its day.</summary>
        public int Players { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.SubmitPastDaily"/>.</summary>
    public class DailyPastResult
    {
        public int Moves { get; set; }

        /// <summary>It's the player's new best later play (their first, or fewer moves).</summary>
        public bool Improved { get; set; }

        public int LaterMoves { get; set; }
        public byte[] LaterSolution { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.ShowDailySolution"/>.</summary>
    public class DailySolution
    {
        public int Number { get; set; }

        /// <summary><see cref="MoveCodec"/> bytes of a shortest solution.</summary>
        public byte[] Moves { get; set; }
    }

    /// <summary>The answer to <see cref="HubMethods.GetStreakLeaderboard"/>: longest current streak first.</summary>
    public class StreakLeaderboard
    {
        public StreakEntry[] Top { get; set; }

        /// <summary>The asking player's streak and place (0 = no streak), and how many players have one.</summary>
        public int MyStreak { get; set; }
        public int MyRank { get; set; }
        public int Players { get; set; }
    }

    public class StreakEntry
    {
        public string Name { get; set; }

        /// <summary>Days in a row solved, up to today (or yesterday, while today can still be saved).</summary>
        public int Days { get; set; }

        /// <summary>Equal streaks share a place.</summary>
        public int Rank { get; set; }

        public bool IsMe { get; set; }
    }
}
