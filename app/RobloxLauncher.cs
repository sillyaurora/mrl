using System.Diagnostics;
using System.IO;

namespace MultipleRobloxInstances
{
    public enum LauncherType { Auto, Vanilla, Bloxstrap, Fishstrap }

    /// <summary>
    /// Handles detection of which Roblox launcher the user has installed, resolves
    /// the correct RobloxPlayerBeta.exe path, and spawns new instances.
    ///
    /// Important: even when Bloxstrap or Fishstrap is the user's launcher, the log
    /// files and RobloxCookies.dat are always written to the vanilla Roblox AppData
    /// folder — Roblox itself controls those paths regardless of bootstrapper.
    /// </summary>
    public static class RobloxLauncher
    {
        private static readonly string LocalAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // ── Base install directories ──────────────────────────────────────────────
        public static string VanillaBase   => Path.Combine(LocalAppData, "Roblox");
        public static string BloxstrapBase => Path.Combine(LocalAppData, "Bloxstrap");
        public static string FishstrapBase => Path.Combine(LocalAppData, "Fishstrap");

        // Launcher executable paths (for detection only — we bypass them for launch)
        public static string BloxstrapExe => Path.Combine(BloxstrapBase, "Bloxstrap.exe");
        public static string FishstrapExe => Path.Combine(FishstrapBase, "Fishstrap.exe");

        // ── Paths that are always vanilla regardless of launcher ──────────────────

        /// <summary>Roblox log directory — always vanilla AppData, even with Bloxstrap/Fishstrap.</summary>
        public static string LogsPath => Path.Combine(VanillaBase, "logs");

        /// <summary>RobloxCookies.dat — always vanilla AppData (Error-773 fix target).</summary>
        public static string CookiePath =>
            Path.Combine(VanillaBase, "LocalStorage", "RobloxCookies.dat");

        // ── Detection ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Auto-detects which launcher is installed.
        /// Priority: Fishstrap → Bloxstrap → Vanilla.
        /// </summary>
        public static LauncherType Detect()
        {
            if (File.Exists(FishstrapExe)) return LauncherType.Fishstrap;
            if (File.Exists(BloxstrapExe)) return LauncherType.Bloxstrap;
            return LauncherType.Vanilla;
        }

        /// <summary>Resolves Auto to the detected type; passes explicit choices through.</summary>
        public static LauncherType Resolve(LauncherType preferred)
            => preferred == LauncherType.Auto ? Detect() : preferred;

        // ── Path resolution ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the Versions sub-directory for a given launcher.
        /// Bloxstrap and Fishstrap each keep their own copy of RobloxPlayerBeta.exe here.
        /// </summary>
        private static string VersionsDirFor(LauncherType type) => type switch
        {
            LauncherType.Bloxstrap => Path.Combine(BloxstrapBase, "Versions"),
            LauncherType.Fishstrap => Path.Combine(FishstrapBase, "Versions"),
            _                      => Path.Combine(VanillaBase,   "Versions"),
        };

        /// <summary>
        /// Finds the most recently-written RobloxPlayerBeta.exe under the launcher's
        /// Versions folder. Returns null if not found.
        /// </summary>
        public static string? FindRobloxPlayerBeta(LauncherType type)
        {
            string versionsDir = VersionsDirFor(type);
            if (!Directory.Exists(versionsDir)) return null;

            return Directory
                .EnumerateDirectories(versionsDir)
                .Select(dir => Path.Combine(dir, "RobloxPlayerBeta.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        /// <summary>Human-readable name shown in the UI badge.</summary>
        public static string DisplayName(LauncherType type) => type switch
        {
            LauncherType.Bloxstrap => "Bloxstrap",
            LauncherType.Fishstrap => "Fishstrap",
            _                      => "Roblox",
        };

        // ── Launch ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Launches a new Roblox instance by calling RobloxPlayerBeta.exe directly,
        /// bypassing the bootstrapper. This is intentional: we manage the singleton
        /// mutex ourselves, and letting Bloxstrap/Fishstrap intercept the launch would
        /// conflict with that lock.
        ///
        /// Bloxstrap/Fishstrap users: if you have their built-in multi-instance feature
        /// enabled, disable it — it will fight over the same mutex.
        ///
        /// Returns the started Process (still alive; caller should track the PID).
        /// Throws FileNotFoundException or InvalidOperationException on failure.
        /// </summary>
        public static Process LaunchInstance(LauncherType type, string? exePathOverride = null)
        {
            string? exe = exePathOverride;

            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                exe = FindRobloxPlayerBeta(type);

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                throw new FileNotFoundException(
                    $"Could not locate RobloxPlayerBeta.exe for {DisplayName(type)}.\n" +
                    $"Expected under: {VersionsDirFor(type)}\n\n" +
                    "You can set a manual path via the RobloxExeOverride key in config.json.",
                    exe ?? "(not found)");
            }

            var psi = new ProcessStartInfo(exe)
            {
                // --app opens the Roblox home screen. The user can then join a
                // game from there, or paste a roblox:// link into the address bar.
                Arguments      = "--app",
                UseShellExecute = false,
            };

            Process? proc = Process.Start(psi);
            if (proc is null)
                throw new InvalidOperationException(
                    "Process.Start returned null — the OS refused to start RobloxPlayerBeta.exe.");

            return proc;
        }
    }
}
