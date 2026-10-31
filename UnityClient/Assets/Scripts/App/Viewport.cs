#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Ricochet.Client.App
{
    /// <summary>
    /// The screen in CSS pixels (what a browser lays pages out in): screen pixels divided by the device pixel ratio, which
    /// browser builds read from the canvas (<c>Plugins/WebGL/Viewport.jslib</c>). Elsewhere one CSS pixel is one pixel.
    /// </summary>
    public static class Viewport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int Ricochet_CanvasCssWidth();

        [DllImport("__Internal")]
        private static extern int Ricochet_TouchPrimary();

        [DllImport("__Internal")]
        private static extern float Ricochet_VisibleTop();

        [DllImport("__Internal")]
        private static extern float Ricochet_VisibleHeight();

        [DllImport("__Internal")]
        private static extern float Ricochet_TakeWheel();

        [DllImport("__Internal")]
        private static extern int Ricochet_PageVisible();
#endif

        /// <summary>The game is on screen: its browser tab is showing (elsewhere: the window has focus).</summary>
        public static bool PageVisible
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Ricochet_PageVisible() == 1;
#else
                return UnityEngine.Application.isFocused;
#endif
            }
        }

        /// <summary>Starts listening for the browser's wheel events (call once at startup; see <see cref="TakeWheel"/>).</summary>
        public static void ListenForWheel() => TakeWheel(out _);

        /// <summary>
        /// The browser's scroll distance for the wheel events since the last call, in CSS pixels (false outside browsers):
        /// a trackpad's many small events add up to what it moved, a wheel notch is ~100 px, as on a web page.
        /// </summary>
        public static bool TakeWheel(out float cssY)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            cssY = Ricochet_TakeWheel();
            return true;
#else
            cssY = 0f;
            return false;
#endif
        }

        /// <summary>
        /// The part of the screen not covered by an on-screen keyboard, in CSS pixels from the top (false where unknown:
        /// outside browsers, or browsers without the visualViewport API).
        /// </summary>
        public static bool VisibleArea(out float top, out float height)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            top = Ricochet_VisibleTop();
            height = Ricochet_VisibleHeight();
            return height > 0f;
#else
            top = height = 0f;
            return false;
#endif
        }

        /// <summary>The main pointer is a finger (phones, tablets), not a mouse: from the browser's (pointer: coarse).</summary>
        public static bool TouchPrimary
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Ricochet_TouchPrimary() == 1;
#else
                return false;
#endif
            }
        }

        /// <summary>Device pixels per CSS pixel (2 or 3 on most phones).</summary>
        public static float PixelRatio
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                int css = Ricochet_CanvasCssWidth();
                if (css > 0)
                    return UnityEngine.Mathf.Max(0.5f, UnityEngine.Screen.width / (float)css);
#endif
                return 1f;
            }
        }
    }
}
