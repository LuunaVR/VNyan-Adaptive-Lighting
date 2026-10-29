using System;
using UnityEngine;
using VNyanAdaptiveLighting.Capture;
using VNyanAdaptiveLighting.Configuration;
using VNyanAdaptiveLighting.Diagnostics;
using VNyanAdaptiveLighting.Lighting;

namespace VNyanAdaptiveLighting.Plugin
{
    public enum TestMode
    {
        Live = 0,
        Darkness,
        Normal,
        Maximum,
        Colored
    }

    /// <summary>
    /// Per-frame driver. Sampling happens at the configured rate inside the Spout receiver; this class only
    /// smooths the latest result every frame and pushes it to the lights in LateUpdate.
    /// </summary>
    public sealed class AdaptiveLightingHost : MonoBehaviour
    {
        private const float AvatarPollInterval = 1f;
        private const float TintTimeConstant = 0.35f;

        private SettingsManager _settings;
        private SpoutFeedReceiver _feed;
        private readonly AvatarLightingController _lighting = new AvatarLightingController();
        private readonly LightingSmoother _smoother = new LightingSmoother();
        private readonly PerformanceMonitor _perf = new PerformanceMonitor();

        private float _baseTarget = 1f;
        private float _level;
        private RgbTint _tintTarget = RgbTint.White;
        private RgbTint _tint = RgbTint.White;
        private bool _wasEnabled;
        private float _nextAvatarPoll;
        private object _lastAvatar;
        private float _lastSampleAt;
        private readonly AdaptiveLightingSettings _scratch = new AdaptiveLightingSettings { Mode = LightingMode.Colored };
        private float _blackSince = -1f;
        private const float BlackLuminance = 0.002f;
        private const float BlackHoldSeconds = 2f;

        public TestMode Test { get; private set; }
        public float ScreenLuminance { get; private set; }
        public float TargetBrightness { get; private set; } = 1f;
        public float CurrentBrightness => _smoother.Initialized ? _smoother.Value : 1f;
        public SpoutFeedReceiver Feed => _feed;
        public PerformanceMonitor Perf => _perf;
        public AvatarLightingController Lighting => _lighting;
        public SettingsManager Settings => _settings;

        /// <summary>Raised every frame (used by the settings window to refresh diagnostics).</summary>
        public event Action<float> FrameTick;

        public void Init(SettingsManager settings)
        {
            _settings = settings;
            _feed = new SpoutFeedReceiver(_perf);
        }

        public void ReconnectSpout() => _feed?.Reconnect();

        public void SetTestMode(TestMode mode)
        {
            Test = mode;
        }

        private void Update()
        {
            if (_settings == null) return;
            _perf.Frame.Begin();
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            _settings.Tick(now);
            FrameTick?.Invoke(now);
            var s = _settings.Current;

            if (!s.PluginEnabled)
            {
                if (_wasEnabled) Deactivate();
                _perf.Frame.End();
                return;
            }

            if (!_wasEnabled)
            {
                _wasEnabled = true;
                _smoother.Reset(1f); // start from the avatar's original look
                _tint = RgbTint.White;
                _lighting.Rescan();
            }

            PollAvatar(now);
            ComputeTarget(now, dt, s);
            TargetBrightness = Mathf.Clamp(_baseTarget, 0f, BrightnessMapper.HardMaxBrightness);
            _smoother.Update(TargetBrightness, dt, s.BrighteningTime, s.DarkeningTime);

            _tint.R = LightingSmoother.Step(_tint.R, _tintTarget.R, dt, TintTimeConstant);
            _tint.G = LightingSmoother.Step(_tint.G, _tintTarget.G, dt, TintTimeConstant);
            _tint.B = LightingSmoother.Step(_tint.B, _tintTarget.B, dt, TintTimeConstant);
            _perf.Frame.End();
        }

        private void LateUpdate()
        {
            if (_settings == null || !_wasEnabled) return;
            // Applied after all Update() calls so our scaling follows any VNyan lighting changes made this frame.
            _lighting.Apply(Time.unscaledTime, _smoother.Value, _tint);
        }

        private void ComputeTarget(float now, float dt, AdaptiveLightingSettings s)
        {
            switch (Test)
            {
                case TestMode.Darkness:
                    _level = 0f; ScreenLuminance = 0f;
                    _baseTarget = BrightnessMapper.Map(0f, s);
                    _tintTarget = RgbTint.White;
                    _feed.Stop();
                    return;
                case TestMode.Maximum:
                    _level = 1f; ScreenLuminance = 1f;
                    _baseTarget = BrightnessMapper.Map(1f, s);
                    _tintTarget = RgbTint.White;
                    _feed.Stop();
                    return;
                case TestMode.Colored:
                {
                    // Cycles through colours at full strength so the tint is easy to see, whatever the saved mode is.
                    var col = Color.HSVToRGB(Mathf.Repeat(now / 6f, 1f), 1f, 1f);
                    _scratch.ColorInfluence = s.ColorInfluence > 0f ? s.ColorInfluence : 1f;
                    _baseTarget = 1f;
                    _tintTarget = BrightnessMapper.ComputeTint(new FrameStats { R = col.r, G = col.g, B = col.b, Valid = true }, _scratch);
                    _feed.Stop();
                    return;
                }
                case TestMode.Normal:
                    _baseTarget = 1f;
                    _tintTarget = RgbTint.White;
                    _feed.Stop();
                    return;
            }

            _feed.SetAnalysisSettings(s);
            _feed.Tick(now, s);

            if (_feed.Connected)
            {
                if (_feed.ConsumeNewStats())
                {
                    var stats = _feed.Latest;
                    ScreenLuminance = stats.Luminance;
                    _level = BrightnessMapper.PerceptualLevel(stats.Luminance);
                    float sampleDt = Mathf.Max(0.001f, now - _lastSampleAt);
                    _lastSampleAt = now;
                    _baseTarget = BrightnessMapper.Map(_level, s);
                    _tintTarget = BrightnessMapper.ComputeTint(stats, s);
                    // A pure black frame for a while means no game is running (e.g. it was closed), not a dark scene.
                    if (stats.Luminance < BlackLuminance)
                    {
                        if (_blackSince < 0f) _blackSince = now;
                    }
                    else _blackSince = -1f;
                }

                if (_blackSince >= 0f && now - _blackSince >= BlackHoldSeconds)
                {
                    _baseTarget = s.NeutralBrightness;
                    _tintTarget = RgbTint.White;
                }
            }
            else
            {
                _blackSince = -1f;
                // No feed: ease back to the neutral level.
                _baseTarget = s.NeutralBrightness;
                _tintTarget = RgbTint.White;
                _lastSampleAt = now;
            }
        }

        private void PollAvatar(float now)
        {
            if (now < _nextAvatarPoll) return;
            _nextAvatarPoll = now + AvatarPollInterval;
            try
            {
                var avatar = VNyanInterface.VNyanInterface.VNyanAvatar?.getAvatarObject();
                if (!ReferenceEquals(avatar, _lastAvatar))
                {
                    _lastAvatar = avatar;
                    _lighting.Rescan();
                }
            }
            catch (Exception) { /* VNyan avatar interface not ready; ignore */ }
        }

        private void Deactivate()
        {
            _wasEnabled = false;
            _lighting.Restore();
            _feed.Stop();
            _smoother.Reset(1f);
            TargetBrightness = 1f;
        }

        public void Shutdown()
        {
            try { _lighting.Restore(); } catch { }
            try { _feed?.Dispose(); } catch { }
            try { _settings?.FlushIfDirty(); } catch { }
            _wasEnabled = false;
        }

        private void OnDisable() => Shutdown();
        private void OnDestroy() => Shutdown();
        private void OnApplicationQuit() => Shutdown();
    }
}
