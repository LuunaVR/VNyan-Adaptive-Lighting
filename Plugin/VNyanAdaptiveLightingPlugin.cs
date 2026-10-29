using System;
using UnityEngine;
using VNyanAdaptiveLighting.Configuration;
using VNyanAdaptiveLighting.UI;
using VNyanInterface;

namespace VNyanAdaptiveLighting.Plugin
{
    /// <summary>
    /// VNyan plugin entry point (IVNyanPluginManifest). The DLL is placed in VNyan/Items/Assemblies.
    /// </summary>
    public sealed class VNyanAdaptiveLightingPlugin : MonoBehaviour, IVNyanPluginManifest, IButtonClickedHandler
    {
        public const string ButtonName = "VNyan Adaptive Lighting";
        private static VNyanAdaptiveLightingPlugin _instance;

        private SettingsManager _settings;
        private AdaptiveLightingHost _host;
        private LightingSettingsWindow _window;
        private bool _initialized;

        public string PluginName => "VNyanAdaptiveLighting";
        public string Version => PluginInfo.Version;
        public string Title => "VNyan Adaptive Lighting";
        public string Author => "Luuna";
        public string Website => "https://github.com/LuunaVR/VNyan-Adaptive-Lighting";

        public void InitializePlugin()
        {
            if (_initialized) return;
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[VNyanAdaptiveLighting] Already initialized; ignoring duplicate initialization.");
                return;
            }

            try
            {
                _instance = this;
                _initialized = true;

                _settings = new SettingsManager();
                _settings.Load();
                if (_settings.LastError != null) Debug.LogWarning("[VNyanAdaptiveLighting] " + _settings.LastError);

                var go = new GameObject("VNyanAdaptiveLighting_Host");
                DontDestroyOnLoad(go);
                _host = go.AddComponent<AdaptiveLightingHost>();
                _host.Init(_settings);

                _window = new LightingSettingsWindow(_host, _settings);
                _host.FrameTick += _window.Tick;
                VNyanInterface.VNyanInterface.VNyanUI.registerPluginButton(ButtonName, this);
                Debug.Log("[VNyanAdaptiveLighting] Initialized. Settings: " + _settings.FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[VNyanAdaptiveLighting] Initialization failed: " + ex);
                Cleanup();
            }
        }

        public void pluginButtonClicked()
        {
            try { _window?.Toggle(); }
            catch (Exception ex) { Debug.LogError("[VNyanAdaptiveLighting] Could not open settings window: " + ex); }
        }

        private void Update()
        {
            _window?.Tick(Time.unscaledTime);
        }

        private void Cleanup()
        {
            try { _window?.Destroy(); } catch { }
            try { _host?.Shutdown(); } catch { }
            if (_host != null) Destroy(_host.gameObject);
            _host = null;
            _window = null;
            if (_instance == this) _instance = null;
            _initialized = false;
        }

        private void OnDestroy() => Cleanup();
        private void OnApplicationQuit() => Cleanup();
    }
}
