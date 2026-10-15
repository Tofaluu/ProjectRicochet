using System.Collections.Generic;
using Ricochet.Client.Board;
using Ricochet.Client.Game;
using Ricochet.Client.Scratchpad;
using Ricochet.Client.Theme;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// The match history on the results (IMGUI, under the board overlay's card and toggles, until the UI pass):
    /// step through the match's boards and replay each player's best or the shortest solution on that board.
    /// Reads <see cref="MatchState.History"/>; added by <see cref="LobbyUI"/> at startup.
    /// </summary>
    public class MatchHistoryOverlay : MonoBehaviour
    {
        private const int Radius = 14;
        private const float Width = 340f;
        private const float Pad = 16f;
        private const float RowHeight = 30f;

        private MatchState _match;
        private ScratchpadController _scratchpad;
        private BoardOverlay _boardOverlay;
        private GameTheme _theme;
        private int _index = -1;

        private GUIStyle _cardStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _textStyle;
        private GUIStyle _buttonStyle;

        private GameTheme Colors => GameTheme.OrDefault(_theme);

        public void Init(MatchState match, ScratchpadController scratchpad, GameTheme theme)
        {
            _match = match;
            _scratchpad = scratchpad;
            _boardOverlay = scratchpad.GetComponent<BoardOverlay>();
            _theme = theme;
        }

        private void OnGUI()
        {
            if (_match == null || !_match.InLobby || _match.Phase != MatchPhase.Finished || _match.History.Count == 0)
            {
                _index = -1;
                return;
            }
            IReadOnlyList<HistoryRound> history = _match.History;
            // Start on the last board.
            if (_index < 0 || _index >= history.Count)
                _index = history.Count - 1;
            CreateStyles();

            HistoryRound round = history[_index];
            var rows = new List<(string label, ReplaySegment segment)>();
            foreach (HistorySolution solution in round.Solutions)
            {
                string who = solution.Name == _match.MyName ? "You" : solution.Name;
                string won = solution.Name == round.WinnerName ? "  (won)" : "";
                rows.Add(($"{who}  ·  {solution.Moves.Count} moves{won}", new ReplaySegment(who, solution.Moves, 0.6f)));
            }
            if (round.Shortest != null)
                rows.Add(($"Shortest possible  ·  {round.Shortest.Count} moves",
                    new ReplaySegment("Shortest possible", round.Shortest, 0.6f, isShortest: true)));

            float scale = BoardOverlay.UiScale;
            Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float top = (_boardOverlay != null ? _boardOverlay.LeftColumnBottom : 16f) + 12f;
            float inner = Width - 2f * Pad;
            float height = Pad + 22f + RowHeight + 6f + 24f + rows.Count * (RowHeight + 6f) + 22f + Pad;
            var card = new Rect(16f, top, Width, height);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = Tint(Colors.Panel);
            GUI.Box(card, GUIContent.none, _cardStyle);

            float x = card.x + Pad, y = card.y + Pad;
            GUI.Label(new Rect(x, y, inner, 22f), "MATCH HISTORY", _headerStyle);
            y += 22f;

            // < Board 2 of 7 >
            GUI.backgroundColor = Tint(Colors.Input);
            GUI.enabled = _index > 0;
            if (GUI.Button(new Rect(x, y, 40f, RowHeight), "<", _buttonStyle))
                Select(_index - 1);
            GUI.enabled = _index < history.Count - 1;
            if (GUI.Button(new Rect(x + inner - 40f, y, 40f, RowHeight), ">", _buttonStyle))
                Select(_index + 1);
            GUI.enabled = true;
            _textStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x + 40f, y, inner - 80f, RowHeight), $"Board {round.MatchRound} of {history.Count}", _textStyle);
            y += RowHeight + 6f;

            _textStyle.alignment = TextAnchor.MiddleLeft;
            string result = round.WinnerName == null ? "Nobody solved it"
                : round.WinnerName == _match.MyName ? "You won this board" : $"{round.WinnerName} won this board";
            GUI.Label(new Rect(x, y, inner, 24f), result, _textStyle);
            y += 24f;

            foreach ((string label, ReplaySegment segment) in rows)
            {
                GUI.backgroundColor = Tint(segment.IsShortest ? Colors.FrameColor(BoardMode.ShortestReplay, round.Board.Target) : Colors.Input);
                if (GUI.Button(new Rect(x, y, inner, RowHeight), label, _buttonStyle))
                    Play(round, segment);
                y += RowHeight + 6f;
            }

            GUI.backgroundColor = previous;
            GUI.Label(new Rect(x, y, inner, 22f), "Click one to replay it on its board.", _headerStyle);
            GUI.matrix = matrix;
        }

        private void Select(int index)
        {
            _index = index;
            _scratchpad.ShowHistoryBoard(_match.History[index].Board);
        }

        private void Play(HistoryRound round, ReplaySegment segment)
        {
            if (_scratchpad.Session.Round != round.Board)
                _scratchpad.ShowHistoryBoard(round.Board);
            _scratchpad.ShowSolution(segment);
        }

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
                font = Colors.BoldFont, fontSize = 15, clipping = TextClipping.Overflow,
                normal = { textColor = Colors.PanelText },
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = Colors.BoldFont, fontSize = 14, alignment = TextAnchor.MiddleCenter,
                normal = { background = rounded, textColor = Colors.PanelText },
                hover = { background = rounded, textColor = Colors.PanelText },
                active = { background = rounded, textColor = Colors.PanelText },
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
