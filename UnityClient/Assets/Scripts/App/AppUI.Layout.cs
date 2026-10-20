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
    public partial class AppUI
    {
        private enum Layout { Desktop, Compact, Rotate }

        private const float CompactBelowWidth = 760f;   // CSS px: narrower landscape windows use the compact layout too
        private const float RotateBelowHeight = 500f;   // CSS px: landscape this short (a phone) asks to be turned
        private const float CompactDesignWidth = 430f;  // CSS px: wider compact screens (tablets) scale up a little...
        private const float CompactMaxZoom = 1.25f;     // ...up to this
        private const float CompactBoardShare = 0.55f;  // the board's most of the panel's height in the compact layout
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

        private bool Compact => _layout == Layout.Compact;

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

            // Compact: a square board as wide as the stage, but leaving the panel below some room.
            if (Compact)
            {
                float side = Mathf.Min(_boardArea.parent.contentRect.width, _root.layout.height * CompactBoardShare);
                if (side > 0f)
                    _boardArea.style.height = Mathf.Floor(side);
            }
            else
                _boardArea.style.height = StyleKeyword.Null;
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
