using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// A clicked input field puts the caret at the end instead of selecting (highlighting) all its text,
    /// which is Unity's default. Added to every input by <see cref="UiSkin"/>.
    /// </summary>
    [RequireComponent(typeof(InputField))]
    public class CaretAtEndOnFocus : MonoBehaviour, ISelectHandler
    {
        public void OnSelect(BaseEventData eventData) => StartCoroutine(MoveCaretToEnd());

        // The field selects everything when it activates, a frame after being selected.
        private IEnumerator MoveCaretToEnd()
        {
            yield return null;
            InputField input = GetComponent<InputField>();
            if (input.isFocused)
                input.MoveTextEnd(false);
        }
    }
}
