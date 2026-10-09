using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Metro.Platform
{
    /// <summary>
    /// Everything about the player that outlives a single city: unlocked achievements and lifetime
    /// totals. Stored as profile.json next to the saves and synced through Steam Cloud.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public int version = 1;
        public List<string> achievements = new List<string>();
        public List<long> achievedAt = new List<long>();     // Unix seconds, parallel to achievements
        public double totalTrips;
        public int citiesFounded, daysRun, requestsDone, linesOpened, metropolises, gamesLost;
        public float bestDayTrips;
        public List<string> challenges = new List<string>();  // challenge ids, parallel to challengeStars
        public List<int> challengeStars = new List<int>();
        public long savedAt;                                  // Unix seconds

        public const string FileName = "profile.json";
        // Scripted test runs (-autotest) never touch the player's real achievements.
        public static string PathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, PlatformServices.HasArg("-autotest") ? "profile_test.json" : FileName);

        public bool Has(string id) => achievements.Contains(id);

        public void Add(string id, long when)
        {
            if (Has(id)) return;
            achievements.Add(id);
            achievedAt.Add(when);
        }

        public long WhenUnlocked(string id)
        {
            int i = achievements.IndexOf(id);
            return i >= 0 && i < achievedAt.Count ? achievedAt[i] : 0;
        }

        public int ChallengeStars(string id)
        {
            int i = challenges.IndexOf(id);
            return i >= 0 && i < challengeStars.Count ? challengeStars[i] : 0;
        }

        /// <summary>Keeps the best result per challenge. Returns true if it improved.</summary>
        public bool RecordChallenge(string id, int stars)
        {
            int i = challenges.IndexOf(id);
            if (i < 0)
            {
                challenges.Add(id);
                challengeStars.Add(stars);
                return stars > 0;
            }
            while (challengeStars.Count < challenges.Count) challengeStars.Add(0);
            if (stars <= challengeStars[i]) return false;
            challengeStars[i] = stars;
            return true;
        }

        /// <summary>Combines two copies (e.g. this PC and the Steam Cloud): nothing earned is ever lost.</summary>
        public void MergeFrom(PlayerProfile o)
        {
            if (o == null) return;
            for (int i = 0; i < o.challenges.Count && i < o.challengeStars.Count; i++) RecordChallenge(o.challenges[i], o.challengeStars[i]);
            for (int i = 0; i < o.achievements.Count; i++)
                Add(o.achievements[i], i < o.achievedAt.Count ? o.achievedAt[i] : 0);
            totalTrips = Math.Max(totalTrips, o.totalTrips);
            citiesFounded = Math.Max(citiesFounded, o.citiesFounded);
            daysRun = Math.Max(daysRun, o.daysRun);
            requestsDone = Math.Max(requestsDone, o.requestsDone);
            linesOpened = Math.Max(linesOpened, o.linesOpened);
            metropolises = Math.Max(metropolises, o.metropolises);
            gamesLost = Math.Max(gamesLost, o.gamesLost);
            bestDayTrips = Mathf.Max(bestDayTrips, o.bestDayTrips);
        }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static PlayerProfile Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                {
                    var p = FromJson(File.ReadAllText(PathOnDisk));
                    if (p != null) return p;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Profile unreadable, starting fresh: " + e.Message);
            }
            return new PlayerProfile();
        }

        public static PlayerProfile FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = JsonUtility.FromJson<PlayerProfile>(json);
            if (p == null) return null;
            p.achievements ??= new List<string>();
            p.achievedAt ??= new List<long>();
            p.challenges ??= new List<string>();
            p.challengeStars ??= new List<int>();
            return p;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public bool Save()
        {
            try
            {
                savedAt = Now;
                SafeFile.WriteAllText(PathOnDisk, ToJson());
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Metro] Profile save failed: " + e.Message);
                return false;
            }
        }
    }

    /// <summary>Crash-safe file writes: write a temp file, keep the previous version as .bak, then swap.</summary>
    public static class SafeFile
    {
        public static void WriteAllText(string path, string text) => WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));

        public static void WriteAllBytes(string path, byte[] data)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
            {
                string bak = path + ".bak";
                try
                {
                    File.Replace(tmp, path, bak, true);
                    return;
                }
                catch (Exception)
                {
                    // File.Replace can fail on some file systems; fall back to delete + move.
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
            }
            File.Move(tmp, path);
        }
    }
}
