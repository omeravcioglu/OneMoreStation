using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Metro.Platform
{
    /// <summary>
    /// Steam Cloud for saves and the player profile. Local files stay the working copies; the cloud keeps
    /// gzip-compressed mirrors. At startup the newer copy of each save wins (by timestamp), and the
    /// profile is merged so no achievement or lifetime total is ever lost. Every save is uploaded
    /// right after it's written.
    /// </summary>
    public static class CloudSync
    {
        const string ProfileName = "profile.json";
        static string SaveName(string slot) => "save_" + slot + ".json.gz";
        static string MetaName(string slot) => "save_" + slot + ".meta.json";

        public static bool Enabled => PlatformServices.Current.CloudAvailable;
        public static string LastAction { get; private set; } = "";

        /// <summary>Reconciles every save slot and the profile with the cloud. Call once at startup.</summary>
        public static void PullAll()
        {
            if (!Enabled) return;
            int down = 0, up = 0;
            foreach (var slot in SaveSystem.Slots)
            {
                try
                {
                    int r = SyncSlot(slot);
                    if (r > 0) down++;
                    if (r < 0) up++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Metro] Cloud sync of {slot} failed: {e.Message}");
                }
            }
            try { PullProfile(); }
            catch (Exception e) { Debug.LogWarning("[Metro] Cloud profile sync failed: " + e.Message); }
            LastAction = $"Steam Cloud: {down} save(s) downloaded, {up} uploaded";
            Debug.Log("[Metro] " + LastAction);
        }

        /// <summary>+1 downloaded, -1 uploaded, 0 nothing to do.</summary>
        static int SyncSlot(string slot)
        {
            var plat = PlatformServices.Current;
            string local = SaveSystem.PathFor(slot), localMeta = SaveSystem.MetaFor(slot);
            long cloudTs = plat.CloudTimestamp(SaveName(slot));
            long localTs = File.Exists(local) ? ToUnix(File.GetLastWriteTimeUtc(local)) : 0;
            if (cloudTs == 0 && localTs == 0) return 0;
            if (cloudTs > localTs + 2)
            {
                var gz = plat.CloudRead(SaveName(slot));
                if (gz == null) return 0;
                var json = Gunzip(gz);
                // Never replace a working save with something that doesn't parse.
                var test = JsonUtility.FromJson<SaveData>(Encoding.UTF8.GetString(json));
                if (test == null || test.stations == null) return 0;
                SafeFile.WriteAllBytes(local, json);
                File.SetLastWriteTimeUtc(local, FromUnix(cloudTs));
                var meta = plat.CloudRead(MetaName(slot));
                if (meta != null)
                {
                    SafeFile.WriteAllBytes(localMeta, meta);
                    File.SetLastWriteTimeUtc(localMeta, FromUnix(cloudTs));
                }
                return 1;
            }
            if (localTs > cloudTs + 2)
            {
                PushSlot(slot);
                return -1;
            }
            return 0;
        }

        /// <summary>Uploads one save slot (called after every save).</summary>
        public static void PushSlot(string slot)
        {
            if (!Enabled) return;
            try
            {
                var plat = PlatformServices.Current;
                string local = SaveSystem.PathFor(slot), localMeta = SaveSystem.MetaFor(slot);
                if (!File.Exists(local)) return;
                bool ok = plat.CloudWrite(SaveName(slot), Gzip(File.ReadAllBytes(local)));
                if (File.Exists(localMeta)) ok &= plat.CloudWrite(MetaName(slot), File.ReadAllBytes(localMeta));
                if (!ok)
                {
                    Debug.LogWarning("[Metro] Steam Cloud upload of " + slot + " failed (quota?).");
                    return;
                }
                // Align timestamps so the next start sees both copies as equal.
                long ts = plat.CloudTimestamp(SaveName(slot));
                if (ts > 0)
                {
                    File.SetLastWriteTimeUtc(local, FromUnix(ts));
                    if (File.Exists(localMeta)) File.SetLastWriteTimeUtc(localMeta, FromUnix(ts));
                }
                LastAction = "Steam Cloud: saved " + SaveSystem.SlotLabel(slot);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Cloud upload of " + slot + " failed: " + e.Message);
            }
        }

        public static void DeleteSlot(string slot)
        {
            if (!Enabled) return;
            var plat = PlatformServices.Current;
            plat.CloudDelete(SaveName(slot));
            plat.CloudDelete(MetaName(slot));
        }

        static void PullProfile()
        {
            var plat = PlatformServices.Current;
            var bytes = plat.CloudRead(ProfileName);
            if (bytes == null) return;
            var cloud = PlayerProfile.FromJson(Encoding.UTF8.GetString(bytes));
            if (cloud == null) return;
            var local = PlayerProfile.Load();
            local.MergeFrom(cloud);
            local.Save();
        }

        public static void PushProfile()
        {
            if (!Enabled) return;
            try
            {
                if (File.Exists(PlayerProfile.PathOnDisk))
                    PlatformServices.Current.CloudWrite(ProfileName, File.ReadAllBytes(PlayerProfile.PathOnDisk));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Cloud profile upload failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ helpers

        public static byte[] Gzip(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        public static byte[] Gunzip(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gz.CopyTo(output);
                return output.ToArray();
            }
        }

        static long ToUnix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        static DateTime FromUnix(long s) => DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime;
    }
}
