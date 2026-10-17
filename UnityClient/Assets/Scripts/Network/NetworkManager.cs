using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// SignalR connection to the game server. Server messages may arrive on background threads,
    /// so they're queued and raised as events on the main thread in <see cref="Update"/>.
    /// See <see cref="HubMethods"/> for the wire contract, <see cref="IHubConnection"/> for the
    /// desktop / browser clients.
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        // The cloud server (deploy/). Local testing: type localhost:5022 in the Server field.
        public const string DefaultServer = "https://ricochet.thomasliu.dev";

        [SerializeField] private string _serverUrl = DefaultServer + HubMethods.HubPath;

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        // Identifies this run of the game to the server, so a reconnect takes back the same seat.
        private readonly string _playerId = Guid.NewGuid().ToString("N");
        private IHubConnection _connection;
        private string _connectedUrl;
        private bool _listening;
        private string _lobbyCode;

        public event Action<LobbyState> LobbyStateChanged;
        /// <summary>(seconds until the next board is dealt)</summary>
        public event Action<float> RoundCountdown;
        /// <summary>(round number, board)</summary>
        public event Action<int, RoundSetup> RoundStarted;
        /// <summary>(round number, seconds remaining)</summary>
        public event Action<int, float> TimerUpdated;
        /// <summary>(round number, seconds of the round's time limit remaining)</summary>
        public event Action<int, float> RoundTimeLimit;
        /// <summary>(round number, player name, move count, their moves if the lobby shows them (else empty))</summary>
        public event Action<int, string, int, Move[]> NewBestSolution;
        /// <summary>(the match's boards so far) on joining mid-match or on the results</summary>
        public event Action<RoundRecord[]> MatchHistory;
        /// <summary>(reason)</summary>
        public event Action<string> SolutionRejected;
        /// <summary>(result, winner's moves (empty if none), shortest moves (null if the solver gave up))</summary>
        public event Action<RoundResult, Move[], Move[]> RoundEnded;
        /// <summary>Human-readable connection status for the UI.</summary>
        public event Action<string> StatusChanged;
        /// <summary>A lobby request (start, settings) was refused; the message is for the player.</summary>
        public event Action<string> RequestFailed;
        /// <summary>(this player's name in the lobby) The join (or rejoin) went through. Usually comes after the first LobbyStateChanged.</summary>
        public event Action<string> Joined;

        public bool IsInLobby { get; private set; }

        /// <summary>The connection dropped and is being re-established; the lobby seat is kept meanwhile.</summary>
        public bool IsReconnecting { get; private set; }

        /// <summary>This player's name as the server shows it (unique in the lobby, e.g. "Sam 2").</summary>
        public string PlayerName { get; private set; }
        public int CurrentRound { get; private set; }

        /// <summary>
        /// Hub URL. Accepts a bare host or base address (e.g. a tunnel URL pasted by a player):
        /// the scheme defaults to https (http for localhost) and the hub path is appended.
        /// </summary>
        public string ServerUrl
        {
            get => _serverUrl;
            set => _serverUrl = NormalizeServerUrl(value);
        }

        public static string NormalizeServerUrl(string input)
        {
            string url = (input ?? "").Trim().TrimEnd('/');
            if (url.Length == 0)
                url = DefaultServer;
            if (!url.Contains("://"))
                url = (url.StartsWith("localhost") || url.StartsWith("127.0.0.1") ? "http://" : "https://") + url;
            if (!url.EndsWith(HubMethods.HubPath, StringComparison.OrdinalIgnoreCase))
                url += HubMethods.HubPath;
            return url;
        }

        private void Awake()
        {
            // Keep processing server messages (and the timer) while the window is unfocused.
            Application.runInBackground = true;
        }

        public async void JoinLobby(string playerName, string lobbyCode)
        {
            try
            {
                if (_connection == null || _connection.IsDisconnected || _connectedUrl != _serverUrl)
                    await ConnectAsync();

                SetStatus("Joining " + lobbyCode + "...");
                // Lobby messages start arriving before the join call returns.
                _listening = true;
                PlayerName = await _connection.InvokeAsync<string>(
                    HubMethods.JoinLobby, playerName, lobbyCode, HubMethods.ProtocolVersion, _playerId);
                _lobbyCode = lobbyCode;
                IsInLobby = true;
                Joined?.Invoke(PlayerName);
                SetStatus("In lobby " + lobbyCode.Trim().ToUpperInvariant());
            }
            catch (Exception e)
            {
                // A rejected join leaves us wherever we were before.
                _listening = IsInLobby;
                SetStatus("Join failed: " + Describe(e));
            }
        }

        public async void LeaveLobby()
        {
            if (_connection == null || !IsInLobby)
                return;

            // Stop reacting to the old lobby right away, including messages already on their way.
            _listening = false;
            IsInLobby = false;
            SetStatus("Left the lobby");
            try
            {
                await _connection.InvokeAsync(HubMethods.LeaveLobby);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Network] Leave failed: " + Describe(e));
            }
        }

        public async void SubmitSolution(IReadOnlyList<Move> moves)
        {
            if (_connection == null || !IsInLobby)
                return;

            try
            {
                await _connection.InvokeAsync(HubMethods.SubmitSolution, CurrentRound, MoveCodec.Encode(moves));
            }
            catch (Exception e)
            {
                SetStatus("Submit failed: " + Describe(e));
            }
        }

        /// <summary>Host only: start a match (or play again after one).</summary>
        public async void StartMatch()
        {
            if (_connection == null || !IsInLobby)
                return;

            try
            {
                await _connection.InvokeAsync(HubMethods.StartMatch);
            }
            catch (Exception e)
            {
                ReportError("Couldn't start: " + Describe(e));
            }
        }

        /// <summary>Host only, between matches.</summary>
        public async void UpdateSettings(MatchSettings settings)
        {
            if (_connection == null || !IsInLobby)
                return;

            try
            {
                await _connection.InvokeAsync(HubMethods.UpdateSettings, settings);
            }
            catch (Exception e)
            {
                ReportError("Couldn't change settings: " + Describe(e));
            }
        }

        /// <summary>A valid solution that doesn't beat the best: kept by the server for the match history only.</summary>
        public async void RecordSolution(IReadOnlyList<Move> moves)
        {
            if (_connection == null || !IsInLobby)
                return;

            try
            {
                await _connection.InvokeAsync(HubMethods.RecordSolution, CurrentRound, MoveCodec.Encode(moves));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Network] Record failed: " + Describe(e));
            }
        }

        /// <summary>Vote (or take back the vote) to end the current round early.</summary>
        public async void SetDone(bool done)
        {
            if (_connection == null || !IsInLobby)
                return;

            try
            {
                await _connection.InvokeAsync(HubMethods.SetDone, CurrentRound, done);
            }
            catch (Exception e)
            {
                SetStatus("Vote failed: " + Describe(e));
            }
        }

        // ---- Daily puzzle (no lobby needed; errors surface as exceptions, see DescribeError) ----

        /// <summary>Today's daily puzzle; the day's first call starts the player's clock.</summary>
        public async Task<DailyPuzzleInfo> GetDailyAsync(string guestId, string playerName)
        {
            await EnsureConnectedAsync();
            return await _connection.InvokeAsync<DailyPuzzleInfo>(HubMethods.GetDaily, guestId, playerName, HubMethods.ProtocolVersion);
        }

        public async Task<DailyResult> SubmitDailyAsync(string guestId, int number, IReadOnlyList<Move> moves)
        {
            await EnsureConnectedAsync();
            return await _connection.InvokeAsync<DailyResult>(HubMethods.SubmitDaily, guestId, number, MoveCodec.Encode(moves));
        }

        public async Task<DailyLeaderboard> GetDailyLeaderboardAsync(string guestId, int number)
        {
            await EnsureConnectedAsync();
            return await _connection.InvokeAsync<DailyLeaderboard>(HubMethods.GetDailyLeaderboard, guestId, number);
        }

        /// <summary>The server's message for a failed call (without the transport's prefix).</summary>
        public static string DescribeError(Exception e) => Describe(e);

        // Reuses a live connection (never resets a lobby's); connects otherwise.
        private async Task EnsureConnectedAsync()
        {
            if (_connection == null || _connection.IsDisconnected || _connectedUrl != _serverUrl)
            {
                await ConnectAsync();
                SetStatus(""); // connected: clear "Connecting to..." (a lobby join sets its own status)
            }
        }

        private async Task ConnectAsync()
        {
            SetStatus("Connecting to " + _serverUrl + "...");
            _connection?.Dispose();

            IsInLobby = false;
            IsReconnecting = false;
            IHubConnection connection = CreateConnection(_serverUrl);
            _connection = connection;
            _connectedUrl = _serverUrl;

            _connection.On<LobbyState>(HubMethods.LobbyState,
                state => PostLobbyEvent(() => LobbyStateChanged?.Invoke(state)));
            _connection.On<double>(HubMethods.RoundCountdown,
                seconds => PostLobbyEvent(() => RoundCountdown?.Invoke((float)seconds)));
            _connection.On<int, byte[]>(HubMethods.RoundStarted, (round, board) =>
            {
                RoundSetup setup = BoardCodec.Decode(board);
                PostLobbyEvent(() =>
                {
                    CurrentRound = round;
                    RoundStarted?.Invoke(round, setup);
                });
            });
            _connection.On<int, double>(HubMethods.TimerUpdated,
                (round, seconds) => PostLobbyEvent(() => TimerUpdated?.Invoke(round, (float)seconds)));
            _connection.On<int, double>(HubMethods.RoundTimeLimit,
                (round, seconds) => PostLobbyEvent(() => RoundTimeLimit?.Invoke(round, (float)seconds)));
            _connection.On<int, string, int, byte[]>(HubMethods.NewBestSolution, (round, player, count, shown) =>
            {
                Move[] moves = MoveCodec.Decode(shown ?? Array.Empty<byte>());
                PostLobbyEvent(() => NewBestSolution?.Invoke(round, player, count, moves));
            });
            _connection.On<RoundRecord[]>(HubMethods.MatchHistory,
                rounds => PostLobbyEvent(() => MatchHistory?.Invoke(rounds ?? Array.Empty<RoundRecord>())));
            _connection.On<string>(HubMethods.SolutionRejected,
                reason => PostLobbyEvent(() => SolutionRejected?.Invoke(reason)));
            _connection.On<RoundResult>(HubMethods.RoundEnded, result =>
            {
                Move[] winnerMoves = MoveCodec.Decode(result.WinnerMoves ?? Array.Empty<byte>());
                Move[] shortest = result.ShortestMoves != null && result.ShortestMoves.Length > 0
                    ? MoveCodec.Decode(result.ShortestMoves)
                    : null;
                PostLobbyEvent(() => RoundEnded?.Invoke(result, winnerMoves, shortest));
            });

            _connection.Reconnecting += () =>
            {
                Post(() =>
                {
                    if (_connection != connection)
                        return;
                    IsReconnecting = true;
                    SetStatus("Connection lost, reconnecting...");
                });
            };
            // A new connection id: join again with the same player id to take back the seat.
            _connection.Reconnected += () =>
            {
                Post(() =>
                {
                    if (_connection == connection)
                        Rejoin();
                });
            };
            // Also raised when the automatic reconnect gives up.
            _connection.Closed += error =>
            {
                Post(() =>
                {
                    // A replaced connection can report its closure late; only the current one matters.
                    if (_connection != connection)
                        return;
                    IsInLobby = false;
                    IsReconnecting = false;
                    _listening = false;
                    SetStatus(error == null ? "Disconnected" : "Disconnected: " + error);
                });
            };

            await connection.StartAsync();
        }

        private static IHubConnection CreateConnection(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new JsHubConnection(url);
#else
            return new DotNetHubConnection(url);
#endif
        }

        private async void Rejoin()
        {
            if (!IsInLobby)
            {
                IsReconnecting = false;
                SetStatus("Reconnected");
                return;
            }
            try
            {
                PlayerName = await _connection.InvokeAsync<string>(
                    HubMethods.JoinLobby, PlayerName, _lobbyCode, HubMethods.ProtocolVersion, _playerId);
                Joined?.Invoke(PlayerName);
                IsReconnecting = false;
                SetStatus("Reconnected");
            }
            catch (Exception e)
            {
                // E.g. the seat expired and the lobby is gone: back to the join screen.
                IsReconnecting = false;
                IsInLobby = false;
                _listening = false;
                SetStatus("Couldn't rejoin: " + Describe(e));
            }
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void OnDestroy()
        {
            IHubConnection connection = _connection;
            _connection = null;
            connection?.Dispose();
        }

        private void Post(Action action) => _mainThreadQueue.Enqueue(action);

        // Lobby messages are dropped (when dispatched) once we've left, so a late one can't pull the
        // UI back into a lobby we no longer belong to.
        private void PostLobbyEvent(Action action) => Post(() =>
        {
            if (_listening)
                action();
        });

        // Called after an await, which may resume off the main thread.
        private void ReportError(string message)
        {
            Debug.LogWarning("[Network] " + message);
            Post(() => RequestFailed?.Invoke(message));
        }

        private void SetStatus(string status)
        {
            Debug.Log("[Network] " + status);
            StatusChanged?.Invoke(status);
        }

        // HubExceptions arrive wrapped in a generic prefix; show just the server's message.
        private static string Describe(Exception e)
        {
            const string marker = "HubException: ";
            int index = e.Message.IndexOf(marker, StringComparison.Ordinal);
            return index >= 0 ? e.Message.Substring(index + marker.Length) : e.Message;
        }
    }
}
