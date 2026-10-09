using System;
using System.Collections.Generic;
using Metro.UI;
using UnityEngine;

namespace Metro
{
    /// <summary>Player preferences, persisted in PlayerPrefs (per machine; saves and achievements use Steam Cloud).</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public int version = 2;
        public float uiScale = 1f;
        public bool sound = true;           // version 1; folded into sfxVolume
        public bool labels = true;
        public bool edgePan;
        public bool autosave = true;
        public bool fullscreen;             // version 1; folded into windowMode
        public float masterVolume = 1f;
        public float musicVolume = 0.55f;
        public float sfxVolume = 0.8f;
        public int windowMode = 1;          // 0 windowed, 1 borderless fullscreen, 2 exclusive fullscreen
        public int resolutionWidth, resolutionHeight;   // 0 = automatic
        public bool vsync = true;
        public bool pauseOnFocusLoss;
        public bool colorBlind;                         // blue-to-vermilion crowding colours
        public int frameLimit;                          // with VSync off: 0 = unlimited, else 30/60/120/144
        public List<KeyBindSave> keys = new List<KeyBindSave>();

        public static readonly int[] FrameLimits = { 30, 60, 120, 144, 0 };
        public static readonly string[] FrameLimitNames = { "30", "60", "120", "144", "Unlimited" };

        // Scripted test runs keep their own settings, like their own saves and profile.
        static string Key => Metro.Platform.PlatformServices.HasArg("-autotest") ? "metro.settings.autotest" : "metro.settings.v1";

        public static readonly string[] WindowModeNames = { "Windowed", "Borderless", "Fullscreen" };

        public static GameSettings Load()
        {
            try
            {
                if (PlayerPrefs.HasKey(Key))
                {
                    var s = JsonUtility.FromJson<GameSettings>(PlayerPrefs.GetString(Key));
                    if (s != null)
                    {
                        if (s.version < 2)
                        {
                            s.version = 2;
                            if (!s.sound) s.sfxVolume = 0f;
                            s.windowMode = s.fullscreen ? 1 : 0;
                        }
                        Keybinds.Load(s.keys);
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Settings reset: " + e.Message);
            }
            return new GameSettings { windowMode = Screen.fullScreen ? 1 : 0 };
        }

        public void Save()
        {
            keys = Keybinds.Save();
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public void Apply()
        {
            Sfx.enabled = sfxVolume > 0.001f;
            Sfx.volume = Sfx.VolumeFor(sfxVolume);
            AudioListener.volume = Mathf.Clamp01(masterVolume);
            if (MusicPlayer.I != null) MusicPlayer.I.SetVolume(musicVolume);
            if (UIRoot.I != null) UIRoot.I.UiScale = uiScale;
            if (GameRoot.I != null && GameRoot.I.CamCtl != null) GameRoot.I.CamCtl.edgePan = edgePan;
            MapPalette.ColorBlindSafe = colorBlind;
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            Application.targetFrameRate = vsync ? -1 : frameLimit > 0 ? frameLimit : 1000;
            // Automated runs pass -screen-width/-autotest and keep the window they asked for.
            if (!Application.isEditor && !Metro.Platform.PlatformServices.HasArg("-autotest") && !Metro.Platform.PlatformServices.HasArg("-screen-width")) ApplyDisplay();
        }

        public FullScreenMode Mode => windowMode == 2 ? FullScreenMode.ExclusiveFullScreen : windowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

        void ApplyDisplay()
        {
            var mode = Mode;
            int w = resolutionWidth, h = resolutionHeight;
            if (w <= 0 || h <= 0)
            {
                if (mode == FullScreenMode.Windowed)
                {
                    w = Mathf.Min(1600, Display.main.systemWidth - 80);
                    h = Mathf.Min(900, Display.main.systemHeight - 120);
                }
                else
                {
                    w = Display.main.systemWidth;
                    h = Display.main.systemHeight;
                }
            }
            if (Screen.fullScreenMode != mode || Screen.width != w || Screen.height != h) Screen.SetResolution(w, h, mode);
        }

        /// <summary>Distinct resolutions the display supports, largest first.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 1024 && v.y >= 600 && !list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height));
            list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
            return list;
        }
    }
}
