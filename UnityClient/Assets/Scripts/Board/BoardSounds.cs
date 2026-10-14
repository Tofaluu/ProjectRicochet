using UnityEngine;

namespace Ricochet.Client.Board
{
    /// <summary>Short sound effects synthesized at runtime (no audio assets yet).</summary>
    public static class BoardSounds
    {
        private const int Rate = 44100;

        private static AudioClip _thud;
        private static AudioClip _select;
        private static AudioClip _success;
        private static AudioClip _opponent;

        /// <summary>A robot stopping against a wall or another robot: a soft, low knock.</summary>
        public static AudioClip Thud => _thud != null ? _thud : (_thud = Create("Thud", 0.16f, t =>
        {
            float pitch = Mathf.Lerp(150f, 70f, Mathf.Clamp01(t / 0.08f));
            float body = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 28f);
            float click = (Random.value * 2f - 1f) * Mathf.Exp(-t * 400f) * 0.25f;
            return (body + click) * 0.9f;
        }));

        /// <summary>Picking a robot: a tiny high blip.</summary>
        public static AudioClip Select => _select != null ? _select : (_select = Create("Select", 0.06f, t =>
            Mathf.Sin(2f * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 70f) * 0.5f));

        /// <summary>Solving it yourself: a soft, short three-note rise.</summary>
        public static AudioClip Success => _success != null ? _success : (_success = Create("Success", 1.1f, t =>
            Notes(t, 0.06f, 7f, 523.25f, 659.25f, 783.99f) * 0.22f));

        /// <summary>An opponent locked in a new best: a gentle two-note "ding-dong", distinct from Success.</summary>
        public static AudioClip OpponentSolved => _opponent != null ? _opponent : (_opponent = Create("OpponentSolved", 1.3f, t =>
            Notes(t, 0.16f, 5f, 880f, 659.25f) * 0.24f));

        // Sine notes starting <paramref name="gap"/> apart, each fading out at <paramref name="decay"/>.
        private static float Notes(float t, float gap, float decay, params float[] frequencies)
        {
            float sum = 0f;
            for (int i = 0; i < frequencies.Length; i++)
            {
                float local = t - i * gap;
                if (local < 0f)
                    break;
                sum += Mathf.Sin(2f * Mathf.PI * frequencies[i] * local) * Mathf.Exp(-local * decay);
            }
            return sum;
        }

        private static AudioClip Create(string name, float seconds, System.Func<float, float> sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                // A tiny fade-in so clips never pop, and a smooth fade over the last fifth so tails die out
                // gently instead of being cut.
                float fadeOut = Mathf.Clamp01((seconds - t) / (seconds * 0.2f));
                float fade = Mathf.Clamp01(t / 0.003f) * fadeOut * fadeOut;
                data[i] = Mathf.Clamp(sample(t) * fade, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
