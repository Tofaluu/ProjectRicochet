using Ricochet.Client.Board;
using Ricochet.Client.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace Ricochet.Client.UI
{
    /// <summary>
    /// Styles the side panel at runtime from the <see cref="GameTheme"/>: rounded panel, inputs and buttons,
    /// fonts and colours. The scene keeps plain widgets (placeholder layout until the real UI pass); swap
    /// the look here and in the theme, not widget by widget.
    /// </summary>
    public static class UiSkin
    {
        private const float PanelRadius = 0.18f;
        private const float WidgetRadius = 0.12f;

        public static void Apply(Transform root, GameTheme theme)
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponent<Button>() != null)
                    StyleButton(image, theme);
                else if (image.GetComponent<InputField>() != null)
                    StyleInput(image.GetComponent<InputField>(), theme);
                else
                    Round(image, PanelRadius, theme.Panel);
            }

            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponentInParent<Button>() != null || text.GetComponentInParent<InputField>() != null)
                    continue; // styled with their widget
                string name = text.gameObject.name;
                if (name == "Title" || name == "Lobby")
                    SetText(text, theme.TitleFont, theme.PanelText, 34);
                else if (name.EndsWith("Label") || name.EndsWith("Header"))
                    SetText(text, theme.BoldFont, theme.MutedText, text.fontSize);
                else if (name == "Feedback")
                    SetText(text, theme.BoldFont, theme.FrameColor(Game.BoardMode.PlayerReplay), text.fontSize);
                else
                    SetText(text, theme.Font, theme.PanelText, text.fontSize);
            }
        }

        private static void StyleButton(Image image, GameTheme theme)
        {
            string name = image.gameObject.name;
            Color color = name.StartsWith("Leave") ? theme.DangerButton
                : name.StartsWith("Done") ? theme.SecondaryButton
                : theme.PrimaryButton;
            Round(image, WidgetRadius, color);

            // The image carries the colour; the button only lightens/darkens it.
            Button button = image.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.45f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            foreach (Text label in image.GetComponentsInChildren<Text>(true))
                SetText(label, theme.BoldFont, theme.ButtonText, label.fontSize);
        }

        private static void StyleInput(InputField input, GameTheme theme)
        {
            Round(input.image, WidgetRadius, theme.Input);
            ColorBlock colors = input.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.selectedColor = new Color(1.18f, 1.18f, 1.18f);
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
            input.colors = colors;
            input.caretColor = theme.PanelText;
            input.customCaretColor = true;
            input.selectionColor = new Color(theme.PrimaryButton.r, theme.PrimaryButton.g, theme.PrimaryButton.b, 0.45f);

            if (input.textComponent != null)
                SetText(input.textComponent, theme.Font, theme.PanelText, input.textComponent.fontSize);
            if (input.placeholder is Text placeholder)
            {
                Color muted = theme.MutedText;
                muted.a = 0.7f;
                SetText(placeholder, theme.Font, muted, placeholder.fontSize);
                placeholder.fontStyle = FontStyle.Normal;
            }
        }

        private static void Round(Image image, float radius, Color color)
        {
            image.sprite = ShapeSprites.RoundedRect(radius);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = color;
        }

        private static void SetText(Text text, Font font, Color color, int size)
        {
            text.font = font;
            text.color = color;
            text.fontSize = size;
            // Nunito's lines are a little taller than the built-in font's; truncating would hide them.
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
