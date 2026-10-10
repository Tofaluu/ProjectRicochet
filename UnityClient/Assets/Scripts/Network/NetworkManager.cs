using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// SignalR connection to the game server. Server messages arrive on background threads,
    /// so they're queued and raised as events on the main thread in <see cref="Update"/>.
    /// See <see cref="HubMethods"/> for the wire contract.
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        public const string DefaultServer = "http://localhost:5022";

        [SerializeField] private string _serverUrl = DefaultServer + HubMethods.HubPath;

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private HubConnection _connection;
        private string _connectedUrl;

        public event Action<LobbyState> LobbyStateChanged;
        /// <summary>(round number, board)</summary>
        public event Action<int, RoundSetup> RoundStarted;
        /// <summary>(round number, seconds remaining)</summary>
        public event Action<int, float> TimerUpdated;
        /// <summary>(round number, player name, move count)</summary>
        public event Action<int, string, int> NewBestSolution;
        /// <summary>(reason)</summary>
        public event Action<string> SolutionRejected;
        /// <summary>(round number, winner name or null, move count)</summary>
        public event Action<int, string, int> RoundEnded;
        /// <summary>Human-readable connection status for the UI.</summary>
        public event Action<string> StatusChanged;

        public bool IsInLobby { get; private set; }
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
                if (_connection == null || _connection.State == HubConnectionState.Disconnected || _connectedUrl != _serverUrl)
                    await ConnectAsync();

                SetStatus("Joining " + lobbyCode + "...");
                await _connection.InvokeAsync(HubMethods.JoinLobby, playerName, lobbyCode);
                IsInLobby = true;
                SetStatus("In lobby " + lobbyCode.Trim().ToUpperInvariant());
            }
            catch (Exception e)
            {
                SetStatus("Join failed: " + Describe(e));
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

        private async Task ConnectAsync()
        {
            SetStatus("Connecting to " + _serverUrl + "...");
            HubConnection old = _connection;
            if (old != null)
                _ = old.DisposeAsync();

            IsInLobby = false;
            HubConnection connection = new HubConnectionBuilder().WithUrl(_serverUrl).Build();
            _connection = connection;
            _connectedUrl = _serverUrl;

            _connection.On<LobbyState>(HubMethods.LobbyState,
                state => Post(() => LobbyStateChanged?.Invoke(state)));
            _connection.On<int, byte[]>(HubMethods.RoundStarted, (round, board) =>
            {
                RoundSetup setup = BoardCodec.Decode(board);
                Post(() =>
                {
                    CurrentRound = round;
                    RoundStarted?.Invoke(round, setup);
                });
            });
            _connection.On<int, double>(HubMethods.TimerUpdated,
                (round, seconds) => Post(() => TimerUpdated?.Invoke(round, (float)seconds)));
            _connection.On<int, string, int>(HubMethods.NewBestSolution,
                (round, player, moves) => Post(() => NewBestSolution?.Invoke(round, player, moves)));
            _connection.On<string>(HubMethods.SolutionRejected,
                reason => Post(() => SolutionRejected?.Invoke(reason)));
            _connection.On<int, string, int>(HubMethods.RoundEnded,
                (round, winner, moves) => Post(() => RoundEnded?.Invoke(round, winner, moves)));

            _connection.Closed += error =>
            {
                Post(() =>
                {
                    // A replaced connection can report its closure late; only the current one matters.
                    if (_connection != connection)
                        return;
                    IsInLobby = false;
                    SetStatus(error == null ? "Disconnected" : "Disconnected: " + error.Message);
                });
                return Task.CompletedTask;
            };

            await connection.StartAsync();
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
            HubConnection connection = _connection;
            _connection = null;
            if (connection != null)
                _ = connection.DisposeAsync();
        }

        private void Post(Action action) => _mainThreadQueue.Enqueue(action);

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
