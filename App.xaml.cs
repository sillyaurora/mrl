using System.Windows;
using System.Windows.Media.Animation;

namespace MultipleRobloxInstances
{
    public partial class App : Application
    {
        private async void App_OnStartup(object sender, StartupEventArgs e)
        {
            // Show splash immediately so the user sees something while we init.
            var splash = new SplashWindow();
            splash.Show();

            // Create the main window (InitializeComponent only — no heavy work yet).
            var main = new MainWindow();

            // Run the full init sequence. The splash callback updates progress.
            string? initError = null;
            try
            {
                await main.InitializeAsync((msg, step, total, hint) =>
                    splash.SetStatus(msg, step, total, hint));
            }
            catch (Exception ex)
            {
                initError = ex.Message;
            }

            // Transition: fade main in while fading splash out.
            main.Opacity = 0;
            main.Show();

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            main.BeginAnimation(Window.OpacityProperty, fadeIn);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (_, _) => splash.Close();
            splash.BeginAnimation(Window.OpacityProperty, fadeOut);

            // Surface any init exception that escaped the try/catch inside InitializeAsync.
            if (initError != null)
                main.ReportError($"Startup: {initError}");
        }
    }
}
