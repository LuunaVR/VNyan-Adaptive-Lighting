using System;
using Klak.Spout;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VNyanAdaptiveLighting.Configuration;
using VNyanAdaptiveLighting.Diagnostics;

namespace VNyanAdaptiveLighting.Capture
{
    /// <summary>
    /// Receives a Spout sender through VNyan's bundled KlakSpout receiver (a separate receiver instance, so
    /// VNyan's own avatar sender is untouched), has KlakSpout downsample every frame straight into a 64x36 texture and reads
    /// that back asynchronously at the sampling rate. Full-resolution frames never leave the GPU.
    /// </summary>
    public sealed class SpoutFeedReceiver : IDisposable
    {
        private const float SenderPollInterval = 1f;
        // KlakSpout blits each incoming frame straight into this small texture, so no full-resolution buffer is kept.
        public const int TargetWidth = 64;
        public const int TargetHeight = 36;
        private const float WarmupSeconds = 0.5f;

        private readonly PerformanceMonitor _perf;
        private GameObject _go;
        private SpoutReceiver _receiver;
        private SpoutResources _resources;
        private string _boundName;

        private RenderTexture _target;
        private float _listedSince;
        private byte[] _pixels;
        private Texture2D _syncTex;
        private bool _requestPending;
        private int _generation;
        private bool _senderListed;
        private bool _wasListed;
        private float _nextPoll;
        private float _nextSample;
        private float _lastSampleTime;
        private bool _hasNewStats;
        private readonly Action<AsyncGPUReadbackRequest> _onReadback;

        public bool Connected { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Status { get; private set; } = "Disconnected";
        public string[] KnownSenders { get; private set; } = new string[0];
        public FrameStats Latest { get; private set; }
        public float LastSampleInterval { get; private set; } = 0.1f;
        public bool Failed { get; private set; }

        public SpoutFeedReceiver(PerformanceMonitor perf)
        {
            _perf = perf;
            _onReadback = OnReadback;
        }

        /// <summary>True once per new analysed frame.</summary>
        public bool ConsumeNewStats()
        {
            bool v = _hasNewStats;
            _hasNewStats = false;
            return v;
        }

        public void Reconnect()
        {
            DestroyReceiver();
            _nextPoll = 0f;
            _wasListed = false;
        }

        public void Tick(float now, AdaptiveLightingSettings s)
        {
            if (Failed) return;
            try
            {
                if (_receiver == null || _boundName != s.SpoutSenderName) EnsureReceiver(s.SpoutSenderName);
                if (_receiver == null) return;

                if (now >= _nextPoll)
                {
                    _nextPoll = now + SenderPollInterval;
                    PollSenders(now, s.SpoutSenderName);
                }

                // KlakSpout exposes no per-frame signal, so allow a short warm-up after the sender appears.
                Connected = _senderListed && now - _listedSince >= WarmupSeconds;
                if (Connected) { Width = TargetWidth; Height = TargetHeight; Status = "Connected"; }
                else
                {
                    Status = _senderListed ? "Waiting for frames" : "Inactive. Is the source running?";
                    return;
                }

                if (now < _nextSample || _requestPending) return;
                _nextSample = now + 1f / s.SampleRateHz;
                LastSampleInterval = Math.Max(0.001f, now - _lastSampleTime);
                _lastSampleTime = now;

                _perf.Sample.Begin();
                StartSample();
                _perf.Sample.End();
            }
            catch (Exception ex)
            {
                Failed = true;
                Connected = false;
                Status = "Error: " + ex.Message;
                Debug.LogError("[VNyanAdaptiveLighting] Spout receiver failed: " + ex);
            }
        }

        private void EnsureReceiver(string name)
        {
            if (_receiver != null && _go != null)
            {
                // Same component, new name: KlakSpout releases and recreates its native receiver.
                Rebind(name);
                _boundName = name;
                _senderListed = false;
                _wasListed = false;
                _nextPoll = 0f;
                return;
            }

            if (_resources == null) _resources = FindResources();
            if (_resources == null || _resources.blitShader == null)
            {
                Failed = true;
                Status = "Spout blit shader unavailable";
                Debug.LogError("[VNyanAdaptiveLighting] Could not locate KlakSpout resources/shader in this VNyan build.");
                return;
            }

            _go = new GameObject("VNyanAdaptiveLighting_SpoutReceiver");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            // Disabled while configuring so Update never runs without resources.
            _go.SetActive(false);
            EnsureTarget();
            _receiver = _go.AddComponent<SpoutReceiver>();
            _receiver.SetResources(_resources);
            _receiver.targetTexture = _target;
            _receiver.sourceName = name;
            _go.SetActive(true);
            _boundName = name;
        }

        private static SpoutResources FindResources()
        {
            var all = Resources.FindObjectsOfTypeAll<SpoutResources>();
            foreach (var r in all)
                if (r != null && r.blitShader != null) return r;

            var shader = Shader.Find("Hidden/Klak/Spout/Blit");
            if (shader == null) return null;
            var created = ScriptableObject.CreateInstance<SpoutResources>();
            created.blitShader = shader;
            return created;
        }

        private void PollSenders(float now, string name)
        {
            KnownSenders = SpoutManager.GetSourceNames() ?? new string[0];
            bool listed = false;
            for (int i = 0; i < KnownSenders.Length; i++)
                if (string.Equals(KnownSenders[i], name, StringComparison.Ordinal)) { listed = true; break; }

            // Sender (re)appeared after being gone, e.g. OBS restarted: rebind the native receiver.
            if (listed && !_wasListed && _receiver != null && _boundName != null && Connected == false && _hadSender)
                Rebind(name);

            if (listed && !_wasListed) _listedSince = now;
            if (listed) _hadSender = true;
            _wasListed = listed;
            _senderListed = listed;
            if (!listed) { Connected = false; }
        }

        private bool _hadSender;

        // Assigning a different name makes KlakSpout drop and recreate its native receiver.
        private void Rebind(string name)
        {
            _receiver.sourceName = string.Empty;
            _receiver.sourceName = name;
        }

        private void EnsureTarget()
        {
            if (_target != null) return;
            _target = new RenderTexture(TargetWidth, TargetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            _target.Create();
            _pixels = new byte[TargetWidth * TargetHeight * 4];
        }

        private void StartSample()
        {
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                _requestPending = true;
                AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32, _onReadback);
            }
            else
            {
                // Tiny (64x36) synchronous fallback.
                if (_syncTex == null) _syncTex = new Texture2D(TargetWidth, TargetHeight, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                var prev = RenderTexture.active;
                RenderTexture.active = _target;
                _syncTex.ReadPixels(new Rect(0, 0, TargetWidth, TargetHeight), 0, 0, false);
                RenderTexture.active = prev;
                _syncTex.GetRawTextureData<byte>().CopyTo(_pixels);
                Publish(TargetWidth, TargetHeight);
            }
        }

        private void OnReadback(AsyncGPUReadbackRequest req)
        {
            _requestPending = false;
            if (_target == null || req.hasError) return;
            var data = req.GetData<byte>();
            if (data.Length < _pixels.Length) return;
            NativeArray<byte>.Copy(data, _pixels, _pixels.Length);
            Publish(TargetWidth, TargetHeight);
        }

        private AdaptiveLightingSettings _analysisSettings;
        public void SetAnalysisSettings(AdaptiveLightingSettings s) => _analysisSettings = s;

        private void Publish(int w, int h)
        {
            var s = _analysisSettings;
            Latest = LuminanceAnalyzer.Analyze(_pixels, w, h, s?.BorderIgnore ?? 0f, s?.CenterWeight ?? 0f);
            _hasNewStats = Latest.Valid;
            _perf.RecordSample(Time.unscaledTime);
        }

        private void DestroyReceiver()
        {
            _generation++;
            Connected = false;
            _senderListed = false;
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _receiver = null;
            _boundName = null;
            Status = "Disconnected";
        }

        private void ReleaseTarget()
        {
            if (_target == null) return;
            _target.Release();
            UnityEngine.Object.Destroy(_target);
            _target = null;
        }

        /// <summary>Stops receiving and frees GPU/native resources (plugin disabled or unloading).</summary>
        public void Stop()
        {
            DestroyReceiver();
            ReleaseTarget();
            if (_syncTex != null) UnityEngine.Object.Destroy(_syncTex);
            _syncTex = null;
            Failed = false;
            Width = Height = 0;
            Latest = default;
            _hasNewStats = false;
        }

        public void Dispose() => Stop();
    }
}
