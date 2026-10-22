using System;
using System.Collections.Generic;
using SharedLogic;
using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>
    /// White shapes generated at runtime (no art assets yet), tinted by the renderer's colour. Drawn from
    /// signed distance functions so edges stay smooth. Each is 1x1 world unit unless noted.
    /// </summary>
    public static class ShapeSprites
    {
        private const int Size = 128;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Circle => Get("circle", () => FromSdf(p => p.magnitude - 1f));

        /// <summary>A soft, blurred disc for shadows and glows (opaque centre fading to the edge).</summary>
        public static Sprite SoftCircle => Get("soft", () => FromAlpha(p => Mathf.SmoothStep(1f, 0f, p.magnitude)));

        /// <summary>A ring whose band is <paramref name="width"/> of the radius.</summary>
        public static Sprite Ring(float width) =>
            Get("ring" + width, () => FromSdf(p => Mathf.Abs(p.magnitude - (1f - width / 2f)) - width / 2f));

        /// <summary>
        /// A rounded rectangle for 9-slicing (<see cref="SpriteDrawMode.Sliced"/>): set the renderer's size, and
        /// the corners keep a radius of <paramref name="radius"/> world units.
        /// </summary>
        public static Sprite RoundedRect(float radius) => Get("rect" + radius, () =>
        {
            // A rounded square whose corner radius is a quarter of the texture; the border is the corner.
            Sprite source = FromSdf(p => RoundedBox(p, 0.5f));
            const int border = Size / 4;
            float pixelsPerUnit = border / radius;
            return Sprite.Create(source.texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), pixelsPerUnit,
                0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        });

        /// <summary>
        /// A ring of <paramref name="dashes"/> square-ended dashes (half of it dashes, half gaps, like a CSS dashed border)
        /// whose band is <paramref name="width"/> of the radius.
        /// </summary>
        public static Sprite DashedRing(float width, int dashes) => Get("dashed" + width + "x" + dashes, () => FromSdf(p =>
        {
            float radius = 1f - width / 2f, r = p.magnitude;
            float turn = Mathf.Atan2(p.y, p.x) / (2f * Mathf.PI) * dashes;
            float along = Mathf.Abs(turn - Mathf.Floor(turn) - 0.5f);        // 0 = a dash's middle, 0.5 = the gap's
            float arc = (along - 0.25f) * 2f * Mathf.PI / dashes * r;         // distance past the dash's end, along the ring
            return Mathf.Max(arc, Mathf.Abs(r - radius) - width / 2f);
        }));

        /// <summary>The symbol marking a target for <paramref name="robot"/> (null = the any-robot target).</summary>
        public static Sprite TargetSymbol(RobotColor? robot) => Get(SymbolName(robot), () => FromSdf(SymbolSdf(robot)));

        /// <summary>
        /// An outline for <see cref="TargetSymbol"/>: the symbol grown by <paramref name="grow"/> (of its half-size) on every
        /// side. Draw it (1 + grow) times the symbol's size, under the symbol, in the outline colour.
        /// </summary>
        public static Sprite TargetOutline(RobotColor? robot, float grow) =>
            Get(SymbolName(robot) + "-outline" + grow, () =>
            {
                Func<Vector2, float> sdf = SymbolSdf(robot);
                return FromSdf(p => sdf(p * (1f + grow)) - grow);
            });

        /// <summary>Wood grain: faint wavy lines across a square, tinted by the renderer (rotate it for the other direction).</summary>
        public static Sprite Grain => Get("grain", () => FromAlpha(p =>
        {
            float v = (p.y + 1f) * 3.5f + 0.12f * Mathf.Sin(p.x * 2.3f + p.y * 1.7f);
            return Mathf.Clamp01(1f - Mathf.Abs(v - Mathf.Round(v)) / 0.09f);
        }));

        private static string SymbolName(RobotColor? robot) =>
            !robot.HasValue ? "vortex" : robot.Value == RobotColor.Red ? "diamond" : robot.Value == RobotColor.Blue ? "circle-symbol"
            : robot.Value == RobotColor.Green ? "triangle" : "star";

        private static Func<Vector2, float> SymbolSdf(RobotColor? robot)
        {
            if (!robot.HasValue)
                return Vortex;
            switch (robot.Value)
            {
                case RobotColor.Red: return p => (Mathf.Abs(p.x) + Mathf.Abs(p.y)) * 0.75f - 0.75f;
                case RobotColor.Blue: return p => p.magnitude - 0.8f;
                case RobotColor.Green: return Triangle;
                default: return Star;
            }
        }

        /// <summary>An arrow pointing up (rotate it for the other directions): a shaft under a triangular head.</summary>
        public static Sprite Arrow => Get("arrow", () => FromSdf(ArrowShape));

        /// <summary>
        /// A white rounded square for IMGUI backgrounds, with corners of <paramref name="radiusPixels"/>:
        /// use it with <c>GUIStyle.border</c> set to the same radius, tinted by <c>GUI.backgroundColor</c>.
        /// </summary>
        public static Texture2D RoundedTexture(int radiusPixels) =>
            Get("tex" + radiusPixels, () => FromSdf(p => RoundedBox(p, 0.5f), radiusPixels * 4)).texture;

        private static Sprite Get(string key, Func<Sprite> create)
        {
            if (!Cache.TryGetValue(key, out Sprite sprite) || sprite == null)
                Cache[key] = sprite = create();
            return sprite;
        }

        // Shape from a signed distance in [-1, 1] space (negative inside), anti-aliased over about a pixel.
        private static Sprite FromSdf(Func<Vector2, float> sdf, int size = Size)
        {
            float pixel = 2f / size;
            return FromAlpha(p => Mathf.Clamp01(0.5f - sdf(p) / pixel), size);
        }

        private static Sprite FromAlpha(Func<Vector2, float> alpha, int size = Size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(p)) * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static float RoundedBox(Vector2 p, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(1f - radius, 1f - radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        private static float ArrowShape(Vector2 p)
        {
            // Shaft: a box from the bottom up into the head.
            Vector2 q = new Vector2(Mathf.Abs(p.x) - 0.17f, Mathf.Abs(p.y + 0.3f) - 0.45f);
            float shaft = Mathf.Max(q.x, q.y);
            // Head: tip at the top, base across the middle (edges from the tip to (+-0.6, 0)).
            float side = (0.75f * Mathf.Abs(p.x) + 0.6f * (p.y - 0.8f)) / 0.96f;
            float head = Mathf.Max(side, -p.y);
            return Mathf.Min(shaft, head);
        }

        private static float Triangle(Vector2 p)
        {
            // Equilateral, pointing up, centred a little low so it looks balanced.
            p = new Vector2(Mathf.Abs(p.x), p.y + 0.15f);
            const float k = 1.7320508f;
            float r = 0.8f;
            return Mathf.Max(p.x * k * 0.5f + p.y * 0.5f, -p.y) - r * 0.5f;
        }

        private static float Star(Vector2 p)
        {
            // Five points: distance to the star outline in polar form, good enough for a filled shape.
            float angle = Mathf.Atan2(p.x, p.y);
            float sector = Mathf.PI * 2f / 5f;
            float a = Mathf.Repeat(angle + sector / 2f, sector) - sector / 2f;
            float t = Mathf.Abs(a) / (sector / 2f);
            float radius = Mathf.Lerp(0.95f, 0.42f, t);
            return p.magnitude - radius;
        }

        private static float Vortex(Vector2 p)
        {
            // A ring with a dot in the middle.
            float ring = Mathf.Abs(p.magnitude - 0.72f) - 0.14f;
            float dot = p.magnitude - 0.28f;
            return Mathf.Min(ring, dot);
        }
    }
}
