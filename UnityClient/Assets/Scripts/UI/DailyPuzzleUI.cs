using System;
using System.Collections.Generic;
using System.Linq;
using Ricochet.Client.Board;
using Ricochet.Client.Game;
using Ricochet.Client.Network;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// The daily puzzle from the join screen: the Daily button loads today's board (the server starts the player's
    /// clock), reaching the target sends the solution if it beats their best (else the "not shorter" sound), and the
    /// board goes back to the start so they can keep improving until midnight Eastern. An IMGUI card under the board
    /// overlay (until the UI pass) shows the clock, their best, streak, rank, the top of the leaderboard and "Copy
    /// result". State lives in <see cref="DailyState"/>; added by <see cref="LobbyUI"/>. Joining a lobby ends it.
    /// </summary>
    public class DailyPuzzleUI : MonoBehaviour
    {
        private const int Radius = 14;
        private const float Width = 340f;
        private const float Pad = 16f;
        private const float RowHeight = 30f;
        private const float SolvedResetDelay = 0.9f;
        private const int LeaderboardRows = 5;
        private const string ShareLink = "https://ricochet.thomasliu.dev";

        private readonly DailyState _daily = new DailyState();
        private NetworkManager _network;
        private ScratchpadController _scratchpad;
        private BoardOverlay _boardOverlay;
        private GameTheme _theme;
        private Func<string> _playerName;
        private Text _buttonLabel;
        private string _notice;
        private float _noticeUntil;

        private GUIStyle _cardStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _textStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _buttonStyle;

        private GameTheme Colors => GameTheme.OrDefault(_theme);
        private bool OnDailyBoard => _daily.Stage == DailyStage.Playing && _scratchpad.Session.Round == _daily.Round;

        public void Init(NetworkManager network, ScratchpadController scratchpad, GameTheme theme, Button dailyButton,
            Func<string> playerName)
        {
            _network = network;
            _scratchpad = scratchpad;
            _boardOverlay = scratchpad.GetComponent<BoardOverlay>();
            _theme = theme;
            _playerName = playerName;
            _buttonLabel = dailyButton.GetComponentInChildren<Text>(true);
            dailyButton.onClick.AddListener(Open);
            _scratchpad.Solved += OnSolved;
        }

        private void OnDestroy()
        {
            if (_scratchpad != null)
                _scratchpad.Solved -= OnSolved;
        }

        // ---- Flow ----

        private async void Open()
        {
            if (_daily.Stage == DailyStage.Loading)
                return;
            // A clicked button stays selected, and the arrow keys would then move between buttons, not robots.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            _daily.BeginLoading();
            try
            {
                DailyPuzzleInfo info = await _network.GetDailyAsync(GuestIdentity.Id, _playerName());
                if (_daily.Stage != DailyStage.Loading || _network.IsInLobby)
                    return; // closed, or joined a lobby, meanwhile
                _daily.OnLoaded(info, Time.time);
                _scratchpad.ShowDaily(_daily.Round);
                if (_daily.HasSolved)
                    RefreshLeaderboard();
            }
            catch (Exception e)
            {
                _daily.OnFailed(NetworkManager.DescribeError(e));
            }
        }

        private void OnSolved(int moves)
        {
            if (!OnDailyBoard)
                return;
            Move[] solution = new List<Move>(_scratchpad.Session.Moves).ToArray();
            if (_daily.IsImprovement(moves))
                Submit(solution);
            else
                _scratchpad.View.MarkNotShorter();
            // Back to the start to look for a shorter one (a longer solution is a dead end).
            _scratchpad.ResetAfter(SolvedResetDelay);
        }

        private async void Submit(Move[] solution)
        {
            _daily.OnSubmitting();
            try
            {
                DailyResult result = await _network.SubmitDailyAsync(GuestIdentity.Id, _daily.Number, solution);
                _daily.OnSubmitted(result);
                RefreshLeaderboard();
            }
            catch (Exception e)
            {
                string message = NetworkManager.DescribeError(e);
                if (message.Contains("is over"))
                {
                    // Midnight passed while playing: today's puzzle instead.
                    Show("A new daily puzzle is out!");
                    Open();
                    return;
                }
                _daily.OnFailed(message);
            }
        }

        private async void RefreshLeaderboard()
        {
            try
            {
                _daily.OnLeaderboard(await _network.GetDailyLeaderboardAsync(GuestIdentity.Id, _daily.Number));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Daily] Leaderboard failed: " + NetworkManager.DescribeError(e));
            }
        }

        private void Close(bool backToPractice)
        {
            bool wasOnBoard = OnDailyBoard;
            _daily.Close();
            if (wasOnBoard)
                _scratchpad.Session.Title = null;
            if (backToPractice)
                _scratchpad.PlayOffline();
        }

        private void CopyResult()
        {
            Clipboard.Copy(_daily.ShareText(ShareLink));
            Show("Copied! Paste it anywhere.");
        }

        private void Show(string notice)
        {
            _notice = notice;
            _noticeUntil = Time.time + 2.5f;
        }

        private void Update()
        {
            if (_network == null)
                return;
            // A lobby takes the board over.
            if (_daily.IsActive && _network.IsInLobby)
                Close(backToPractice: false);
            if (_buttonLabel != null)
                _buttonLabel.text = _daily.Number > 0 && _daily.Streak > 0
                    ? $"Daily Puzzle #{_daily.Number}  ·  {_daily.Streak}-day streak"
                    : _daily.Number > 0 ? $"Daily Puzzle #{_daily.Number}" : "Daily Puzzle";
            if (!OnDailyBoard)
                return;
            _scratchpad.Session.Title = _daily.HasSolved
                ? $"Daily #{_daily.Number}  ·  best {_daily.BestMoves}"
                : $"Daily #{_daily.Number}  ·  {DailyState.Clock(_daily.Elapsed(Time.time))}";
            if (_daily.UntilNext(Time.time) <= 0f && _daily.Stage == DailyStage.Playing)
            {
                Show("A new daily puzzle is out!");
                Open();
            }
        }

        // ---- Card ----

        private void OnGUI()
        {
            if (_network == null || !_daily.IsActive || _network.IsInLobby)
                return;
            CreateStyles();

            float scale = BoardOverlay.UiScale;
            Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float inner = Width - 2f * Pad;
            var lines = new List<(string text, GUIStyle style)>();
            float now = Time.time;

            string header = _daily.Number > 0 ? $"DAILY PUZZLE #{_daily.Number}  ·  {_daily.Weekday.ToUpperInvariant()}" : "DAILY PUZZLE";
            switch (_daily.Stage)
            {
                case DailyStage.Loading:
                    lines.Add(("Loading today's puzzle...", _textStyle));
                    break;
                case DailyStage.Error:
                    lines.Add((_daily.Error, _textStyle));
                    break;
                default:
                    if (!_daily.HasSolved)
                    {
                        lines.Add(($"Time  {DailyState.Clock(_daily.Elapsed(now))}", _textStyle));
                        lines.Add(("Solve it in as few moves as you can. Your time counts from when you first opened it, " +
                            "and you can keep improving until midnight (Eastern).", _smallStyle));
                    }
                    else
                    {
                        string optimal = _daily.BestMoves <= _daily.ShortestLength ? "the best possible!" : $"best possible {_daily.ShortestLength}";
                        lines.Add(($"Your best: {_daily.BestMoves} moves ({optimal})  ·  {DailyState.Clock(_daily.BestSeconds)}", _textStyle));
                        string rank = _daily.Rank > 0 ? $"#{_daily.Rank} of {_daily.Players} today" : "";
                        string streak = _daily.Streak > 0 ? $"{_daily.Streak}-day streak" : "";
                        lines.Add((string.Join("  ·  ", new[] { rank, streak }.Where(s => s.Length > 0)), _textStyle));
                        if (_daily.BestMoves > _daily.ShortestLength)
                            lines.Add(("Keep going: a shorter solution beats your best.", _smallStyle));
                        AddLeaderboard(lines);
                    }
                    if (_daily.Submitting)
                        lines.Add(("Sending...", _smallStyle));
                    lines.Add(($"Next puzzle in {DailyState.Clock(_daily.UntilNext(now))}", _smallStyle));
                    break;
            }
            if (_notice != null && now < _noticeUntil)
                lines.Add((_notice, _smallStyle));

            float textHeight = 0f;
            foreach ((string text, GUIStyle style) in lines)
                textHeight += style.CalcHeight(new GUIContent(text), inner) + 4f;
            int buttons = _daily.Stage == DailyStage.Error ? 2 : _daily.HasSolved ? 2 : 1;
            float height = Pad + 22f + textHeight + 6f + buttons * (RowHeight + 6f) + Pad - 6f;

            float top = (_boardOverlay != null ? _boardOverlay.LeftColumnBottom : 16f) + 12f;
            var card = new Rect(16f, top, Width, height);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.Panel);
            GUI.Box(card, GUIContent.none, _cardStyle);

            float x = card.x + Pad, y = card.y + Pad;
            GUI.Label(new Rect(x, y, inner, 22f), header, _headerStyle);
            y += 22f;
            foreach ((string text, GUIStyle style) in lines)
            {
                float h = style.CalcHeight(new GUIContent(text), inner);
                GUI.Label(new Rect(x, y, inner, h), text, style);
                y += h + 4f;
            }
            y += 6f;

            GUI.backgroundColor = Tint(Colors.PrimaryButton);
            if (_daily.Stage == DailyStage.Error)
            {
                if (GUI.Button(new Rect(x, y, inner, RowHeight), "Try again", _buttonStyle))
                    Open();
                y += RowHeight + 6f;
            }
            else if (_daily.HasSolved)
            {
                if (GUI.Button(new Rect(x, y, inner, RowHeight), "Copy result", _buttonStyle))
                    CopyResult();
                y += RowHeight + 6f;
            }
            GUI.backgroundColor = Tint(Colors.Input);
            if (GUI.Button(new Rect(x, y, inner, RowHeight), "Back to practice", _buttonStyle))
                Close(backToPractice: true);

            GUI.backgroundColor = previous;
            GUI.matrix = matrix;
        }

        // The top few, plus the player's own line if they're further down.
        private void AddLeaderboard(List<(string text, GUIStyle style)> lines)
        {
            DailyLeaderboard board = _daily.Leaderboard;
            if (board == null || board.Top == null || board.Top.Length == 0)
                return;
            lines.Add(("TODAY'S LEADERBOARD", _headerStyle));
            for (int i = 0; i < board.Top.Length && i < LeaderboardRows; i++)
                lines.Add((Row(i + 1, board.Top[i]), _smallStyle));
            if (board.MyRank > LeaderboardRows)
                lines.Add(($"{board.MyRank}.  You  ·  {_daily.BestMoves} moves  ·  {DailyState.Clock(_daily.BestSeconds)}", _smallStyle));
        }

        private static string Row(int place, DailyLeaderboardEntry e) =>
            $"{place}.  {(e.IsMe ? "You" : e.Name)}  ·  {e.Moves} moves  ·  {DailyState.Clock(e.Seconds)}";

        private void CreateStyles()
        {
            if (_cardStyle != null)
                return;
            Texture2D rounded = ShapeSprites.RoundedTexture(Radius);
            var border = new RectOffset(Radius, Radius, Radius, Radius);
            _cardStyle = new GUIStyle(GUI.skin.box) { normal = { background = rounded }, border = border };
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                font = Colors.BoldFont, fontSize = 13, clipping = TextClipping.Overflow,
                normal = { textColor = Colors.MutedText },
            };
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                font = Colors.BoldFont, fontSize = 15, wordWrap = true,
                normal = { textColor = Colors.PanelText },
            };
            _smallStyle = new GUIStyle(_textStyle) { font = Colors.Font, fontSize = 14, normal = { textColor = Colors.MutedText } };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = Colors.BoldFont, fontSize = 14, alignment = TextAnchor.MiddleCenter,
                normal = { background = rounded, textColor = Colors.ButtonText },
                hover = { background = rounded, textColor = Colors.ButtonText },
                active = { background = rounded, textColor = Colors.ButtonText },
                border = border,
            };
        }

        // See BoardOverlay.Tint: browser builds gamma-correct IMGUI tints once more.
        private static Color Tint(Color color)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                return color.linear;
#endif
            return color;
        }
    }
}
