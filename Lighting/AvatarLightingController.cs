using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VNyanAdaptiveLighting.Lighting
{
    /// <summary>
    /// Scales the intensity of the Unity scene lights and the ambient lighting that illuminate the avatar.
    /// MToon/lit shaders respond to both. Nothing on the avatar's materials or textures is touched.
    ///
    /// Original values are remembered per light. If another system (VNyan's own lighting UI) changes a value
    /// we have applied, the new value becomes the new original, so we never fight the user's own lighting setup.
    /// </summary>
    public sealed class AvatarLightingController
    {
        private const float RescanInterval = 10f;

        private sealed class LightState
        {
            public Light Light;
            public int Id;
            public float OrigIntensity, AppliedIntensity;
            public Color OrigColor, AppliedColor;
        }

        private readonly List<LightState> _lights = new List<LightState>(8);
        private readonly Dictionary<int, LightState> _byId = new Dictionary<int, LightState>(8);
        private float _nextScan;

        private bool _ambientCaptured;
        private float _origAmbientIntensity, _appliedAmbientIntensity;
        private Color _origAmbientLight, _appliedAmbientLight;
        private Color _origSky, _appliedSky, _origEquator, _appliedEquator, _origGround, _appliedGround;
        private AmbientMode _ambientMode;

        public bool Applied { get; private set; }
        public int LightCount => _lights.Count;

        public void Rescan() => _nextScan = 0f;

        public void Apply(float now, float brightness, RgbTint tint)
        {
            if (now >= _nextScan)
            {
                _nextScan = now + RescanInterval;
                ScanLights();
            }

            for (int i = _lights.Count - 1; i >= 0; i--)
            {
                var st = _lights[i];
                var light = st.Light;
                if (light == null) { _byId.Remove(st.Id); _lights.RemoveAt(i); continue; }

                if (Applied)
                {
                    if (!Approx(light.intensity, st.AppliedIntensity)) st.OrigIntensity = light.intensity;
                    if (!ApproxColor(light.color, st.AppliedColor)) st.OrigColor = light.color;
                }
                else
                {
                    st.OrigIntensity = light.intensity;
                    st.OrigColor = light.color;
                }

                st.AppliedIntensity = st.OrigIntensity * brightness;
                st.AppliedColor = MultiplyColor(st.OrigColor, tint);
                light.intensity = st.AppliedIntensity;
                light.color = st.AppliedColor;
            }

            ApplyAmbient(brightness, tint);
            Applied = true;
        }

        public void Restore()
        {
            if (!Applied) return;

            foreach (var st in _lights)
            {
                var light = st.Light;
                if (light == null) continue;
                if (Approx(light.intensity, st.AppliedIntensity)) light.intensity = st.OrigIntensity;
                if (ApproxColor(light.color, st.AppliedColor)) light.color = st.OrigColor;
            }
            _lights.Clear();
            _byId.Clear();
            RestoreAmbient();
            Applied = false;
            _nextScan = 0f;
        }

        private void ScanLights()
        {
            var found = UnityEngine.Object.FindObjectsOfType<Light>();
            for (int i = 0; i < found.Length; i++)
            {
                var l = found[i];
                if (l == null || !l.isActiveAndEnabled) continue;
                int id = l.GetInstanceID();
                if (_byId.ContainsKey(id)) continue;
                var st = new LightState
                {
                    Light = l, Id = id,
                    OrigIntensity = l.intensity, AppliedIntensity = l.intensity,
                    OrigColor = l.color, AppliedColor = l.color
                };
                _byId[id] = st;
                _lights.Add(st);
            }
        }

        private void ApplyAmbient(float brightness, RgbTint tint)
        {
            var mode = RenderSettings.ambientMode;
            if (!_ambientCaptured || mode != _ambientMode)
            {
                if (_ambientCaptured) RestoreAmbient();
                CaptureAmbient(mode);
            }
            else
            {
                // Adopt external changes to the ambient values we last wrote.
                switch (mode)
                {
                    case AmbientMode.Skybox:
                        if (!Approx(RenderSettings.ambientIntensity, _appliedAmbientIntensity)) _origAmbientIntensity = RenderSettings.ambientIntensity;
                        break;
                    case AmbientMode.Flat:
                        if (!ApproxColor(RenderSettings.ambientLight, _appliedAmbientLight)) _origAmbientLight = RenderSettings.ambientLight;
                        break;
                    case AmbientMode.Trilight:
                        if (!ApproxColor(RenderSettings.ambientSkyColor, _appliedSky)) _origSky = RenderSettings.ambientSkyColor;
                        if (!ApproxColor(RenderSettings.ambientEquatorColor, _appliedEquator)) _origEquator = RenderSettings.ambientEquatorColor;
                        if (!ApproxColor(RenderSettings.ambientGroundColor, _appliedGround)) _origGround = RenderSettings.ambientGroundColor;
                        break;
                }
            }

            switch (mode)
            {
                case AmbientMode.Skybox:
                    _appliedAmbientIntensity = Mathf.Clamp(_origAmbientIntensity * brightness, 0f, 8f);
                    RenderSettings.ambientIntensity = _appliedAmbientIntensity;
                    break;
                case AmbientMode.Flat:
                    _appliedAmbientLight = Scale(_origAmbientLight, brightness, tint);
                    RenderSettings.ambientLight = _appliedAmbientLight;
                    break;
                case AmbientMode.Trilight:
                    _appliedSky = Scale(_origSky, brightness, tint);
                    _appliedEquator = Scale(_origEquator, brightness, tint);
                    _appliedGround = Scale(_origGround, brightness, tint);
                    RenderSettings.ambientSkyColor = _appliedSky;
                    RenderSettings.ambientEquatorColor = _appliedEquator;
                    RenderSettings.ambientGroundColor = _appliedGround;
                    break;
            }
        }

        private void CaptureAmbient(AmbientMode mode)
        {
            _ambientMode = mode;
            _origAmbientIntensity = _appliedAmbientIntensity = RenderSettings.ambientIntensity;
            _origAmbientLight = _appliedAmbientLight = RenderSettings.ambientLight;
            _origSky = _appliedSky = RenderSettings.ambientSkyColor;
            _origEquator = _appliedEquator = RenderSettings.ambientEquatorColor;
            _origGround = _appliedGround = RenderSettings.ambientGroundColor;
            _ambientCaptured = true;
        }

        private void RestoreAmbient()
        {
            if (!_ambientCaptured) return;
            switch (_ambientMode)
            {
                case AmbientMode.Skybox:
                    if (Approx(RenderSettings.ambientIntensity, _appliedAmbientIntensity)) RenderSettings.ambientIntensity = _origAmbientIntensity;
                    break;
                case AmbientMode.Flat:
                    if (ApproxColor(RenderSettings.ambientLight, _appliedAmbientLight)) RenderSettings.ambientLight = _origAmbientLight;
                    break;
                case AmbientMode.Trilight:
                    if (ApproxColor(RenderSettings.ambientSkyColor, _appliedSky)) RenderSettings.ambientSkyColor = _origSky;
                    if (ApproxColor(RenderSettings.ambientEquatorColor, _appliedEquator)) RenderSettings.ambientEquatorColor = _origEquator;
                    if (ApproxColor(RenderSettings.ambientGroundColor, _appliedGround)) RenderSettings.ambientGroundColor = _origGround;
                    break;
            }
            _ambientCaptured = false;
        }

        private static Color MultiplyColor(Color c, RgbTint t) => new Color(c.r * t.R, c.g * t.G, c.b * t.B, c.a);
        private static Color Scale(Color c, float k, RgbTint t) => new Color(c.r * k * t.R, c.g * k * t.G, c.b * k * t.B, c.a);
        private static bool Approx(float a, float b) => Math.Abs(a - b) <= 1e-3f * Math.Max(1f, Math.Abs(b));
        private static bool ApproxColor(Color a, Color b) =>
            Math.Abs(a.r - b.r) < 2e-3f && Math.Abs(a.g - b.g) < 2e-3f && Math.Abs(a.b - b.b) < 2e-3f;
    }
}
