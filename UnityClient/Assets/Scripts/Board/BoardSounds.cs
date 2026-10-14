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

        /// <summary>The target robot reaching the target: a rising four-note arpeggio.</summary>
        public static AudioClip Success => _success != null ? _success : (_success = Create("Success", 0.75f, t =>
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float sum = 0f;
            for (int i = 0; i < notes.Length; i++)
            {
                float start = i * 0.07f;
                if (t < start)
                    break;
                float local = t - start;
                // Soft square-ish tone: a sine plus a little of its third harmonic.
                float phase = 2f * Mathf.PI * notes[i] * local;
                sum += (Mathf.Sin(phase) + 0.2f * Mathf.Sin(3f * phase)) * Mathf.Exp(-local * 6f);
            }
            return sum * 0.28f;
        }));

        private static AudioClip Create(string name, float seconds, System.Func<float, float> sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                // A few-millisecond fade at both ends so clips never pop.
                float fade = Mathf.Clamp01(t / 0.003f) * Mathf.Clamp01((seconds - t) / 0.01f);
                data[i] = Mathf.Clamp(sample(t) * fade, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
