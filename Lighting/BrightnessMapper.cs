using System;
using VNyanAdaptiveLighting.Capture;
using VNyanAdaptiveLighting.Configuration;

namespace VNyanAdaptiveLighting.Lighting
{
    public struct RgbTint
    {
        public float R, G, B;
        public static RgbTint White => new RgbTint { R = 1f, G = 1f, B = 1f };
    }

    /// <summary>Maps screen luminance to an avatar brightness multiplier and optional light tint.</summary>
    public static class BrightnessMapper
    {
        /// <summary>Absolute upper bound on any brightness multiplier handed to the lighting controller.</summary>
        public const float HardMaxBrightness = 4f;

        public static float PerceptualLevel(float linearLuminance)
            => LuminanceAnalyzer.LinearToSrgb(linearLuminance);

        // Perceptual level of an ordinary game scene. At this level the avatar sits at the Normal brightness.
        public const float TypicalLevel = 0.4f;

        /// <summary>
        /// Curve through three anchors: black = Min, typical scene = Normal, white = Max. Brightness stays close
        /// to Normal across ordinary scenes and only falls away when the screen gets really dark.
        /// </summary>
        public static float Map(float perceptualLevel, AdaptiveLightingSettings s)
        {
            float level = (float)Math.Pow(Clamp01(perceptualLevel), s.BrightnessGamma);
            float min = s.MinBrightness;
            float max = Math.Max(s.MaxBrightness, min);
            float normal = Math.Min(Math.Max(s.NeutralBrightness, min), max);

            float b;
            if (level <= TypicalLevel)
            {
                float x = level / TypicalLevel;
                float inv = 1f - x;
                b = min + (normal - min) * (1f - inv * inv);
            }
            else
            {
                float x = (level - TypicalLevel) / (1f - TypicalLevel);
                b = normal + (max - normal) * x;
            }
            b *= s.BrightnessMultiplier;
            return Math.Min(Math.Max(b, 0f), HardMaxBrightness);
        }
        /// <summary>
        /// Light colour multiplier. Monochromatic mode (or 0% influence) is exactly white. Colored mode
        /// blends toward the screen chroma with capped saturation and is luminance-normalised so it
        /// never changes brightness.
        /// </summary>
        public static RgbTint ComputeTint(FrameStats frame, AdaptiveLightingSettings s)
        {
            if (s.Mode != LightingMode.Colored || s.ColorInfluence <= 0f || !frame.Valid) return RgbTint.White;

            float max = Math.Max(frame.R, Math.Max(frame.G, frame.B));
            float min = Math.Min(frame.R, Math.Min(frame.G, frame.B));
            if (max <= 1e-4f) return RgbTint.White;

            float saturation = (max - min) / max;
            const float MaxTintStrength = 0.35f;
            float k = Clamp01(saturation) * Clamp01(s.ColorInfluence) * MaxTintStrength;

            float r = 1f + (frame.R / max - 1f) * k;
            float g = 1f + (frame.G / max - 1f) * k;
            float b = 1f + (frame.B / max - 1f) * k;

            float y = LuminanceAnalyzer.Luminance(r, g, b);
            if (y <= 1e-4f) return RgbTint.White;
            return new RgbTint { R = r / y, G = g / y, B = b / y };
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
