using System;
using System.IO;
using VNyanAdaptiveLighting.Capture;
using VNyanAdaptiveLighting.Configuration;
using VNyanAdaptiveLighting.Lighting;
using Xunit;

namespace VNyanAdaptiveLighting.Tests
{
    public class LuminanceTests
    {
        private static byte[] Solid(byte r, byte g, byte b, byte a = 255, int w = 8, int h = 4)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) { px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = a; }
            return px;
        }

        private static float Lum(byte r, byte g, byte b) => LuminanceAnalyzer.Analyze(Solid(r, g, b), 8, 4).Luminance;

        [Fact] public void BlackIsZero() => Assert.Equal(0f, Lum(0, 0, 0), 5);
        [Fact] public void WhiteIsOne() => Assert.Equal(1f, Lum(255, 255, 255), 4);
        [Fact] public void GrayIsIntermediate()
        {
            float y = Lum(128, 128, 128);
            Assert.InRange(y, 0.19f, 0.23f); // sRGB 50% => ~0.216 linear
        }
        [Fact] public void RedGreenBlueUseRec709Weights()
        {
            Assert.Equal(0.2126f, Lum(255, 0, 0), 3);
            Assert.Equal(0.7152f, Lum(0, 255, 0), 3);
            Assert.Equal(0.0722f, Lum(0, 0, 255), 3);
        }
        [Fact] public void GreenIsBrighterThanRedIsBrighterThanBlue()
        {
            Assert.True(Lum(0, 255, 0) > Lum(255, 0, 0));
            Assert.True(Lum(255, 0, 0) > Lum(0, 0, 255));
        }
        [Fact] public void SrgbIsLinearisedNotAveraged()
        {
            Assert.NotEqual((0.2126f + 0.7152f + 0.0722f) * 0.5f, Lum(128, 128, 128), 2);
        }
        [Fact] public void TransparentPixelsCompositeOverBlack()
        {
            var px = Solid(255, 255, 255, 255);
            for (int i = 0; i < px.Length / 8; i++) px[i * 4 + 3] = 0; // half of the frame transparent
            Assert.InRange(LuminanceAnalyzer.Analyze(px, 8, 4).Luminance, 0.45f, 0.55f);
        }
        [Fact] public void UniformZeroAlphaIsTreatedAsOpaque()
            => Assert.Equal(1f, LuminanceAnalyzer.Analyze(Solid(255, 255, 255, 0), 8, 4).Luminance, 3);
        [Fact] public void BorderIgnoreExcludesEdges()
        {
            var px = Solid(0, 0, 0, 255, 10, 10);
            for (int x = 0; x < 10; x++) { int i = x * 4; px[i] = px[i + 1] = px[i + 2] = 255; } // white top row
            Assert.True(LuminanceAnalyzer.Analyze(px, 10, 10, 0f).Luminance > 0.05f);
            Assert.Equal(0f, LuminanceAnalyzer.Analyze(px, 10, 10, 0.1f).Luminance, 4);
        }
        [Fact] public void CenterWeightFavoursCentre()
        {
            var px = Solid(0, 0, 0, 255, 16, 16);
            for (int y = 6; y < 10; y++) for (int x = 6; x < 10; x++) { int i = (y * 16 + x) * 4; px[i] = px[i + 1] = px[i + 2] = 255; }
            Assert.True(LuminanceAnalyzer.Analyze(px, 16, 16, 0f, 1f).Luminance > LuminanceAnalyzer.Analyze(px, 16, 16, 0f, 0f).Luminance);
        }
        [Fact] public void InvalidBufferIsInvalidResult()
            => Assert.False(LuminanceAnalyzer.Analyze(new byte[3], 8, 4).Valid);
    }

    public class MappingTests
    {
        private readonly AdaptiveLightingSettings _s = new AdaptiveLightingSettings();

        [Fact] public void BlackMapsToMinimum() => Assert.Equal(0.25f, BrightnessMapper.Map(0f, _s), 4);
        [Fact] public void WhiteMapsToMaximum() => Assert.Equal(1.00f, BrightnessMapper.Map(1f, _s), 4);
        [Fact] public void GrayIsBetween()
        {
            float b = BrightnessMapper.Map(BrightnessMapper.PerceptualLevel(0.0508f), _s);
            Assert.InRange(b, 0.85f, 0.95f);
        }
        [Fact] public void MultiplierScales()
        {
            _s.BrightnessMultiplier = 2f;
            Assert.Equal(2f, BrightnessMapper.Map(1f, _s), 3);
        }
        [Fact] public void MinimumStaysVisible() => Assert.True(BrightnessMapper.Map(0f, _s) > 0.05f);
        [Fact] public void MonochromaticIgnoresColour()
        {
            var tint = BrightnessMapper.ComputeTint(new FrameStats { R = 1, G = 0, B = 0, Valid = true }, _s);
            Assert.Equal(1f, tint.R); Assert.Equal(1f, tint.G); Assert.Equal(1f, tint.B);
        }
        [Fact] public void MonochromaticWithInfluenceStillIgnoresColour()
        {
            _s.ColorInfluence = 1f;
            var tint = BrightnessMapper.ComputeTint(new FrameStats { R = 0, G = 1, B = 0, Valid = true }, _s);
            Assert.Equal(1f, tint.G); Assert.Equal(1f, tint.R);
        }
        [Fact] public void ColoredModeTintIsSubtleAndLuminanceNeutral()
        {
            _s.Mode = LightingMode.Colored; _s.ColorInfluence = 1f;
            var tint = BrightnessMapper.ComputeTint(new FrameStats { R = 1, G = 0, B = 0, Valid = true }, _s);
            Assert.True(tint.R > tint.G && tint.R > tint.B);
            Assert.Equal(1f, LuminanceAnalyzer.Luminance(tint.R, tint.G, tint.B), 3);
            Assert.True(tint.R / tint.G < 2f, "tint must stay subtle");
        }
        [Fact] public void ColorInfluenceScalesTint()
        {
            _s.Mode = LightingMode.Colored;
            var f = new FrameStats { R = 1, G = 0, B = 0, Valid = true };
            _s.ColorInfluence = 0.2f; var low = BrightnessMapper.ComputeTint(f, _s);
            _s.ColorInfluence = 1f; var high = BrightnessMapper.ComputeTint(f, _s);
            Assert.True(high.R - high.G > low.R - low.G);
        }
        [Fact] public void ColoredModeGrayScreenHasNoTint()
        {
            _s.Mode = LightingMode.Colored; _s.ColorInfluence = 1f;
            var t = BrightnessMapper.ComputeTint(new FrameStats { R = .4f, G = .4f, B = .4f, Valid = true }, _s);
            Assert.Equal(t.R, t.G, 4); Assert.Equal(t.G, t.B, 4);
        }
        [Fact] public void GreenAndRedScreensAffectBrightnessOnlyByLuminance()
        {
            _s.NeutralBrightness = 0.5f;
            float green = BrightnessMapper.Map(BrightnessMapper.PerceptualLevel(LuminanceAnalyzer.Luminance(0, 1, 0)), _s);
            float red = BrightnessMapper.Map(BrightnessMapper.PerceptualLevel(LuminanceAnalyzer.Luminance(1, 0, 0)), _s);
            Assert.True(green > red);
        }
    }

    public class SmoothingTests
    {
        [Fact] public void FrameRateIndependent()
        {
            var a = new LightingSmoother(); a.Reset(0f);
            var b = new LightingSmoother(); b.Reset(0f);
            for (int i = 0; i < 60; i++) a.Update(1f, 1f / 60f, 0.2f, 0.5f);
            for (int i = 0; i < 240; i++) b.Update(1f, 1f / 240f, 0.2f, 0.5f);
            Assert.Equal(a.Value, b.Value, 3);
        }
        [Fact] public void BrighteningIsFasterThanDarkening()
        {
            var up = new LightingSmoother(); up.Reset(0f);
            var down = new LightingSmoother(); down.Reset(1f);
            up.Update(1f, 0.1f, 0.12f, 0.45f);
            down.Update(0f, 0.1f, 0.12f, 0.45f);
            Assert.True(up.Value > 1f - down.Value);
        }
        [Fact] public void ConvergesWithoutOvershoot()
        {
            var s = new LightingSmoother(); s.Reset(0f);
            float prev = 0f;
            for (int i = 0; i < 600; i++) { s.Update(1f, 1f / 60f, 0.12f, 0.45f); Assert.InRange(s.Value, prev, 1f); prev = s.Value; }
            Assert.Equal(1f, s.Value, 3);
        }
        [Fact] public void ZeroTimeIsInstant()
        {
            var s = new LightingSmoother(); s.Reset(0f);
            Assert.Equal(1f, s.Update(1f, 0.016f, 0f, 0f));
        }
    }

    public class SettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "val_tests_" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact] public void DefaultsMatchSpecification()
        {
            var s = new AdaptiveLightingSettings();
            Assert.Equal(0.25f, s.MinBrightness); Assert.Equal(1.00f, s.MaxBrightness);
            Assert.Equal(1f, s.BrightnessMultiplier); Assert.Equal(1f, s.BrightnessGamma);
            Assert.Equal(10f, s.SampleRateHz); Assert.Equal(0.3f, s.BrighteningTime); Assert.Equal(0.3f, s.DarkeningTime);
            Assert.Equal(LightingMode.Monochromatic, s.Mode); Assert.Equal(0f, s.ColorInfluence);
           
            Assert.Equal("VNyan_AvatarLighting", s.SpoutSenderName);
        }
        [Fact] public void ValidateClampsAndRepairs()
        {
            var s = new AdaptiveLightingSettings { SampleRateHz = 999, MinBrightness = -5, MaxBrightness = float.NaN,
                ColorInfluence = 7, SpoutSenderName = "  ", Mode = (LightingMode)42 };
            s.Validate();
            Assert.Equal(30f, s.SampleRateHz); Assert.True(s.MinBrightness >= 0); Assert.False(float.IsNaN(s.MaxBrightness));
            Assert.Equal(1f, s.ColorInfluence); Assert.Equal("VNyan_AvatarLighting", s.SpoutSenderName);
            Assert.Equal(LightingMode.Monochromatic, s.Mode);
            Assert.True(s.MaxBrightness >= s.MinBrightness);
        }
        [Fact] public void RoundTripPersists()
        {
            var m = new SettingsManager(_dir); m.Load();
            m.Current.MinBrightness = 0.4f; m.Current.SpoutSenderName = "Other"; m.Current.Mode = LightingMode.Colored;
            Assert.True(m.Save());
            var m2 = new SettingsManager(_dir); m2.Load();
            Assert.Equal(0.4f, m2.Current.MinBrightness); Assert.Equal("Other", m2.Current.SpoutSenderName);
            Assert.Equal(LightingMode.Colored, m2.Current.Mode);
            Assert.EndsWith("VNyanAdaptiveLighting.json", m2.FilePath);
        }
        [Fact] public void CorruptFileFallsBackToDefaults()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "VNyanAdaptiveLighting.json"), "{ not json ");
            var m = new SettingsManager(_dir); m.Load();
            Assert.NotNull(m.LastError);
            Assert.Equal(0.25f, m.Current.MinBrightness);
        }
        [Fact] public void OutOfRangeFileValuesAreClamped()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "VNyanAdaptiveLighting.json"), "{\"SampleRateHz\":1000,\"MinBrightness\":-3}");
            var m = new SettingsManager(_dir); m.Load();
            Assert.Equal(30f, m.Current.SampleRateHz); Assert.Equal(0f, m.Current.MinBrightness);
        }
        [Fact] public void SavingIsDebounced()
        {
            var m = new SettingsManager(_dir); m.Load();
            m.NotifyChanged(0f); m.NotifyChanged(0.2f); m.NotifyChanged(0.4f);
            m.Tick(0.5f);
            Assert.False(File.Exists(m.FilePath));
            m.Tick(0.4f + SettingsManager.DebounceSeconds + 0.01f);
            Assert.True(File.Exists(m.FilePath));
        }
        [Fact] public void ResetKeepsEnabledFlagAndRestoresDefaults()
        {
            var m = new SettingsManager(_dir); m.Load();
            m.Current.MinBrightness = 0.9f; m.Current.PluginEnabled = false;
            m.ResetToDefaults(0f);
            Assert.Equal(0.25f, m.Current.MinBrightness); Assert.False(m.Current.PluginEnabled);
        }
    }
}
