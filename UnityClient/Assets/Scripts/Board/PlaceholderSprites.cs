using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>Plain shapes generated at runtime so the board needs no art assets yet. Each is 1x1 world unit.</summary>
    public static class PlaceholderSprites
    {
        private static Sprite _square;
        private static Sprite _circle;

        public static Sprite Square => _square != null ? _square : (_square = CreateSquare());
        public static Sprite Circle => _circle != null ? _circle : (_circle = CreateCircle(64));

        private static Sprite CreateSquare()
        {
            var texture = new Texture2D(4, 4) { filterMode = FilterMode.Point, name = "PlaceholderSquare" };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        }

        private static Sprite CreateCircle(int size)
        {
            var texture = new Texture2D(size, size) { filterMode = FilterMode.Bilinear, name = "PlaceholderCircle" };
            var pixels = new Color32[size * size];
            float radius = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    byte alpha = (byte)(Mathf.Clamp01(radius - distance) * 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
