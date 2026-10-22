#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace Metro.Platform
{
#if !DISABLESTEAMWORKS
    /// <summary>
    /// Steam through Steamworks.NET (SDK 1.64): achievements, stats, Steam Cloud (remote storage),
    /// rich presence and the overlay. Every call is guarded: the game keeps running if Steam hiccups.
    /// </summary>
    public sealed class SteamPlatform : IPlatform
    {
        bool alive;
        bool pendingStore;
        float lastStore = -1000f;
        readonly bool cloud;
        string lastPresence = "";

        // Steamworks.NET callbacks must be kept referenced, or the GC collects them.
        Callback<GameOverlayActivated_t> overlayCb;
        Callback<UserStatsStored_t> statsStoredCb;

        public event Action<bool> OverlayChanged;

        SteamPlatform()
        {
            alive = true;
            overlayCb = Callback<GameOverlayActivated_t>.Create(e => OverlayChanged?.Invoke(e.m_bActive != 0));
            statsStoredCb = Callback<UserStatsStored_t>.Create(e =>
            {
                if (e.m_eResult != EResult.k_EResultOK) Debug.LogWarning("[Metro] Steam stats store result: " + e.m_eResult);
            });
            try
            {
                cloud = SteamRemoteStorage.IsCloudEnabledForAccount() && SteamRemoteStorage.IsCloudEnabledForApp();
            }
            catch (Exception)
            {
                cloud = false;
            }
        }

        /// <summary>Initialises the Steam API. Returns null (and why) when Steam can't be used.</summary>
        public static SteamPlatform TryCreate(out string error)
        {
            error = null;
            try
            {
                if (!Packsize.Test())
                {
                    error = "Steamworks.NET was built for the wrong platform (Packsize test failed)";
                    return null;
                }
                if (!DllCheck.Test())
                {
                    error = "steam_api DLL check failed";
                    return null;
                }
                if (SteamConfig.RelaunchThroughSteam && !SteamConfig.IsTestApp && !Application.isEditor)
                {
                    if (SteamAPI.RestartAppIfNecessary(new AppId_t(SteamConfig.AppId)))
                    {
                        error = "relaunching through Steam";
                        Application.Quit();
                        return null;
                    }
                }
                var res = SteamAPI.InitEx(out string msg);
                if (res != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    error = $"Steam unavailable ({res}{(string.IsNullOrEmpty(msg) ? "" : ": " + msg)})";
                    return null;
                }
                return new SteamPlatform();
            }
            catch (DllNotFoundException e)
            {
                error = "steam_api64.dll not found: " + e.Message;
            }
            catch (Exception e)
            {
                error = "Steam init failed: " + e.Message;
            }
            return null;
        }

        public string Name => "Steam";
        public bool IsOnline => alive;
        public bool CloudAvailable => alive && cloud;

        public string PlayerName
        {
            get
            {
                if (!alive) return "";
                try { return SteamFriends.GetPersonaName(); }
                catch (Exception) { return ""; }
            }
        }

        public void Tick()
        {
            if (!alive) return;
            try
            {
                SteamAPI.RunCallbacks();
                // Valve asks for stats to be stored "on the order of minutes", achievements right away.
                if (pendingStore && Time.realtimeSinceStartup - lastStore > 60f) Store();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Steam tick: " + e.Message);
            }
        }

        void Store()
        {
            pendingStore = false;
            lastStore = Time.realtimeSinceStartup;
            SteamUserStats.StoreStats();
        }

        public void Shutdown()
        {
            if (!alive) return;
            alive = false;
            try
            {
                SteamFriends.ClearRichPresence();
                SteamAPI.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Steam shutdown: " + e.Message);
            }
        }

        public bool UnlockAchievement(string apiName)
        {
            if (!alive) return false;
            try
            {
                if (SteamUserStats.GetAchievement(apiName, out bool done) && done) return true;
                bool ok = SteamUserStats.SetAchievement(apiName);
                if (ok) StoreStats(true);
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Steam achievement " + apiName + ": " + e.Message);
                return false;
            }
        }

        public bool IsAchievementUnlocked(string apiName)
        {
            if (!alive) return false;
            try { return SteamUserStats.GetAchievement(apiName, out bool done) && done; }
            catch (Exception) { return false; }
        }

        public void SetStat(string name, int value)
        {
            if (!alive) return;
            try
            {
                if (SteamUserStats.SetStat(name, value)) StoreStats(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Steam stat " + name + ": " + e.Message);
            }
        }

        public void IndicateProgress(string apiName, uint current, uint max)
        {
            if (!alive || current >= max) return;
            try { SteamUserStats.IndicateAchievementProgress(apiName, current, max); }
            catch (Exception) { }
        }

        public void StoreStats(bool urgent)
        {
            if (!alive) return;
            pendingStore = true;
            if (urgent && Time.realtimeSinceStartup - lastStore > 2f)
            {
                try { Store(); }
                catch (Exception e) { Debug.LogWarning("[Metro] Steam store: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ Steam Cloud

        public bool CloudWrite(string name, byte[] data)
        {
            if (!CloudAvailable || data == null) return false;
            try { return SteamRemoteStorage.FileWrite(name, data, data.Length); }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Cloud write " + name + ": " + e.Message);
                return false;
            }
        }

        public byte[] CloudRead(string name)
        {
            if (!CloudAvailable) return null;
            try
            {
                if (!SteamRemoteStorage.FileExists(name)) return null;
                int size = SteamRemoteStorage.GetFileSize(name);
                if (size <= 0) return null;
                var buf = new byte[size];
                int read = SteamRemoteStorage.FileRead(name, buf, size);
                return read == size ? buf : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Cloud read " + name + ": " + e.Message);
                return null;
            }
        }

        public bool CloudExists(string name)
        {
            if (!CloudAvailable) return false;
            try { return SteamRemoteStorage.FileExists(name); }
            catch (Exception) { return false; }
        }

        public long CloudTimestamp(string name)
        {
            if (!CloudAvailable) return 0;
            try { return SteamRemoteStorage.FileExists(name) ? SteamRemoteStorage.GetFileTimestamp(name) : 0; }
            catch (Exception) { return 0; }
        }

        public bool CloudDelete(string name)
        {
            if (!CloudAvailable) return false;
            try { return SteamRemoteStorage.FileExists(name) && SteamRemoteStorage.FileDelete(name); }
            catch (Exception) { return false; }
        }

        public List<string> CloudFiles()
        {
            var list = new List<string>();
            if (!CloudAvailable) return list;
            try
            {
                int n = SteamRemoteStorage.GetFileCount();
                for (int i = 0; i < n; i++) list.Add(SteamRemoteStorage.GetFileNameAndSize(i, out _));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Cloud list: " + e.Message);
            }
            return list;
        }

        // ------------------------------------------------------------------ rich presence

        public void SetPresence(string status, string city, string size, string day)
        {
            if (!alive) return;
            string key = status + "|" + city + "|" + size + "|" + day;
            if (key == lastPresence) return;
            lastPresence = key;
            try
            {
                SteamFriends.SetRichPresence("city", city ?? "");
                SteamFriends.SetRichPresence("size", size ?? "");
                SteamFriends.SetRichPresence("day", day ?? "");
                // Tokens live in Steam/rich_presence_english.vdf, uploaded in Steamworks.
                SteamFriends.SetRichPresence("steam_display", "#Status_" + status);
            }
            catch (Exception) { }
        }

        public void ClearPresence()
        {
            if (!alive) return;
            lastPresence = "";
            try { SteamFriends.ClearRichPresence(); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ store

        public bool OpenStorePage(uint appId)
        {
            if (!alive) return false;
            try
            {
                if (!SteamUtils.IsOverlayEnabled()) return false;
                SteamFriends.ActivateGameOverlayToStore(new AppId_t(appId), EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
#else
    /// <summary>Steamworks isn't available on this platform: always offline.</summary>
    public sealed class SteamPlatform
    {
        public bool CloudAvailable => false;
        public string PlayerName => "";

        public static IPlatform TryCreate(out string error)
        {
            error = "Steam is not supported on this platform";
            return null;
        }
    }
#endif
}
