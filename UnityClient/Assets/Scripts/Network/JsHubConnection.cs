using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// Browser builds: the JavaScript SignalR client (<c>signalr.min.js</c>, loaded by the WebGL template),
    /// driven through <c>Plugins/WebGL/RicochetHub.jslib</c>. Arguments and results cross as JSON in the
    /// SignalR JSON protocol's shape (camelCase, enums as numbers, byte[] as base64). JavaScript calls back
    /// on the main thread. Only usable in a WebGL player, not the Editor.
    /// </summary>
    internal sealed class JsHubConnection : IHubConnection
    {
        private const int StateReconnecting = 1, StateReconnected = 2, StateClosed = 3;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };
        private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        private static readonly Dictionary<int, JsHubConnection> Connections = new Dictionary<int, JsHubConnection>();
        private static readonly Dictionary<int, TaskCompletionSource<string>> PendingCalls =
            new Dictionary<int, TaskCompletionSource<string>>();
        private static bool _initialized;
        private static int _lastCallId;

        private readonly int _handle;
        private readonly Dictionary<string, Action<JArray>> _handlers = new Dictionary<string, Action<JArray>>();
        private bool _started;
        private bool _closed;

        public JsHubConnection(string url)
        {
            if (!_initialized)
            {
                RicochetHub_Init(OnMessage, OnCallDone, OnState);
                _initialized = true;
            }
            _handle = RicochetHub_Create(url);
            Connections[_handle] = this;
        }

        public bool IsDisconnected => !_started || _closed;

        public event Action Reconnecting;
        public event Action Reconnected;
        public event Action<string> Closed;

        public void On<T1>(string method, Action<T1> handler) =>
            Register(method, args => handler(Arg<T1>(args, 0)));

        public void On<T1, T2>(string method, Action<T1, T2> handler) =>
            Register(method, args => handler(Arg<T1>(args, 0), Arg<T2>(args, 1)));

        public void On<T1, T2, T3>(string method, Action<T1, T2, T3> handler) =>
            Register(method, args => handler(Arg<T1>(args, 0), Arg<T2>(args, 1), Arg<T3>(args, 2)));

        public async Task StartAsync()
        {
            await Call(id => RicochetHub_Start(_handle, id));
            _started = true;
        }

        public Task InvokeAsync(string method, params object[] args) =>
            Call(id => RicochetHub_Invoke(_handle, id, method, JsonConvert.SerializeObject(args, Settings)));

        public async Task<T> InvokeAsync<T>(string method, params object[] args)
        {
            string json = await Call(id => RicochetHub_Invoke(_handle, id, method, JsonConvert.SerializeObject(args, Settings)));
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public void Dispose()
        {
            Connections.Remove(_handle);
            RicochetHub_Stop(_handle);
        }

        private void Register(string method, Action<JArray> handler)
        {
            _handlers[method] = handler;
            RicochetHub_On(_handle, method);
        }

        private static T Arg<T>(JArray args, int index) =>
            index < args.Count ? args[index].ToObject<T>(Serializer) : default;

        private static Task<string> Call(Action<int> send)
        {
            var call = new TaskCompletionSource<string>();
            int id = ++_lastCallId;
            PendingCalls[id] = call;
            send(id);
            return call.Task;
        }

        // ---- Callbacks from JavaScript (exceptions must not escape into the browser) ----

        private delegate void MessageCallback(int handle, string method, string argsJson);
        private delegate void CallDoneCallback(int callId, int ok, string resultOrError);
        private delegate void StateCallback(int handle, int state, string error);

        [MonoPInvokeCallback(typeof(MessageCallback))]
        private static void OnMessage(int handle, string method, string argsJson)
        {
            try
            {
                if (Connections.TryGetValue(handle, out JsHubConnection connection) &&
                    connection._handlers.TryGetValue(method, out Action<JArray> handler))
                    handler(JArray.Parse(argsJson));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(CallDoneCallback))]
        private static void OnCallDone(int callId, int ok, string resultOrError)
        {
            if (!PendingCalls.TryGetValue(callId, out TaskCompletionSource<string> call))
                return;
            PendingCalls.Remove(callId);
            try
            {
                if (ok != 0)
                    call.SetResult(resultOrError);
                else
                    call.SetException(new Exception(resultOrError));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(StateCallback))]
        private static void OnState(int handle, int state, string error)
        {
            if (!Connections.TryGetValue(handle, out JsHubConnection connection))
                return;
            try
            {
                switch (state)
                {
                    case StateReconnecting:
                        connection.Reconnecting?.Invoke();
                        break;
                    case StateReconnected:
                        connection.Reconnected?.Invoke();
                        break;
                    case StateClosed:
                        connection._closed = true;
                        Connections.Remove(handle);
                        connection.Closed?.Invoke(error);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ---- RicochetHub.jslib ----

        [DllImport("__Internal")]
        private static extern void RicochetHub_Init(MessageCallback onMessage, CallDoneCallback onCallDone, StateCallback onState);
        [DllImport("__Internal")]
        private static extern int RicochetHub_Create(string url);
        [DllImport("__Internal")]
        private static extern void RicochetHub_On(int handle, string method);
        [DllImport("__Internal")]
        private static extern void RicochetHub_Start(int handle, int callId);
        [DllImport("__Internal")]
        private static extern void RicochetHub_Invoke(int handle, int callId, string method, string argsJson);
        [DllImport("__Internal")]
        private static extern void RicochetHub_Stop(int handle);
    }
}
