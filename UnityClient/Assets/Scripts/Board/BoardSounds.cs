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
        private static AudioClip _skip;
        private static AudioClip _notShorter;
        private static AudioClip _tick;
        private static AudioClip _start;
        private static AudioClip _boom;
        private static AudioClip _boomReversed;

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

        /// <summary>Skipping (a skip/done vote, or the board being skipped): a soft downward swoosh, unlike the notes.</summary>
        public static AudioClip Skip => _skip != null ? _skip : (_skip = Create("Skip", 0.32f, t =>
        {
            // Phase of a sine whose pitch glides from 620 Hz down to 260 Hz.
            float glide = 0.3f;
            float phase = 2f * Mathf.PI * (620f * t - (620f - 260f) * Mathf.Min(t, glide) * Mathf.Min(t, glide) / (2f * glide));
            float air = (Random.value * 2f - 1f) * 0.12f;
            return (Mathf.Sin(phase) + air) * Mathf.Exp(-t * 9f) * 0.35f;
        }));

        /// <summary>Countdown 3, 2, 1: a short, soft beep.</summary>
        public static AudioClip Tick => _tick != null ? _tick : (_tick = Create("Tick", 0.16f, t =>
            Mathf.Sin(2f * Mathf.PI * 660f * t) * Mathf.Exp(-t * 22f) * 0.3f));

        /// <summary>The board arrives after a countdown: the same beep an octave up, held a little longer.</summary>
        public static AudioClip Start => _start != null ? _start : (_start = Create("Start", 0.5f, t =>
            (Mathf.Sin(2f * Mathf.PI * 1320f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 660f * t)) * Mathf.Exp(-t * 7f) * 0.25f));

        /// <summary>Under the big burst: a soft low boom (falling sine plus a puff of noise).</summary>
        public static AudioClip Boom => _boom != null ? _boom : (_boom = Create("Boom", 0.7f, BoomSample));

        /// <summary>The boom played backwards (a "whoomp" sucking in) for an undone burst.</summary>
        public static AudioClip BoomReversed => _boomReversed != null ? _boomReversed
            : (_boomReversed = Create("BoomReversed", 0.7f, BoomSample, reversed: true));

        private static float BoomSample(float t)
        {
            float pitch = Mathf.Lerp(110f, 45f, Mathf.Clamp01(t / 0.35f));
            float body = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 6f);
            float puff = (Random.value * 2f - 1f) * Mathf.Exp(-t * 18f) * 0.35f;
            return (body + puff) * 0.6f;
        }

        /// <summary>Reached the target, but not in fewer moves than the best: a low, falling "uh-uh".</summary>
        public static AudioClip NotShorter => _notShorter != null ? _notShorter : (_notShorter = Create("NotShorter", 0.45f, t =>
        {
            float first = Mathf.Sin(2f * Mathf.PI * 311f * t) * Mathf.Exp(-t * 12f) * Mathf.Clamp01((0.15f - t) / 0.03f);
            float local = t - 0.13f;
            float second = local >= 0f ? Mathf.Sin(2f * Mathf.PI * 233f * local) * Mathf.Exp(-local * 9f) : 0f;
            return (first + second) * 0.3f;
        }));

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

        private static AudioClip Create(string name, float seconds, System.Func<float, float> sample, bool reversed = false)
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
            if (reversed)
                System.Array.Reverse(data); // the fades swap ends too, so it still never pops
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
