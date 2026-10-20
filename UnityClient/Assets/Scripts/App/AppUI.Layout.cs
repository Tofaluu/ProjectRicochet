using Ricochet.Client.Board;
using Ricochet.Client.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ricochet.Client.App
{
    // The layout for the screen's shape, picked every frame from its size in CSS pixels (Viewport):
    //  - Desktop: landscape screens (desktops, laptops, tablets held sideways) - the UXML's own layout, scaled with the
    //    screen exactly as before.
    //  - Compact: upright screens (phones, tablets held upright) and narrow windows - top bar, the board with its controls,
    //    the screen's panel below it, the nav as a bottom tab bar (.app--compact in App.uss), sized in CSS pixels so text
    //    and buttons are finger-sized. Compact Home hides the board so the cards get the room.
    //  - Rotate: a phone held sideways is too short for either, so it asks to be turned upright.
    // In the compact layout the side panel is a sheet over the board: a handle with a one-line summary sits under the
    // board, and tapping or dragging it slides the panel up over the board (or back down). Menu screens (Play, Lobby)
    // open with it up, play screens (Practice, Daily, Match) with it down; Home has no board, just the panel.
    public partial class AppUI
    {
        private enum Layout { Desktop, Compact, Rotate }

        private const float CompactBelowWidth = 760f;   // CSS px: narrower landscape windows use the compact layout too
        private const float RotateBelowHeight = 500f;   // CSS px: landscape this short (a phone) asks to be turned
        private const float CompactDesignWidth = 430f;  // CSS px: wider compact screens (tablets) scale up a little...
        private const float CompactMaxZoom = 1.25f;     // ...up to this
        private const float SheetHeadHeight = 48f;      // the sheet's handle + summary row
        private const float SheetEase = 14f;            // how fast the sheet slides (per second, exponential)
        private const float MediumBelowWidth = 1400f;   // panel units: a narrower desktop layout (tablets held sideways)

        private Layout _layout = Layout.Desktop;
        private PanelSettings _panelSettings;
        private VisualElement _app, _nav;
        private Button _topbarSettings;
        private Label _topbarAvatar;
        private int _cameraMask = ~0;
        private bool _boardCulled;
        private bool _medium, _shortLabels;
        private ScrollerVisibility _desktopScroller;

        private VisualElement _stage, _stageHead, _boardControls, _sheetHead;
        private Label _sheetSummary, _sheetToggle;
        private float _sheetT = 1f, _sheetTarget = 1f, _sheetRange = 1f; // 0 = down (board shown), 1 = up (panel shown)
        private Screen? _sheetScreen;
        private bool _sheetDragging, _sheetMoved, _wasReplaying;
        private float _dragStartY, _dragStartT;

        private bool Compact => _layout == Layout.Compact;

        // The compact sheet is (partly) up over the board: the board's tip and countdown layer stay hidden.
        private bool SheetCoversBoard => Compact && _sheetT > 0.05f;

        // A touch screen (phone, tablet): taps instead of clicks, no keyboard hints. Read once per frame in ApplyLayout.
        private bool Touch { get; set; }

        // The board isn't on screen: compact Home, or the rotate notice.
        private bool BoardHidden => _layout == Layout.Rotate || (_layout == Layout.Compact && Current == Screen.Home);

        private void InitLayout()
        {
            _app = _root.Q("app");
            _nav = _root.Q("nav");
            _topbarSettings = Click("topbar-settings", () => TogglePopover(_settings));
            _topbarAvatar = _root.Q<Label>("topbar-avatar");
            _topbarAvatar.RegisterCallback<ClickEvent>(_ => TogglePopover(_profilePopover));
            _desktopScroller = _side.verticalScrollerVisibility;

            _stage = _root.Q("stage");
            _stageHead = _stage.Q(className: "stage-head");
            _boardControls = _root.Q("board-controls");
            _sheetHead = _root.Q("sheet-head");
            _sheetSummary = _root.Q<Label>("sheet-summary");
            _sheetToggle = _root.Q<Label>("sheet-toggle");
            _sheetHead.RegisterCallback<PointerDownEvent>(OnSheetDown);
            _sheetHead.RegisterCallback<PointerMoveEvent>(OnSheetMove);
            _sheetHead.RegisterCallback<PointerUpEvent>(OnSheetUp);
            _sheetHead.RegisterCallback<PointerCaptureOutEvent>(_ => _sheetDragging = false);
            ApplyLayoutTexts();
        }

        private void ApplyLayout()
        {
            float ratio = Viewport.PixelRatio;
            Touch = Viewport.TouchPrimary;
            float width = UnityEngine.Screen.width / ratio, height = UnityEngine.Screen.height / ratio;
            Layout layout = width >= height
                ? (height < RotateBelowHeight && width < 1000f ? Layout.Rotate : width < CompactBelowWidth ? Layout.Compact : Layout.Desktop)
                : Layout.Compact;

            if (layout == Layout.Desktop)
            {
                if (_panelSettings.scaleMode != PanelScaleMode.ScaleWithScreenSize)
                    _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            }
            else
            {
                float scale = ratio * Mathf.Clamp(width / CompactDesignWidth, 1f, CompactMaxZoom);
                if (_panelSettings.scaleMode != PanelScaleMode.ConstantPixelSize)
                    _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                if (Mathf.Abs(_panelSettings.scale - scale) > 0.001f)
                    _panelSettings.scale = scale;
            }

            if (layout != _layout)
            {
                _layout = layout;
                _app.EnableInClassList("app--compact", layout == Layout.Compact);
                _app.EnableInClassList("app--rotate", layout == Layout.Rotate);
                // The nav is the sidebar (first) on desktop and the bottom tab bar (after the main area) when compact.
                _nav.RemoveFromHierarchy();
                if (layout == Layout.Compact)
                    _app.Insert(_app.IndexOf(_main) + 1, _nav);
                else
                    _app.Insert(0, _nav);
                ApplyLayoutTexts();
                // Touch screens scroll the panel by dragging: no scrollbar beside the cards.
                _side.verticalScrollerVisibility = layout == Layout.Compact ? ScrollerVisibility.Hidden : _desktopScroller;
            }

            // Medium (desktop layout, under MediumBelowWidth panel units wide: tablets held sideways, small windows):
            // a narrower sidebar, side panel and margins, so the board keeps its room. 1280x720 and up stay as they are.
            bool medium = _layout == Layout.Desktop && _root.layout.width > 0f && _root.layout.width < MediumBelowWidth;
            if (medium != _medium)
            {
                _medium = medium;
                _app.EnableInClassList("app--medium", medium);
            }
            // Buttons lose their keyboard hints where there's no room or no keyboard.
            if ((Compact || _medium || Touch) != _shortLabels)
                ApplyLayoutTexts();

            if (!Compact)
            {
                // Back to the desktop layout: the sheet's sizes and positions go.
                _boardArea.style.height = StyleKeyword.Null;
                foreach (VisualElement element in new[] { _side, _sheetHead })
                {
                    element.style.left = StyleKeyword.Null;
                    element.style.right = StyleKeyword.Null;
                    element.style.top = StyleKeyword.Null;
                    element.style.bottom = StyleKeyword.Null;
                }
                _sheetScreen = null;
            }
        }

        private void ApplyLayoutTexts()
        {
            bool compact = Compact;
            _shortLabels = compact || _medium || Touch;
            bool tap = compact || Touch;
            _navDaily.text = compact ? "Daily" : "Daily puzzle";
            _navPlay.text = compact ? "Play" : "Play with friends";
            _undo.text = _shortLabels ? "Undo" : "Undo  (Z)";
            _reset.text = _shortLabels ? "Reset" : "Reset  (R)";
            _newBoard.text = compact ? "New" : _shortLabels ? "New board" : "New board  (N)";
            _shortest.text = _shortLabels ? "Shortest" : "Shortest  (H)";
            _boardTip.text = tap
                ? "Tap a robot, then tap a dot to move it. You can swipe it too."
                : "Click a robot, then a dot to move it. Arrow keys and dragging work too.";
        }

        // Compact Home and the rotate notice hide the board: the camera stops drawing it (just the background).
        private void ShowBoardVisibility()
        {
            Camera camera = Scratchpad.Camera;
            if (camera == null || BoardHidden == _boardCulled)
                return;
            _boardCulled = BoardHidden;
            if (_boardCulled)
            {
                _cameraMask = camera.cullingMask;
                camera.cullingMask = 0;
            }
            else
                camera.cullingMask = _cameraMask;
        }

        // ---- The compact sheet ----

        private static bool SheetUpByDefault(Screen screen) => screen == Screen.Home || screen == Screen.Play || screen == Screen.Lobby;

        // Each frame in the compact layout, after the screens have updated their labels: sizes the board so the sheet's
        // summary fits under it, and slides the sheet to where it's headed.
        private void ShowSheet(Screen screen)
        {
            if (!Compact)
                return;
            bool home = screen == Screen.Home;
            if (_sheetScreen != screen)
            {
                bool first = _sheetScreen == null;
                _sheetScreen = screen;
                _sheetTarget = SheetUpByDefault(screen) ? 1f : 0f;
                if (first)
                    _sheetT = _sheetTarget;
            }
            // Picking a history solution in the lobby plays it on the board: bring the board into view.
            bool replaying = Session.IsReplaying;
            if (screen == Screen.Lobby && replaying && !_wasReplaying)
                _sheetTarget = 0f;
            _wasReplaying = replaying;
            if (home)
                _sheetTarget = _sheetT = 1f;

            // The board: as wide as the stage, but short enough that the sheet's head still fits below it.
            float padTop = _main.resolvedStyle.paddingTop, padLeft = _main.resolvedStyle.paddingLeft, padRight = _main.resolvedStyle.paddingRight;
            float mainHeight = _main.layout.height;
            if (!home && mainHeight > 0f)
            {
                float room = mainHeight - padTop - _stageHead.layout.height - _boardControls.layout.height - 12f - 8f - SheetHeadHeight;
                float side = Mathf.Floor(Mathf.Min(_stage.layout.width, room));
                if (side > 0f)
                    _boardArea.style.height = side;
            }

            if (!_sheetDragging)
            {
                _sheetT = Mathf.Lerp(_sheetT, _sheetTarget, 1f - Mathf.Exp(-SheetEase * Time.unscaledDeltaTime));
                if (Mathf.Abs(_sheetT - _sheetTarget) < 0.002f)
                    _sheetT = _sheetTarget;
            }
            float down = home ? padTop : _stage.layout.yMax + 8f, up = padTop;
            _sheetRange = Mathf.Max(1f, down - up);
            float top = Mathf.Lerp(down, up, _sheetT);

            _sheetHead.style.display = home ? DisplayStyle.None : DisplayStyle.Flex;
            _sheetHead.style.left = padLeft;
            _sheetHead.style.right = padRight;
            _sheetHead.style.top = top;
            _side.style.left = padLeft;
            _side.style.right = padRight;
            _side.style.top = home ? padTop : top + SheetHeadHeight;
            _side.style.bottom = 0f;

            _sheetSummary.text = SheetSummary(screen);
            _sheetToggle.text = _sheetTarget > 0.5f ? "Show board" : "More";
        }

        private string SheetSummary(Screen screen)
        {
            switch (screen)
            {
                case Screen.Practice:
                    return PlayerSettings.PracticeExact ? $"Practice  ·  solve in {PlayerSettings.PracticeMoves}" : "Practice  ·  puzzle mode off";
                case Screen.Daily:
                    DailyState daily = _daily.State;
                    if (daily.Number <= 0 || daily.Stage != DailyStage.Playing)
                        return "Daily puzzle";
                    return daily.HasSolved
                        ? $"Daily #{daily.Number}  ·  best {daily.BestMoves}/{daily.ShortestLength}  ·  {_dailyTime.text}"
                        : $"Daily #{daily.Number}  ·  {_dailyTime.text}  ·  best possible {daily.ShortestLength}";
                case Screen.Match:
                    string value = _timerValue.text, caption = _timerCaption.text;
                    string when = value.Length == 0 ? caption.ToLowerInvariant()
                        : caption == "TIME LEFT" ? $"{value} left"
                        : caption == "TIME LIMIT" ? $"{value} to find one"
                        : $"{caption.ToLowerInvariant()} {value}";
                    return $"{_matchTitle.text}  ·  {when}";
                case Screen.Lobby:
                    return $"Lobby {_game.Match.LobbyCode}";
                case Screen.Play:
                    return "Play with friends";
                default:
                    return "";
            }
        }

        // The sheet's head: a tap flips it, a drag moves it and it settles on the nearer end.
        private void OnSheetDown(PointerDownEvent e)
        {
            _sheetDragging = true;
            _sheetMoved = false;
            _dragStartY = e.position.y;
            _dragStartT = _sheetT;
            _sheetHead.CapturePointer(e.pointerId);
        }

        private void OnSheetMove(PointerMoveEvent e)
        {
            if (!_sheetDragging)
                return;
            float dy = e.position.y - _dragStartY;
            if (Mathf.Abs(dy) > 6f)
                _sheetMoved = true;
            _sheetT = Mathf.Clamp01(_dragStartT - dy / _sheetRange);
        }

        private void OnSheetUp(PointerUpEvent e)
        {
            if (!_sheetDragging)
                return;
            _sheetDragging = false;
            _sheetHead.ReleasePointer(e.pointerId);
            _sheetTarget = !_sheetMoved ? (_sheetTarget > 0.5f ? 0f : 1f) : (_sheetT > 0.5f ? 1f : 0f);
        }

        // A press on the UI (a button, a card, a popover over the board) or on a hidden board doesn't reach the board.
        private bool PointerBlocked(Vector2 screenPosition)
        {
            if (BoardHidden)
                return true;
            if (_root.panel == null)
                return false;
            float s = PanelScale;
            var onPanel = new Vector2(screenPosition.x / s, (UnityEngine.Screen.height - screenPosition.y) / s);
            VisualElement picked = _root.panel.Pick(onPanel);
            return picked != null && _app.Contains(picked);
        }
    }
}
