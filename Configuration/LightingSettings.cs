using System;

namespace VNyanAdaptiveLighting.Configuration
{
    public enum LightingMode
    {
        Monochromatic = 0,
        Colored = 1
    }

    /// <summary>User-facing settings. Call <see cref="Validate"/> after any external change.</summary>
    public sealed class AdaptiveLightingSettings
    {
        public const string DefaultSenderName = "VNyanAdaptiveLighting_Spout";

        public bool PluginEnabled { get; set; } = true;
        public string SpoutSenderName { get; set; } = DefaultSenderName;

        public LightingMode Mode { get; set; } = LightingMode.Monochromatic;
        public float MinBrightness { get; set; } = 0.25f;
        public float MaxBrightness { get; set; } = 1.00f;
        public float BrightnessMultiplier { get; set; } = 1.00f;
        public float BrightnessGamma { get; set; } = 1.00f;
        public float ColorInfluence { get; set; } = 0f;

        /// <summary>Brightness used when the Spout feed is unavailable.</summary>
        public float NeutralBrightness { get; set; } = 1.00f;

        public float SampleRateHz { get; set; } = 10f;
        public float BrighteningTime { get; set; } = 0.3f;
        public float DarkeningTime { get; set; } = 0.3f;

        /// <summary>Fraction (0-0.4) of each screen edge excluded from analysis.</summary>
        public float BorderIgnore { get; set; } = 0f;
        /// <summary>0 = uniform weighting, 1 = strongly center weighted.</summary>
        public float CenterWeight { get; set; } = 0f;



        public AdaptiveLightingSettings Clone() => (AdaptiveLightingSettings)MemberwiseClone();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SpoutSenderName)) SpoutSenderName = DefaultSenderName;
            SpoutSenderName = SpoutSenderName.Trim();
            if (!Enum.IsDefined(typeof(LightingMode), Mode)) Mode = LightingMode.Monochromatic;

            MinBrightness = Clamp(MinBrightness, 0f, 1f, 0.25f);
            MaxBrightness = Clamp(MaxBrightness, 0f, 1f, 1f);
            if (MaxBrightness < MinBrightness) MaxBrightness = MinBrightness;
            BrightnessMultiplier = Clamp(BrightnessMultiplier, 0.1f, 3f, 1f);
            BrightnessGamma = Clamp(BrightnessGamma, 0.3f, 3f, 1f);
            ColorInfluence = Clamp(ColorInfluence, 0f, 1f, 0f);
            NeutralBrightness = Clamp(NeutralBrightness, 0f, 1f, 1f);

            SampleRateHz = Clamp(SampleRateHz, 2f, 30f, 10f);
            BrighteningTime = Clamp(BrighteningTime, 0f, 3f, 0.3f);
            DarkeningTime = Clamp(DarkeningTime, 0f, 5f, 0.3f);
            BorderIgnore = Clamp(BorderIgnore, 0f, 0.4f, 0f);
            CenterWeight = Clamp(CenterWeight, 0f, 1f, 0f);

        }

        public static float Clamp(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }
    }
}
