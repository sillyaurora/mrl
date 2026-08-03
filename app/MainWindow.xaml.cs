using MultipleRobloxInstances.Resources;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MultipleRobloxInstances
{
    public partial class MainWindow : Window
    {
        public readonly string Version = "2.3";

        private static readonly HttpClient RobloxAPI = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // ── State ─────────────────────────────────────────────────────────────────
        public AppSettings     _settings         = new();
        public LauncherType    _resolvedLauncher  = LauncherType.Vanilla;
        public Mutex?          RobloxLock;
        public FileStream?     RobloxCookieLock;
        private bool           _mutexOwned;
        private readonly CancellationTokenSource _watchdogCts = new();

        // Process tracking
        public FileInfo? Last;
        public bool      Debounce = false;

        // PIDs we launched directly via the Launch button.
        // These skip the debounce because we bypass the bootstrapper (one WMI event, not two).
        private readonly HashSet<int> _ourLaunchedPids = new();
        private readonly object       _pidLock         = new();

        // ─────────────────────────────────────────────────────────────────────────
        // Constructor — stays minimal; all heavy work moves to InitializeAsync.
        // ─────────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Error / status surface
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows a brief error in the action-bar strip, auto-hides after 8 s.
        /// Thread-safe.
        /// </summary>
        public void ReportError(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ErrorStrip.Text       = $"⚠  {message}";
                ErrorStrip.Visibility = Visibility.Visible;

                var t = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(8) };
                t.Tick += (_, _) =>
                {
                    ErrorStrip.Visibility = Visibility.Collapsed;
                    t.Stop();
                };
                t.Start();
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Init sequence — called by App.xaml.cs while splash is shown
        // ─────────────────────────────────────────────────────────────────────────

        public async Task InitializeAsync(Action<string, int, int, string?> status)
        {
            const int STEPS = 6;
            int step = 0;
            void Step(string msg, string? hint = null) =>
                status(msg, ++step, STEPS, hint);

            // ── 1. Load settings ─────────────────────────────────────────────────
            Step("Loading settings...");
            try   { _settings = AppSettings.Load(); }
            catch (Exception ex)
            {
                ReportError($"Config load failed, using defaults — {ex.Message}");
            }

            // ── 2. Detect launcher ───────────────────────────────────────────────
            Step("Detecting Roblox launcher...");
            _resolvedLauncher = RobloxLauncher.Resolve(_settings.GetPreferredLauncherType());

            string launcherName = RobloxLauncher.DisplayName(_resolvedLauncher);
            await Dispatcher.InvokeAsync(() =>
                LauncherBadge.Text = $"via {launcherName}");

            // Warn Bloxstrap/Fishstrap users about potential mutex conflict
            if (_resolvedLauncher is LauncherType.Bloxstrap or LauncherType.Fishstrap)
                ReportError(
                    $"{launcherName} detected — disable its built-in multi-instance feature to avoid mutex conflicts.");

            // ── 3. Check Roblox installation ─────────────────────────────────────
            Step("Checking Roblox installation...");
            if (!Directory.Exists(RobloxLauncher.VanillaBase))
            {
                // Must run on UI thread for MessageBox
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        "Roblox cannot be found.",
                        "Roblox must be installed to %LOCALAPPDATA%\\Roblox. " +
                        "Multiple Roblox Instances will now close.",
                        MessageBoxButton.OK);
                    Process.Start(new ProcessStartInfo(
                        "https://www.roblox.com/download") { UseShellExecute = true });
                    Environment.Exit(0);
                });
            }

            if (RobloxLauncher.FindRobloxPlayerBeta(_resolvedLauncher) == null
                && string.IsNullOrEmpty(_settings.RobloxExeOverride))
            {
                ReportError(
                    $"RobloxPlayerBeta.exe not found under {launcherName}'s Versions folder. " +
                    "Launch button will not work. Set RobloxExeOverride in config.json if needed.");
            }

            // Baseline log file before any Roblox starts
            Last = MostRecentRobloxLogFile();

            // ── 4. Close existing Roblox instances ───────────────────────────────
            Step("Checking for existing instances...");
            if (RobloxInstancesOpen() > 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    bool close = true;
                    if (_settings.PromptBeforeClosingExistingRoblox)
                    {
                        var r = MessageBox.Show(
                            "Multiple Roblox Instances needs to close Roblox.",
                            "Roblox must be closed first.\n\n" +
                            "[Yes] — close all open Roblox instances now.\n" +
                            "[No]  — quit this app so you can close Roblox manually.\n\n" +
                            "WARNING: any unsaved progress in Roblox will be lost.",
                            MessageBoxButton.YesNo);
                        if (r == MessageBoxResult.No) Environment.Exit(0);
                        close = r == MessageBoxResult.Yes;
                    }
                    if (close)
                    {
                        try { foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta")) p.Kill(); }
                        catch (Exception ex) { ReportError($"Failed to close Roblox: {ex.Message}"); }
                    }
                });
            }

            // ── 5. Update check ──────────────────────────────────────────────────
            Step("Checking for updates...");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                string raw = await RobloxAPI.GetStringAsync(
                    "https://raw.githubusercontent.com/Avaluate/MultipleRobloxInstances/" +
                    "refs/heads/main/UpdateAssets/Version", cts.Token);

                string online = raw.Split(new[] { '\r', '\n' }).FirstOrDefault() ?? "";
                if (online != Version)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateAvailable.Text       = $"Update Available: Version {online}";
                        UpdateAvailable.Visibility = Visibility.Visible;
                    });
                }
            }
            catch { /* network unavailable — not a fatal error, skip silently */ }

            // ── 6. Acquire locks, start watcher + watchdog ───────────────────────
            Step("Acquiring locks...", $"via {launcherName}");

            // Mutex
            try { Mutex.OpenExisting("ROBLOX_singletonMutex").Close(); } catch { }
            RobloxLock  = new Mutex(true, "ROBLOX_singletonMutex", out _mutexOwned);
            if (!_mutexOwned)
                ReportError(
                    "Failed to acquire Roblox mutex — another launcher may already hold it.");

            // Cookie lock (Error-773 fix, credit: Voidstrap)
            try
            {
                RobloxCookieLock = new FileStream(
                    RobloxLauncher.CookiePath,
                    FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (Exception ex)
            {
                ReportError(
                    $"Error-773 fix failed ({ex.GetType().Name}) — " +
                    "another program may already handle it.");
            }

            // WMI process watcher
            ProcessWatch();

            // Watchdog
            if (_settings.AutoRelock)
                _ = Task.Run(() => RunWatchdog(_watchdogCts.Token));

            await Dispatcher.InvokeAsync(() =>
                StatusText.Content = "Multiple Roblox Instances is currently running.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Watchdog — re-acquires locks if they're ever released unexpectedly
        // ─────────────────────────────────────────────────────────────────────────

        private async Task RunWatchdog(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(10_000, ct); }
                catch (OperationCanceledException) { break; }

                if (!_settings.AutoRelock) continue;

                // ── Cookie file lock ─────────────────────────────────────────────
                bool cookieOk = false;
                try
                {
                    // Probe the stream — throws if the underlying handle was closed
                    _ = RobloxCookieLock?.Length;
                    cookieOk = RobloxCookieLock?.CanRead == true;
                }
                catch { /* stream is dead */ }

                if (!cookieOk)
                {
                    try
                    {
                        RobloxCookieLock?.Dispose();
                        RobloxCookieLock = new FileStream(
                            RobloxLauncher.CookiePath,
                            FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                    catch (Exception ex)
                    {
                        ReportError($"Watchdog — cookie re-lock failed: {ex.Message}");
                    }
                }

                // ── Mutex ────────────────────────────────────────────────────────
                // If we lost ownership (e.g. the owning thread died), re-acquire.
                if (!_mutexOwned)
                {
                    try
                    {
                        try { Mutex.OpenExisting("ROBLOX_singletonMutex").Close(); } catch { }
                        RobloxLock?.Close();
                        RobloxLock   = new Mutex(true, "ROBLOX_singletonMutex", out _mutexOwned);
                        if (!_mutexOwned) ReportError("Watchdog — mutex re-acquire failed.");
                    }
                    catch (Exception ex)
                    {
                        ReportError($"Watchdog — mutex error: {ex.Message}");
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Process utilities
        // ─────────────────────────────────────────────────────────────────────────

        public int RobloxInstancesOpen()
            => Process.GetProcessesByName("RobloxPlayerBeta").Length;

        public bool CheckIfProcessExists(int pid)
        {
            try   { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch (ArgumentException) { return false; }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Log file helpers
        // ─────────────────────────────────────────────────────────────────────────

        public FileInfo? MostRecentRobloxLogFile()
        {
            try
            {
                var dir = new DirectoryInfo(RobloxLauncher.LogsPath);
                if (!dir.Exists) return null;
                return dir.GetFiles()
                           .OrderByDescending(f => f.LastWriteTime)
                           .FirstOrDefault();
            }
            catch { return null; }
        }

        public string[] ReadViaShadowCopy(string filePath)
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.Copy(filePath, tmp, overwrite: true);
                return File.ReadAllLines(tmp);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public Dictionary<string, string>? GetRobloxDetails(string[] lines)
        {
            foreach (string line in lines)
            {
                if (!line.Contains("game_join_loadtime")) continue;
                var m = Regex.Match(line, @"universeid:(\d+),.*userid:(\d+)");
                if (!m.Success) continue;
                return new Dictionary<string, string>
                {
                    { "Universe", m.Groups[1].Value },
                    { "UserID",   m.Groups[2].Value }
                };
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // WMI process watcher
        // ─────────────────────────────────────────────────────────────────────────

        void ProcessWatch()
        {
            var watcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            watcher.EventArrived += ProcessWatchEvent;
            watcher.Start();
        }

        public void ProcessWatchEvent(object sender, EventArrivedEventArgs e)
        {
            if ((string)e.NewEvent.Properties["ProcessName"].Value != "RobloxPlayerBeta.exe")
                return;

            int pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

            // Was this PID launched directly by our Launch button?
            bool isDirectLaunch;
            lock (_pidLock) { isDirectLaunch = _ourLaunchedPids.Remove(pid); }

            if (isDirectLaunch)
            {
                // Direct launch via RobloxPlayerBeta.exe --app produces exactly one
                // WMI event, so no debounce needed.
                Task.Run(async () => await CheckMonitorLog(pid));
            }
            else
            {
                // Website / roblox:// launches go through the bootstrapper and produce
                // TWO events — a short-lived relay process and then the real client.
                // The debounce skips the first and acts on the second.
                if (Debounce)
                {
                    Debounce = false;
                    Task.Run(async () => await CheckMonitorLog(pid));
                }
                else
                {
                    Debounce = true;
                }
            }
        }

        public async Task CheckMonitorLog(int robloxPid)
        {
            // Wait up to 15 s for a new log file to appear
            for (int i = 0; i < 30; i++)
            {
                FileInfo? recent = MostRecentRobloxLogFile();
                if (recent != null && (Last == null || recent.FullName != Last.FullName))
                {
                    Last = recent;
                    _ = ReadFromLog(recent.FullName, robloxPid);
                    return;
                }
                await Task.Delay(500);
            }
            ReportError($"PID {robloxPid}: no new log file appeared within 15 s.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Core per-instance loop — reads log, creates card, watches until exit
        // ─────────────────────────────────────────────────────────────────────────

        public async Task ReadFromLog(string file, int robloxPid)
        {
            string gameName    = "Failed to obtain";
            string displayName = "Failed to obtain";
            string username    = "Failed to obtain";
            string avatarUrl   = "";
            bool   gotDetails  = false;

            RobloxInstance? card = null;

            Process? proc;
            try { proc = Process.GetProcessById(robloxPid); }
            catch { return; }

            using (proc)
            {
                while (!proc.HasExited)
                {
                    if (!gotDetails)
                    {
                        string[] lines;
                        try   { lines = ReadViaShadowCopy(file); }
                        catch (Exception ex)
                        {
                            ReportError($"Log read (PID {robloxPid}): {ex.Message}");
                            await Task.Delay(1000);
                            continue;
                        }

                        var details = GetRobloxDetails(lines);
                        if (details != null)
                        {
                            string universeId = details["Universe"];
                            string userId     = details["UserID"];

                            // Game name
                            try
                            {
                                var res  = await RobloxAPI.GetAsync(
                                    $"https://games.roblox.com/v1/games?universeIds={universeId}");
                                string body = await res.Content.ReadAsStringAsync();
                                gameName = (string.IsNullOrEmpty(body) || body == "{\"data\":[]}")
                                    ? "[Private experience]"
                                    : JObject.Parse(body)["data"]![0]!["name"]!.ToString();
                            }
                            catch (Exception ex)
                            { ReportError($"Game API (PID {robloxPid}): {ex.Message}"); }

                            // Username / display name
                            try
                            {
                                var res  = await RobloxAPI.GetAsync(
                                    $"https://users.roblox.com/v1/users/{userId}");
                                var json = JObject.Parse(await res.Content.ReadAsStringAsync());
                                displayName = json["displayName"]!.ToString();
                                username    = json["name"]!.ToString();
                            }
                            catch (Exception ex)
                            { ReportError($"User API (PID {robloxPid}): {ex.Message}"); }

                            // Avatar
                            try
                            {
                                var res  = await RobloxAPI.GetAsync(
                                    $"https://thumbnails.roblox.com/v1/users/avatar-headshot" +
                                    $"?size=150x150&format=png&userIds={userId}");
                                avatarUrl = JObject
                                    .Parse(await res.Content.ReadAsStringAsync())
                                    ["data"]![0]!["imageUrl"]!.ToString();
                            }
                            catch (Exception ex)
                            { ReportError($"Avatar API (PID {robloxPid}): {ex.Message}"); }

                            // ── Create UI card ────────────────────────────────────
                            await Dispatcher.InvokeAsync(() =>
                            {
                                card = new RobloxInstance();
                                WP1.Children.Add(card);

                                // ── Nickname ─────────────────────────────────────
                                string? savedNick = _settings.GetNickname(username);
                                if (!string.IsNullOrEmpty(savedNick))
                                    card.NicknameBox.Text = savedNick;

                                card.NicknameBox.LostFocus += (_, _) =>
                                {
                                    _settings.SetNickname(username, card.NicknameBox.Text);
                                    try   { _settings.Save(); }
                                    catch (Exception ex)
                                    { ReportError($"Settings save: {ex.Message}"); }
                                };

                                // ── Kill / Dismiss button ─────────────────────────
                                card.KilInstance.Click += (_, _) =>
                                {
                                    try
                                    {
                                        if (!proc.HasExited) proc.Kill();
                                        else WP1.Children.Remove(card);
                                    }
                                    catch { WP1.Children.Remove(card); }
                                };

                                // ── Relaunch button ───────────────────────────────
                                card.RelaunchBtn.Click += (_, _) =>
                                {
                                    try
                                    {
                                        string? exeOverride =
                                            string.IsNullOrWhiteSpace(_settings.RobloxExeOverride)
                                                ? null : _settings.RobloxExeOverride;
                                        Process relaunched =
                                            RobloxLauncher.LaunchInstance(_resolvedLauncher, exeOverride);
                                        lock (_pidLock) _ourLaunchedPids.Add(relaunched.Id);
                                        WP1.Children.Remove(card);
                                    }
                                    catch (Exception ex)
                                    { ReportError($"Relaunch: {ex.Message}"); }
                                };

                                // ── Labels ────────────────────────────────────────
                                card.DisplayName.Content = displayName;
                                card.FullUsername.Content = username;
                                card.GameName.Content    = gameName;

                                if (!string.IsNullOrEmpty(avatarUrl))
                                {
                                    try
                                    {
                                        var bmp = new BitmapImage();
                                        bmp.BeginInit();
                                        bmp.UriSource = new Uri(avatarUrl, UriKind.Absolute);
                                        bmp.EndInit();
                                        card.PFP.Source = bmp;
                                    }
                                    catch { /* avatar URL was bad */ }
                                }

                                // ── Entrance animation (staggered slide+fade) ─────
                                FrameworkElement[] els =
                                {
                                    card.PFP, card.DisplayName,
                                    card.FullUsername, card.GameName, card.KilInstance
                                };
                                foreach (var el in els) Fade(el, 1, 0, 0);

                                _ = Task.Delay(100).ContinueWith(_ => Dispatcher.Invoke(() =>
                                {
                                    foreach (var el in els)
                                    {
                                        Fade(el, 0, 1, 0.5);
                                        Move(el,
                                            new Thickness(el.Margin.Left,
                                                          el.Margin.Top - 20,
                                                          el.Margin.Right,
                                                          el.Margin.Bottom),
                                            el.Margin, 0.75);
                                    }
                                }));
                            });

                            gotDetails = true;
                        }
                    }

                    await Task.Delay(1000);
                }
            } // proc disposed

            // ── Process exited — crash vs normal close ────────────────────────────
            bool crashed = false;
            try { crashed = proc.ExitCode != 0; } catch { }

            await Dispatcher.InvokeAsync(() =>
            {
                if (card == null) return;
                if (crashed)
                    card.SetCrashed();  // show crash badge + relaunch button, keep card
                else
                    WP1.Children.Remove(card); // clean exit, remove card
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UI animation helpers (same API as original, used by instance cards too)
        // ─────────────────────────────────────────────────────────────────────────

        public void Fade(DependencyObject el, double from, double to, double secs)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(secs))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, el);
            Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }

        public void Move(DependencyObject el, Thickness from, Thickness to, double secs)
        {
            var anim = new ThicknessAnimation(from, to, TimeSpan.FromSeconds(secs))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, el);
            Storyboard.SetTargetProperty(anim, new PropertyPath(MarginProperty));
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }

        public void Scaling(DependencyObject el, double from, double to, double secs)
        {
            void Anim(string prop)
            {
                var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(secs))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
                };
                Storyboard.SetTarget(a, el);
                Storyboard.SetTargetProperty(a, new PropertyPath(prop));
                var sb = new Storyboard(); sb.Children.Add(a); sb.Begin();
            }
            Anim("RenderTransform.Children[0].ScaleX");
            Anim("RenderTransform.Children[0].ScaleY");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Launch Instance button
        // ─────────────────────────────────────────────────────────────────────────

        private async void LaunchBtn_Click(object sender, RoutedEventArgs e)
        {
            int max = _settings.MaxInstances;
            if (max > 0 && RobloxInstancesOpen() >= max)
            {
                ReportError($"Max instances ({max}) already running.");
                return;
            }

            LaunchBtn.IsEnabled = false;
            try
            {
                string? exeOverride =
                    string.IsNullOrWhiteSpace(_settings.RobloxExeOverride)
                        ? null : _settings.RobloxExeOverride;

                Process p = await Task.Run(() =>
                    RobloxLauncher.LaunchInstance(_resolvedLauncher, exeOverride));

                lock (_pidLock) _ourLaunchedPids.Add(p.Id);
            }
            catch (Exception ex)
            {
                ReportError($"Launch failed: {ex.Message}");
            }
            finally
            {
                // Brief cooldown so the user can't spam-click and race the WMI watcher
                await Task.Delay(2500);
                LaunchBtn.IsEnabled = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Top bar
        // ─────────────────────────────────────────────────────────────────────────

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void UpdateAvailable_MouseDown(object sender, MouseButtonEventArgs e)
            => Process.Start(new ProcessStartInfo
               {
                   FileName       = "https://github.com/Avaluate/MultipleRobloxInstances/releases/",
                   UseShellExecute = true
               });

        private void Minimise_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _watchdogCts.Cancel();
            _mutexOwned = false;
            try { RobloxLock?.ReleaseMutex(); RobloxLock?.Close(); } catch { }
            try { RobloxCookieLock?.Close(); }                       catch { }
            Environment.Exit(0);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Status bar links
        // ─────────────────────────────────────────────────────────────────────────

        private void WebsiteIcon_MouseDown(object sender, MouseButtonEventArgs e)
            => Process.Start(new ProcessStartInfo
               { FileName = "https://github.com/Avaluate/MultipleRobloxInstances/wiki",
                 UseShellExecute = true });

        private void TelegramIcon_MouseDown(object sender, MouseButtonEventArgs e)
            => Process.Start(new ProcessStartInfo
               { FileName = "https://t.me/maindabnow", UseShellExecute = true });

        private void DiscordIcon_MouseDown(object sender, MouseButtonEventArgs e)
            => Process.Start(new ProcessStartInfo
               { FileName = "https://maindab.org/discord", UseShellExecute = true });

        private void GitHubIcon_MouseDown(object sender, MouseButtonEventArgs e)
            => Process.Start(new ProcessStartInfo
               { FileName = "https://github.com/Avaluate/MultipleRobloxInstances",
                 UseShellExecute = true });
    }
}
