#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Ricochet.Client.Network
{
    /// <summary>
    /// Copies text for the player to paste elsewhere (e.g. the daily puzzle's result). Browsers don't let Unity's
    /// clipboard reach the system one, so browser builds go through JavaScript (<c>Plugins/WebGL/Clipboard.jslib</c>).
    /// </summary>
    public static class Clipboard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void Ricochet_CopyText(string text);
#endif

        public static void Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Ricochet_CopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }
    }
}
