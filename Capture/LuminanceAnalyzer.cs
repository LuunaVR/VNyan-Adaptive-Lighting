using System;

namespace VNyanAdaptiveLighting.Capture
{
    public struct FrameStats
    {
        /// <summary>Linear relative luminance, 0..1 (Rec.709 weights).</summary>
        public float Luminance;
        /// <summary>Average linear colour, 0..1.</summary>
        public float R, G, B;
        public bool Valid;
    }

    /// <summary>Pure luminance maths. Operates on tiny, already GPU-downsampled frames.</summary>
    public static class LuminanceAnalyzer
    {
        public const float WeightR = 0.2126f;
        public const float WeightG = 0.7152f;
        public const float WeightB = 0.0722f;

        private static readonly float[] ByteToLinear = BuildLut();

        private static float[] BuildLut()
        {
            var lut = new float[256];
            for (int i = 0; i < 256; i++) lut[i] = SrgbToLinear(i / 255f);
            return lut;
        }

        public static float SrgbToLinear(float c)
        {
            if (c <= 0f) return 0f;
            if (c >= 1f) return 1f;
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);
        }

        public static float LinearToSrgb(float c)
        {
            if (c <= 0f) return 0f;
            if (c >= 1f) return 1f;
            return c <= 0.0031308f ? c * 12.92f : 1.055f * (float)Math.Pow(c, 1.0 / 2.4) - 0.055f;
        }

        public static float Luminance(float linR, float linG, float linB)
            => WeightR * linR + WeightG * linG + WeightB * linB;

        /// <summary>
        /// Analyses an RGBA32 buffer whose colour channels are sRGB encoded. Each pixel is linearised
        /// before averaging. Alpha composites over black unless the frame's alpha is uniformly ~0
        /// (some Spout senders do not write alpha), in which case the frame is treated as opaque.
        /// </summary>
        public static FrameStats Analyze(byte[] rgba, int width, int height, float borderIgnore = 0f, float centerWeight = 0f)
        {
            var result = new FrameStats();
            if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4) return result;

            int bx = (int)(width * Clamp01(Math.Min(borderIgnore, 0.45f)));
            int by = (int)(height * Clamp01(Math.Min(borderIgnore, 0.45f)));
            int x0 = bx, x1 = width - bx, y0 = by, y1 = height - by;
            if (x1 <= x0 || y1 <= y0) { x0 = 0; x1 = width; y0 = 0; y1 = height; }

            long alphaSum = 0;
            long count = (long)(x1 - x0) * (y1 - y0);
            for (int y = y0; y < y1; y++)
            {
                int row = y * width * 4;
                for (int x = x0; x < x1; x++) alphaSum += rgba[row + x * 4 + 3];
            }
            bool useAlpha = alphaSum > count * 255L * 0.01;

            centerWeight = Clamp01(centerWeight);
            double sumR = 0, sumG = 0, sumB = 0, sumW = 0;
            float invHalfW = 2f / Math.Max(1, x1 - x0), invHalfH = 2f / Math.Max(1, y1 - y0);
            for (int y = y0; y < y1; y++)
            {
                float dy = (y - y0 + 0.5f) * invHalfH - 1f;
                int row = y * width * 4;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x * 4;
                    float w = 1f;
                    if (centerWeight > 0f)
                    {
                        float dx = (x - x0 + 0.5f) * invHalfW - 1f;
                        float g = (float)Math.Exp(-(dx * dx + dy * dy) / (2f * 0.35f * 0.35f));
                        w = (1f - centerWeight) + centerWeight * g;
                    }
                    float a = useAlpha ? rgba[i + 3] / 255f : 1f;
                    sumR += ByteToLinear[rgba[i]] * a * w;
                    sumG += ByteToLinear[rgba[i + 1]] * a * w;
                    sumB += ByteToLinear[rgba[i + 2]] * a * w;
                    sumW += w;
                }
            }
            if (sumW <= 0) return result;

            result.R = (float)(sumR / sumW);
            result.G = (float)(sumG / sumW);
            result.B = (float)(sumB / sumW);
            result.Luminance = Clamp01(Luminance(result.R, result.G, result.B));
            result.Valid = true;
            return result;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
