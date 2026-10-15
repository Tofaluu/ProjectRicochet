using System;
using System.Threading.Tasks;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// The SignalR connection <see cref="NetworkManager"/> talks through. Desktop builds (and the
    /// Editor) use the C# client (<see cref="DotNetHubConnection"/>); browser builds can't (no threads
    /// or raw sockets there) and use the JavaScript client instead (<see cref="JsHubConnection"/>).
    /// Handlers and events may be raised on any thread.
    /// </summary>
    internal interface IHubConnection : IDisposable
    {
        bool IsDisconnected { get; }

        /// <summary>The connection dropped; automatic reconnect is under way.</summary>
        event Action Reconnecting;
        /// <summary>Reconnected with a new connection id.</summary>
        event Action Reconnected;
        /// <summary>Closed for good (error message, or null when stopped normally).</summary>
        event Action<string> Closed;

        void On<T1>(string method, Action<T1> handler);
        void On<T1, T2>(string method, Action<T1, T2> handler);
        void On<T1, T2, T3>(string method, Action<T1, T2, T3> handler);
        void On<T1, T2, T3, T4>(string method, Action<T1, T2, T3, T4> handler);

        Task StartAsync();
        Task InvokeAsync(string method, params object[] args);
        Task<T> InvokeAsync<T>(string method, params object[] args);
    }
}
