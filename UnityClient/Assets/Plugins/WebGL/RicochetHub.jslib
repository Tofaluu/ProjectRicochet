// Browser builds: bridges JsHubConnection.cs to the JavaScript SignalR client (window.signalR, from
// signalr.min.js in the WebGL template). Strings go to C# as UTF-8 buffers freed after each call.
var RicochetHub = {
  $hub__deps: ['$stringToUTF8', '$lengthBytesUTF8', 'malloc', 'free'],
  $hub: {
    connections: {},
    nextHandle: 1,
    callbacks: null,

    str: function (s) {
      if (s === null || s === undefined) return 0;
      var size = lengthBytesUTF8(s) + 1;
      var ptr = _malloc(size);
      stringToUTF8(s, ptr, size);
      return ptr;
    },

    describe: function (e) {
      return e && e.message ? e.message : String(e);
    },

    message: function (handle, method, args) {
      var m = hub.str(method), a = hub.str(JSON.stringify(args));
      {{{ makeDynCall('viii', 'hub.callbacks.message') }}}(handle, m, a);
      _free(m); _free(a);
    },

    callDone: function (callId, ok, value) {
      var v = hub.str(value);
      {{{ makeDynCall('viii', 'hub.callbacks.callDone') }}}(callId, ok, v);
      if (v) _free(v);
    },

    state: function (handle, state, error) {
      var e = hub.str(error);
      {{{ makeDynCall('viii', 'hub.callbacks.state') }}}(handle, state, e);
      if (e) _free(e);
    }
  },

  RicochetHub_Init: function (onMessage, onCallDone, onState) {
    hub.callbacks = { message: onMessage, callDone: onCallDone, state: onState };
  },

  RicochetHub_Create: function (url) {
    var handle = hub.nextHandle++;
    // Same retry schedule as the C# client: 0, 2, 10 and 30 s.
    var connection = new signalR.HubConnectionBuilder()
      .withUrl(UTF8ToString(url))
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    connection.onreconnecting(function () { hub.state(handle, 1, null); });
    connection.onreconnected(function () { hub.state(handle, 2, null); });
    connection.onclose(function (e) {
      delete hub.connections[handle];
      hub.state(handle, 3, e ? hub.describe(e) : null);
    });
    hub.connections[handle] = connection;
    return handle;
  },

  RicochetHub_On: function (handle, method) {
    var name = UTF8ToString(method);
    hub.connections[handle].on(name, function () {
      hub.message(handle, name, Array.prototype.slice.call(arguments));
    });
  },

  RicochetHub_Start: function (handle, callId) {
    hub.connections[handle].start().then(
      function () { hub.callDone(callId, 1, 'null'); },
      function (e) { hub.callDone(callId, 0, hub.describe(e)); });
  },

  RicochetHub_Invoke: function (handle, callId, method, argsJson) {
    var connection = hub.connections[handle];
    if (!connection) {
      hub.callDone(callId, 0, 'Not connected');
      return;
    }
    var args = [UTF8ToString(method)].concat(JSON.parse(UTF8ToString(argsJson)));
    connection.invoke.apply(connection, args).then(
      function (result) { hub.callDone(callId, 1, JSON.stringify(result === undefined ? null : result)); },
      function (e) { hub.callDone(callId, 0, hub.describe(e)); });
  },

  RicochetHub_Stop: function (handle) {
    var connection = hub.connections[handle];
    if (connection) {
      delete hub.connections[handle];
      connection.stop();
    }
  }
};

autoAddDeps(RicochetHub, '$hub');
mergeInto(LibraryManager.library, RicochetHub);
