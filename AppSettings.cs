using Newtonsoft.Json;
using System.IO;

namespace MultipleRobloxInstances
{
    /// <summary>
    /// Persisted JSON config at %LOCALAPPDATA%\MultipleRobloxInstances\config.json.
    /// All properties are nullable/defaulted so a missing key just falls back gracefully.
    /// </summary>
    public class AppSettings
    {
        // ── Launcher ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Preferred launcher. One of: "Auto" | "Vanilla" | "Bloxstrap" | "Fishstrap".
        /// Auto = detect from what's installed (Fishstrap > Bloxstrap > Vanilla priority).
        /// </summary>
        public string PreferredLauncher { get; set; } = "Auto";

        /// <summary>
        /// Full path to RobloxPlayerBeta.exe, overriding auto-detection.
        /// Useful if Roblox is installed to a non-standard location or the Versions
        /// folder detection fails. Leave null/empty for auto.
        /// Example: "C:\\Users\\you\\AppData\\Local\\Bloxstrap\\Versions\\version-abc\\RobloxPlayerBeta.exe"
        /// </summary>
        public string? RobloxExeOverride { get; set; } = null;

        // ── Instances ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of simultaneous instances the Launch button will start.
        /// 0 or below = unlimited. Does NOT affect instances launched via the website.
        /// </summary>
        public int MaxInstances { get; set; } = 0;

        // ── Locking behaviour ─────────────────────────────────────────────────────

        /// <summary>
        /// If true, a background watchdog re-acquires the singleton mutex and cookie
        /// file lock every ~10 seconds if they're ever released unexpectedly.
        /// </summary>
        public bool AutoRelock { get; set; } = true;

        // ── Startup prompts ───────────────────────────────────────────────────────

        /// <summary>
        /// If true (default), asks before closing Roblox instances already running
        /// at startup. If false, closes them automatically without a prompt.
        /// </summary>
        public bool PromptBeforeClosingExistingRoblox { get; set; } = true;

        // ── Per-account nicknames ─────────────────────────────────────────────────

        /// <summary>
        /// Nicknames keyed by lowercase Roblox username so they persist between
        /// sessions. Edited inline on each instance card.
        /// </summary>
        public Dictionary<string, string> Nicknames { get; set; } = new();

        // ── Storage path ──────────────────────────────────────────────────────────

        [JsonIgnore]
        public static string SettingsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MultipleRobloxInstances",
            "config.json");

        // ── Load / Save ───────────────────────────────────────────────────────────

        /// <summary>
        /// Loads settings from disk. Returns defaults if the file doesn't exist yet.
        /// Throws on I/O or JSON parse errors so callers can surface them visibly.
        /// </summary>
        public static AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
        }

        /// <summary>Saves settings to disk, creating the directory if needed.</summary>
        public void Save()
        {
            string? dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        // ── Nickname helpers ──────────────────────────────────────────────────────

        public string? GetNickname(string robloxUsername)
        {
            if (string.IsNullOrWhiteSpace(robloxUsername)) return null;
            return Nicknames.TryGetValue(robloxUsername.ToLowerInvariant(), out string? nick)
                ? nick : null;
        }

        public void SetNickname(string robloxUsername, string? nickname)
        {
            if (string.IsNullOrWhiteSpace(robloxUsername)) return;
            string key = robloxUsername.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(nickname))
                Nicknames.Remove(key);
            else
                Nicknames[key] = nickname.Trim();
        }

        // ── Launcher type helper ──────────────────────────────────────────────────

        public LauncherType GetPreferredLauncherType()
        {
            return Enum.TryParse<LauncherType>(PreferredLauncher, ignoreCase: true, out var t)
                ? t : LauncherType.Auto;
        }
    }
}
