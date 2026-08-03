using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MultipleRobloxInstances
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Catch ANY unhandled exception and show it — during dev this is
            // essential because a silent crash tells you nothing.
            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    ex.Exception.ToString(),
                    "Unhandled UI Exception",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    ex.ExceptionObject?.ToString() ?? "Unknown error",
                    "Unhandled Exception",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            };

            TaskScheduler.UnobservedTaskException += (_, ex) =>
            {
                MessageBox.Show(
                    ex.Exception.ToString(),
                    "Unhandled Task Exception",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.SetObserved();
            };

            base.OnStartup(e);
            RunAsync();
        }

        private async void RunAsync()
        {
            SplashWindow? splash = null;
            try
            {
                splash = new SplashWindow();
                splash.Show();

                var main = new MainWindow();

                await main.InitializeAsync((msg, step, total, hint) =>
                    splash.SetStatus(msg, step, total, hint));

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
            }
            catch (Exception ex)
            {
                splash?.Close();
                MessageBox.Show(
                    ex.ToString(),
                    "Startup Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
