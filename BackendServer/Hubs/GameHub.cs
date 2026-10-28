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
        private readonly DailyService _daily;

        public GameHub(LobbyManager lobbies, DailyService daily)
        {
            _lobbies = lobbies;
            _daily = daily;
        }

        private static void CheckVersion(int protocolVersion)
        {
            if (protocolVersion < HubMethods.ProtocolVersion)
                throw new HubException("Your game is out of date. Please download the latest version.");
            if (protocolVersion > HubMethods.ProtocolVersion)
                throw new HubException("The server is running an older version than your game. Ask the host to update it.");
        }

        [HubMethodName(HubMethods.JoinLobby)]
        public Task<string> JoinLobby(string playerName, string lobbyCode, int protocolVersion, string playerId)
        {
            CheckVersion(protocolVersion);
            return _lobbies.JoinAsync(Context.ConnectionId, playerName, lobbyCode, playerId);
        }

        [HubMethodName(HubMethods.GetDaily)]
        public Task<DailyPuzzleInfo> GetDaily(string guestId, string name, int protocolVersion)
        {
            CheckVersion(protocolVersion);
            return _daily.GetDailyAsync(guestId, name);
        }

        [HubMethodName(HubMethods.SubmitDaily)]
        public Task<DailyResult> SubmitDaily(string guestId, int number, byte[] moves) =>
            _daily.SubmitAsync(guestId, number, moves);

        [HubMethodName(HubMethods.GetDailyLeaderboard)]
        public Task<DailyLeaderboard> GetDailyLeaderboard(string guestId, int number) =>
            _daily.GetLeaderboardAsync(guestId, number);

        [HubMethodName(HubMethods.GetDailyCalendar)]
        public Task<DailyCalendar> GetDailyCalendar(string guestId) => _daily.GetCalendarAsync(guestId);

        [HubMethodName(HubMethods.GetPastDaily)]
        public Task<DailyPastInfo> GetPastDaily(string guestId, int number) => _daily.GetPastAsync(guestId, number);

        [HubMethodName(HubMethods.SubmitPastDaily)]
        public Task<DailyPastResult> SubmitPastDaily(string guestId, int number, byte[] moves) =>
            _daily.SubmitPastAsync(guestId, number, moves);

        [HubMethodName(HubMethods.ShowDailySolution)]
        public Task<DailySolution> ShowDailySolution(string guestId, int number) => _daily.ShowSolutionAsync(guestId, number);

        [HubMethodName(HubMethods.GetStreakLeaderboard)]
        public Task<StreakLeaderboard> GetStreakLeaderboard(string guestId) => _daily.GetStreakLeaderboardAsync(guestId);

        [HubMethodName(HubMethods.LeaveLobby)]
        public Task LeaveLobby() => _lobbies.LeaveAsync(Context.ConnectionId);

        [HubMethodName(HubMethods.StartMatch)]
        public Task StartMatch() => _lobbies.StartMatchAsync(Context.ConnectionId);

        [HubMethodName(HubMethods.UpdateSettings)]
        public Task UpdateSettings(MatchSettings settings) =>
            _lobbies.UpdateSettingsAsync(Context.ConnectionId, settings);

        [HubMethodName(HubMethods.SubmitSolution)]
        public Task SubmitSolution(int roundNumber, byte[] moves) =>
            _lobbies.SubmitAsync(Context.ConnectionId, roundNumber, moves);

        [HubMethodName(HubMethods.RecordSolution)]
        public Task RecordSolution(int roundNumber, byte[] moves) =>
            _lobbies.RecordAsync(Context.ConnectionId, roundNumber, moves);

        [HubMethodName(HubMethods.SetDone)]
        public Task SetDone(int roundNumber, bool done) =>
            _lobbies.SetDoneAsync(Context.ConnectionId, roundNumber, done);

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Unlike LeaveLobby, the seat is kept for a while in case they reconnect.
            await _lobbies.DisconnectAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
