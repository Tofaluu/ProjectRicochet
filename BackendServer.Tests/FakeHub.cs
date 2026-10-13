using BackendServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BackendServer.Tests;

public sealed record Sent(string Method, object?[] Args);

/// <summary>
/// Stands in for SignalR: tracks group membership and records every message per connection, so tests
/// can read what each client would have received.
/// </summary>
public sealed class FakeHub : IHubContext<GameHub>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _groups = new();
    private readonly Dictionary<string, List<Sent>> _inbox = new();

    public FakeHub()
    {
        Clients = new FakeClients(this);
        Groups = new FakeGroups(this);
    }

    public IHubClients Clients { get; }
    public IGroupManager Groups { get; }

    public IReadOnlyList<Sent> Inbox(string connectionId)
    {
        lock (_gate)
            return _inbox.TryGetValue(connectionId, out List<Sent>? sent) ? sent.ToList() : new List<Sent>();
    }

    private void Deliver(IEnumerable<string> connectionIds, string method, object?[] args)
    {
        lock (_gate)
        {
            foreach (string id in connectionIds.ToList())
            {
                if (!_inbox.TryGetValue(id, out List<Sent>? sent))
                    _inbox[id] = sent = new List<Sent>();
                sent.Add(new Sent(method, args));
            }
        }
    }

    private IEnumerable<string> Members(string group)
    {
        lock (_gate)
            return _groups.TryGetValue(group, out HashSet<string>? members) ? members.ToList() : new List<string>();
    }

    private sealed class Proxy : IClientProxy
    {
        private readonly FakeHub _hub;
        private readonly Func<IEnumerable<string>> _targets;

        public Proxy(FakeHub hub, Func<IEnumerable<string>> targets)
        {
            _hub = hub;
            _targets = targets;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _hub.Deliver(_targets(), method, args);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClients : IHubClients
    {
        private readonly FakeHub _hub;
        public FakeClients(FakeHub hub) => _hub = hub;

        public IClientProxy Client(string connectionId) => new Proxy(_hub, () => new[] { connectionId });
        public IClientProxy Group(string groupName) => new Proxy(_hub, () => _hub.Members(groupName));

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeGroups : IGroupManager
    {
        private readonly FakeHub _hub;
        public FakeGroups(FakeHub hub) => _hub = hub;

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            lock (_hub._gate)
            {
                if (!_hub._groups.TryGetValue(groupName, out HashSet<string>? members))
                    _hub._groups[groupName] = members = new HashSet<string>();
                members.Add(connectionId);
            }
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            lock (_hub._gate)
            {
                if (_hub._groups.TryGetValue(groupName, out HashSet<string>? members))
                    members.Remove(connectionId);
            }
            return Task.CompletedTask;
        }
    }
}
