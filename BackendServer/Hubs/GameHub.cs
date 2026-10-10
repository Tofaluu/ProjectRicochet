using BackendServer.Services;
using Microsoft.AspNetCore.SignalR;
using SharedLogic;

namespace BackendServer.Hubs
{
    /// <summary>
    /// SignalR endpoint. Hub instances only live for one call, so all state and the
    /// round timer live in <see cref="LobbyManager"/>.
    /// </summary>
    public class GameHub : Hub
    {
        private readonly LobbyManager _lobbies;

        public GameHub(LobbyManager lobbies)
        {
            _lobbies = lobbies;
        }

        [HubMethodName(HubMethods.JoinLobby)]
        public Task<string> JoinLobby(string playerName, string lobbyCode) =>
            _lobbies.JoinAsync(Context.ConnectionId, playerName, lobbyCode);

        [HubMethodName(HubMethods.LeaveLobby)]
        public Task LeaveLobby() => _lobbies.LeaveAsync(Context.ConnectionId);

        [HubMethodName(HubMethods.SubmitSolution)]
        public Task SubmitSolution(int roundNumber, byte[] moves) =>
            _lobbies.SubmitAsync(Context.ConnectionId, roundNumber, moves);

        [HubMethodName(HubMethods.SetDone)]
        public Task SetDone(int roundNumber, bool done) =>
            _lobbies.SetDoneAsync(Context.ConnectionId, roundNumber, done);

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _lobbies.LeaveAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
