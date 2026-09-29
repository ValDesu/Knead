using System;
using UnityEngine;

namespace Meowarts.Knead
{
    public enum KneadAxis { X, Y, Z }

    public enum KneadEffect
    {
        [Tooltip("Shorter along the axis, wider around it (keeps the volume). The bottom stays put.")]
        Squish,
        [Tooltip("Longer along the axis, thinner around it. The bottom stays put.")]
        Stretch,
        [Tooltip("Bulges out from the axis, most in the middle.")]
        Expand,
        [Tooltip("Pinches in toward the axis, most in the middle.")]
        Shrink,
        [Tooltip("Rotates around the axis, more toward the top. Amount in degrees.")]
        Twist,
        [Tooltip("Curves the axis sideways. Amount in degrees.")]
        Bend,
        [Tooltip("Squish, stretch, squish, decaying: a Squish that always wobbles.")]
        Wobble,
    }

    public enum KneadEase { Linear, InOut, Out, OutBack, Elastic, Spring }

    [Serializable]
    public struct KneadTweenParams
    {
        public KneadEffect Effect;
        [Tooltip("How strong: a fraction of the box size (0.3 = 30%), or degrees for Twist and Bend.")]
        public float Amount;
        [Tooltip("Seconds, from the hit to back at rest.")]
        public float Duration;
        [Tooltip("0 = one clean squish, 1 = jelly wobble.")]
        [Range(0f, 1f)] public float Elasticity;
        [Tooltip("Seconds before it starts.")]
        public float Delay;
        [Tooltip("Ignores Time.timeScale (pause menus).")]
        public bool UnscaledTime;

        public KneadTweenParams(KneadEffect effect, float amount, float duration = 0.45f, float elasticity = 0.35f,
            float delay = 0f, bool unscaledTime = false)
        {
            Effect = effect;
            Amount = amount;
            Duration = duration;
            Elasticity = elasticity;
            Delay = delay;
            UnscaledTime = unscaledTime;
        }

        public KneadTweenParams With(float? amount = null, float? duration = null, float? elasticity = null,
            float? delay = null, bool? unscaledTime = null) =>
            new(Effect, amount ?? Amount, duration ?? Duration, elasticity ?? Elasticity, delay ?? Delay,
                unscaledTime ?? UnscaledTime);
    }

    // Presets for KneadBox.Bounce: box.Bounce(KneadTween.Squish).
    public static class KneadTween
    {
        public static KneadTweenParams Squish => new(KneadEffect.Squish, 0.3f, 0.45f, 0.35f);
        public static KneadTweenParams Stretch => new(KneadEffect.Stretch, 0.3f, 0.45f, 0.35f);
        public static KneadTweenParams Expand => new(KneadEffect.Expand, 0.25f, 0.4f, 0.3f);
        public static KneadTweenParams Shrink => new(KneadEffect.Shrink, 0.25f, 0.4f, 0.3f);
        public static KneadTweenParams Twist => new(KneadEffect.Twist, 30f, 0.5f, 0.4f);
        public static KneadTweenParams Bend => new(KneadEffect.Bend, 30f, 0.5f, 0.4f);
        public static KneadTweenParams Wobble => new(KneadEffect.Wobble, 0.2f, 0.9f, 0.9f);

        public static KneadTweenParams Preset(KneadEffect effect) => effect switch
        {
            KneadEffect.Squish => Squish,
            KneadEffect.Stretch => Stretch,
            KneadEffect.Expand => Expand,
            KneadEffect.Shrink => Shrink,
            KneadEffect.Twist => Twist,
            KneadEffect.Bend => Bend,
            _ => Wobble,
        };

        // Bounce shape over u = time / duration: 0 at both ends, first peak at 1, then decaying swings to the
        // other side (a squish is followed by a stretch). elasticity sets the number of swings: 1 to 6.
        public static float Envelope(float u, float elasticity)
        {
            if (u <= 0f || u >= 1f) return 0f;
            float w = Mathf.PI * (1f + 5f * Mathf.Clamp01(elasticity));
            return Swing(u, w) / Swing(FirstPeak(w), w);
        }

        static float Swing(float u, float w) => Mathf.Sin(w * u) * (1f - u) * (1f - u);

        // Where the first swing peaks: w cos(wu)(1 - u) = 2 sin(wu), between 0 and the sine's own peak.
        static float FirstPeak(float w)
        {
            float lo = 0f, hi = 0.5f * Mathf.PI / w;
            for (int i = 0; i < 24; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (w * Mathf.Cos(w * m) * (1f - m) > 2f * Mathf.Sin(w * m)) lo = m;
                else hi = m;
            }
            return (lo + hi) * 0.5f;
        }

        public static float Ease(float u, KneadEase ease)
        {
            if (u <= 0f) return 0f;
            if (u >= 1f) return 1f;
            switch (ease)
            {
                case KneadEase.Linear: return u;
                case KneadEase.InOut: return u * u * (3f - 2f * u);
                case KneadEase.Out: return 1f - (1f - u) * (1f - u);
                case KneadEase.OutBack:
                {
                    const float c = 1.70158f;
                    float g = u - 1f;
                    return 1f + (c + 1f) * g * g * g + c * g * g;
                }
                case KneadEase.Elastic:
                    return Mathf.Pow(2f, -10f * u) * Mathf.Sin((u * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                default:
                {
                    // Underdamped spring (Vibrations' spring, 20% overshoot), first crossing at u = 0.6.
                    float x = u / 0.6f;
                    float k = -Mathf.Log(0.2f) / Mathf.PI;
                    float w = Mathf.PI - Mathf.Atan(1f / k);
                    float s = 1f - Mathf.Exp(-k * w * x) * (Mathf.Cos(w * x) + k * Mathf.Sin(w * x));
                    return Mathf.Lerp(s, 1f, u * u * u * u); // lands exactly on 1 at u = 1
                }
            }
        }

        // Offset of a rest point (normalized) for an effect at a signed amount, in normalized box space.
        // Worked out in box units so twists and bends stay round on boxes that aren't cubes.
        public static Vector3 EffectOffset(KneadEffect effect, Vector3 rest, float amount, KneadAxis axis, Vector3 size)
        {
            int a = (int)axis;
            float len = Mathf.Max(size[a], 1e-6f);
            var q = Vector3.Scale(rest - new Vector3(0.5f, 0.5f, 0.5f), size);
            float h = rest[a];
            var radial = q;
            radial[a] = 0f;
            var offset = Vector3.zero;

            switch (effect)
            {
                case KneadEffect.Stretch:
                    amount = -amount;
                    goto case KneadEffect.Squish;
                case KneadEffect.Squish:
                case KneadEffect.Wobble:
                {
                    float s = Mathf.Clamp(amount, -4f, 0.9f);
                    offset = radial * (1f / Mathf.Sqrt(1f - s) - 1f);
                    offset[a] = -s * h * len;
                    break;
                }
                case KneadEffect.Shrink:
                    amount = -amount;
                    goto case KneadEffect.Expand;
                case KneadEffect.Expand:
                    offset = radial * (amount * Mathf.Sin(Mathf.PI * h)); // 0.25 = 25% wider in the middle
                    break;
                case KneadEffect.Twist:
                {
                    var dir = Vector3.zero;
                    dir[a] = 1f;
                    offset = Quaternion.AngleAxis(amount * h, dir) * radial - radial;
                    break;
                }
                case KneadEffect.Bend:
                {
                    float theta = amount * Mathf.Deg2Rad;
                    if (Mathf.Abs(theta) < 1e-5f) break;
                    int b = a == 0 ? 1 : 0;
                    float r = len / theta, x = q[b], y = h * len, phi = h * theta;
                    offset[b] = r - (r - x) * Mathf.Cos(phi) - x;
                    offset[a] = (r - x) * Mathf.Sin(phi) - y;
                    break;
                }
            }

            for (int i = 0; i < 3; i++) offset[i] /= Mathf.Max(size[i], 1e-6f);
            return offset;
        }
    }

    // A running tween. await it, or use OnComplete. Stopping it ends it right away.
    public sealed class KneadTweenHandle
    {
        internal readonly KneadBox box;
        Action onComplete;
        AwaitableCompletionSource source;

        internal KneadTweenHandle(KneadBox box) => this.box = box;

        public bool IsPlaying { get; private set; } = true;

        public void Stop()
        {
            if (box) box.StopTween(this);
            Complete();
        }

        public KneadTweenHandle OnComplete(Action action)
        {
            if (!IsPlaying) action?.Invoke();
            else onComplete += action;
            return this;
        }

        public Awaitable.Awaiter GetAwaiter()
        {
            if (source == null)
            {
                source = new AwaitableCompletionSource();
                if (!IsPlaying) source.SetResult();
            }
            return source.Awaitable.GetAwaiter();
        }

        internal void Complete()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            var action = onComplete;
            onComplete = null;
            action?.Invoke();
            source?.TrySetResult();
        }

        internal static KneadTweenHandle Done(KneadBox box)
        {
            var h = new KneadTweenHandle(box);
            h.Complete();
            return h;
        }
    }
}
