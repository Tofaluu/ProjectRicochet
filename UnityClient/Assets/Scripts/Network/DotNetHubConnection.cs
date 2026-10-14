#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ricochet.Client.Network
{
    /// <summary>The C# SignalR client (desktop builds and the Editor).</summary>
    internal sealed class DotNetHubConnection : IHubConnection
    {
        private readonly HubConnection _connection;

        public DotNetHubConnection(string url)
        {
            // Retries after 0, 2, 10 and 30 s, well within the server's reconnect grace period.
            _connection = new HubConnectionBuilder().WithUrl(url).WithAutomaticReconnect().Build();
            _connection.Reconnecting += _ =>
            {
                Reconnecting?.Invoke();
                return Task.CompletedTask;
            };
            _connection.Reconnected += _ =>
            {
                Reconnected?.Invoke();
                return Task.CompletedTask;
            };
            // Also raised when the automatic reconnect gives up.
            _connection.Closed += error =>
            {
                Closed?.Invoke(error?.Message);
                return Task.CompletedTask;
            };
        }

        public bool IsDisconnected => _connection.State == HubConnectionState.Disconnected;

        public event Action Reconnecting;
        public event Action Reconnected;
        public event Action<string> Closed;

        public void On<T1>(string method, Action<T1> handler) => _connection.On(method, handler);
        public void On<T1, T2>(string method, Action<T1, T2> handler) => _connection.On(method, handler);
        public void On<T1, T2, T3>(string method, Action<T1, T2, T3> handler) => _connection.On(method, handler);

        public Task StartAsync() => _connection.StartAsync();
        public Task InvokeAsync(string method, params object[] args) => _connection.InvokeCoreAsync(method, args);
        public Task<T> InvokeAsync<T>(string method, params object[] args) => _connection.InvokeCoreAsync<T>(method, args);

        public void Dispose() => _ = _connection.DisposeAsync();
    }
}
#endif
