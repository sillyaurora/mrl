using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MultipleRobloxInstances.Resources
{
    public partial class RobloxInstance : UserControl
    {
        public RobloxInstance()
        {
            InitializeComponent();

            // Show/hide the placeholder as the user types
            NicknameBox.TextChanged += (_, _) =>
                NicknamePlaceholder.Visibility =
                    string.IsNullOrEmpty(NicknameBox.Text)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
        }

        /// <summary>
        /// Called by MainWindow.ReadFromLog when the watched process exits with a
        /// non-zero exit code. Updates the card to show crash state: keeps it
        /// visible, shows the crash badge, surfaces the Relaunch button, and
        /// relabels Kill as "Dismiss" so the user can clear it.
        /// </summary>
        public void SetCrashed()
        {
            // Must run on the UI thread — callers inside Dispatcher.InvokeAsync
            // already guarantee this, but guard anyway.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SetCrashed);
                return;
            }

            CrashBadge.Visibility  = Visibility.Visible;
            RelaunchBtn.Visibility = Visibility.Visible;
            KilInstance.Content    = "Dismiss";

            // Fade crash badge in
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
            CrashBadge.BeginAnimation(OpacityProperty, anim);
        }

        // ── Animation helpers (same API as MainWindow, kept here so the card is
        //    self-contained and doesn't need a back-reference to MainWindow) ──────

        public void Fade(DependencyObject el, double from, double to, double secs)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(secs))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, el);
            Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
            var sb = new Storyboard(); sb.Children.Add(anim); sb.Begin();
        }

        public void Move(DependencyObject el, Thickness from, Thickness to, double secs)
        {
            var anim = new ThicknessAnimation(from, to, TimeSpan.FromSeconds(secs))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, el);
            Storyboard.SetTargetProperty(anim, new PropertyPath(MarginProperty));
            var sb = new Storyboard(); sb.Children.Add(anim); sb.Begin();
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
    }
}
