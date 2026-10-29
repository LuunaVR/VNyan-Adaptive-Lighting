using System;
using System.IO;
using Newtonsoft.Json;

namespace VNyanAdaptiveLighting.Configuration
{
    /// <summary>
    /// Loads/saves VNyanAdaptiveLighting.json. Saving is debounced and driven by <see cref="Tick"/>
    /// from the main thread, so dragging a slider never causes more than one write per debounce window.
    /// </summary>
    public sealed class SettingsManager
    {
        public const string FileName = "VNyanAdaptiveLighting.json";
        public const float DebounceSeconds = 0.75f;

        private readonly string _path;
        private float _dirtyTime = -1f;

        public AdaptiveLightingSettings Current { get; private set; } = new AdaptiveLightingSettings();
        public string FilePath => _path;
        public string LastError { get; private set; }

        public event Action SettingsChanged;

        public static string DefaultDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "VNyanAdaptiveLighting");
        }

        public SettingsManager(string directory = null)
        {
            _path = Path.Combine(string.IsNullOrEmpty(directory) ? DefaultDirectory() : directory, FileName);
        }

        public void Load()
        {
            LastError = null;
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonConvert.DeserializeObject<AdaptiveLightingSettings>(File.ReadAllText(_path));
                    Current = loaded ?? new AdaptiveLightingSettings();
                }
                else
                {
                    Current = new AdaptiveLightingSettings();
                }
            }
            catch (Exception ex)
            {
                LastError = "Settings file invalid, defaults used: " + ex.Message;
                TryBackupCorrupt();
                Current = new AdaptiveLightingSettings();
            }
            Current.Validate();
            RaiseChanged();
        }

        /// <summary>Marks settings changed (validates, notifies listeners, schedules a debounced save).</summary>
        public void NotifyChanged(float now)
        {
            Current.Validate();
            _dirtyTime = now;
            RaiseChanged();
        }

        public void ResetToDefaults(float now)
        {
            var enabled = Current.PluginEnabled;
            Current = new AdaptiveLightingSettings { PluginEnabled = enabled };
            NotifyChanged(now);
        }

        /// <summary>Call regularly from the main thread; writes once the debounce window elapsed.</summary>
        public void Tick(float now)
        {
            if (_dirtyTime >= 0f && now - _dirtyTime >= DebounceSeconds) Save();
        }

        public bool Save()
        {
            _dirtyTime = -1f;
            try
            {
                Current.Validate();
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Current, Formatting.Indented));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = "Could not save settings: " + ex.Message;
                return false;
            }
        }

        public void FlushIfDirty()
        {
            if (_dirtyTime >= 0f) Save();
        }

        private void RaiseChanged()
        {
            try { SettingsChanged?.Invoke(); } catch { /* listener errors must not break settings */ }
        }

        private void TryBackupCorrupt()
        {
            try { File.Copy(_path, _path + ".corrupt", true); } catch { }
        }
    }
}
