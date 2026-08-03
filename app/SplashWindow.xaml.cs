using System.Windows;
using System.Windows.Media.Animation;

namespace MultipleRobloxInstances
{
    public partial class SplashWindow : Window
    {
        // Full inner width of the progress track (window 522 - left margin 28 - right margin 28)
        private const double TrackWidth = 466.0;

        public SplashWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Updates the splash status text and animates the progress bar.
        /// Safe to call from any thread.
        /// </summary>
        /// <param name="message">Status line shown below the title.</param>
        /// <param name="step">Current step number (1-based).</param>
        /// <param name="totalSteps">Total number of steps.</param>
        /// <param name="launcherHint">Optional: text shown in the bottom-right hint.</param>
        public void SetStatus(string message, int step, int totalSteps, string? launcherHint = null)
        {
            Dispatcher.Invoke(() =>
            {
                SplashStatus.Text = message;

                double targetWidth = TrackWidth * step / totalSteps;

                // Animate both the blurred glow and the sharp bar together
                var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
                AnimateWidth(ProgressFill,      targetWidth, ease);
                AnimateWidth(ProgressFillSharp, targetWidth, ease);

                if (launcherHint != null)
                    LauncherHint.Text = launcherHint;
            });
        }

        private static void AnimateWidth(FrameworkElement el, double to, IEasingFunction ease)
        {
            var anim = new DoubleAnimation(el.Width, to, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = ease
            };
            el.BeginAnimation(WidthProperty, anim);
        }
    }
}
