using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Metro.EditorTools
{
    /// <summary>
    /// Build helpers usable from the menu or from batch mode (-executeMethod).
    /// BuildWindows: development player (default Builds/Windows, or -buildDir &lt;folder&gt;).
    /// BuildRelease: the shippable player in Builds/Release/&lt;version&gt;/, ready to upload with SteamPipe.
    /// </summary>
    public static class MetroBuild
    {
        public const string Version = "1.0.0";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ExeName = "MetroSimulator.exe";

        [MenuItem("Metro/Add Main Scene To Build Settings")]
        public static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == ScenePath) return;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>Player settings shared by every build.</summary>
        static void ApplyPlayerSettings()
        {
            EnsureSceneInBuildSettings();
            // The window title and the data folder (AppData/LocalLow/<company>/<product>) follow these names.
            PlayerSettings.productName = "One More Station";
            PlayerSettings.companyName = "Metro";
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;
            if (!File.Exists("Assets/Branding/AppIcon.png")) MetroBranding.GenerateAppIcon();
        }

        [MenuItem("Metro/Build Windows Player (development)")]
        public static void BuildWindowsMenu() => BuildWindows();

        public static void BuildWindows()
        {
            ApplyPlayerSettings();
            string dir = Arg("-buildDir");
            string outDir = Path.GetFullPath(string.IsNullOrEmpty(dir) ? Path.Combine(Application.dataPath, "..", "Builds", "Windows") : dir);
            var report = Build(outDir, BuildOptions.None);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Metro/Build Release (Steam)")]
        public static void BuildReleaseMenu() => BuildRelease();

        /// <summary>The shippable build: no development flags, no steam_appid.txt, licences included.</summary>
        public static void BuildRelease()
        {
            ApplyPlayerSettings();
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Release", Version));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            var report = Build(outDir, BuildOptions.CleanBuildCache);
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            if (ok) ok = VerifyRelease(outDir);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static BuildReport Build(string outDir, BuildOptions extra)
        {
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = extra
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Metro] Build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime} -> {outDir}");
            return report;
        }

        /// <summary>Checks a release folder and removes anything that must not ship.</summary>
        static bool VerifyRelease(string outDir)
        {
            var sb = new StringBuilder();
            bool ok = true;
            // Valve: never ship steam_appid.txt (it would bypass the Steam launch check).
            foreach (var f in Directory.GetFiles(outDir, "steam_appid.txt", SearchOption.AllDirectories))
            {
                File.Delete(f);
                sb.AppendLine("removed " + f);
            }
            foreach (var d in Directory.GetDirectories(outDir, "*DoNotShip*", SearchOption.TopDirectoryOnly))
            {
                Directory.Delete(d, true);
                sb.AppendLine("removed " + d);
            }
            string data = Path.Combine(outDir, "MetroSimulator_Data");
            string[] required =
            {
                Path.Combine(outDir, ExeName),
                Path.Combine(data, "Plugins", "x86_64", "steam_api64.dll"),
                Path.Combine(data, "StreamingAssets", "Licenses", "Inter (SIL Open Font License 1.1).txt"),
                Path.Combine(data, "StreamingAssets", "Licenses", "Luckiest Guy (Apache License 2.0).txt"),
                Path.Combine(data, "StreamingAssets", "Licenses", "Shantell Sans (SIL Open Font License 1.1).txt"),
                Path.Combine(data, "StreamingAssets", "Licenses", "Steamworks.NET (MIT License).txt")
            };
            foreach (var r in required)
            {
                if (File.Exists(r)) continue;
                ok = false;
                sb.AppendLine("MISSING " + r);
            }
            long bytes = 0;
            foreach (var f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
            Debug.Log($"[Metro] Release {Version} {(ok ? "OK" : "INCOMPLETE")}: {bytes / (1024 * 1024)} MB in {outDir}\n{sb}");
            return ok;
        }
    }
}
