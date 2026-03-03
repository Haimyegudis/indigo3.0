#nullable disable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IndiLogs_3._0.Views
{
    public partial class SplashWindow : Window
    {
        private class Particle
        {
            public Ellipse Shape;
            public double X, Y;
            public double Vx, Vy;
            public double Life;
            public double Decay;
            public double Size;
        }

        private readonly List<Particle> _particles = new List<Particle>();

        // Physics timer – started INSIDE EmitBurst (after particles exist),
        // never started in OnLoaded. Previously it started at t=0, found
        // 0 particles, stopped itself, and the system never ran.
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Random _rng = new Random();

        // Bright, saturated palette – clearly visible on dark background
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0,   210, 255),
            Color.FromRgb(120, 230, 255),
            Color.FromRgb(255, 255, 255),
            Color.FromRgb(60,  160, 255),
            Color.FromRgb(210, 160, 255),
            Color.FromRgb(0,   255, 200),
            Color.FromRgb(255, 220, 80),
            Color.FromRgb(80,  200, 120),
        };

        private double _cx, _cy;
        private int _dotTick;
        private readonly DispatcherTimer _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };

        public SplashWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cx = ActualWidth  / 2.0;
            _cy = ActualHeight / 2.0;

            // Text reveals + ring animations → handled by XAML Storyboard (EventTrigger on Loaded)

            // Particle burst: timer starts INSIDE EmitBurst, not here
            Delay(350, EmitBurst);

            // Dot pulse
            _dotTimer.Tick += OnDotTick;
            _dotTimer.Start();

            // Fade-out after 2.8 s
            Delay(2800, BeginClose);
        }

        private void EmitBurst()
        {
            const int count = 220;
            for (int i = 0; i < count; i++)
            {
                double angle = _rng.NextDouble() * Math.PI * 2;
                double speed = 2.5 + _rng.NextDouble() * 8.5;
                double size  = 5.0 + _rng.NextDouble() * 13.0;  // 5–18 px
                double life  = 0.7 + _rng.NextDouble() * 0.3;
                double decay = 0.005 + _rng.NextDouble() * 0.013;

                var ellipse = new Ellipse
                {
                    Width   = size,
                    Height  = size,
                    Fill    = new SolidColorBrush(Palette[_rng.Next(Palette.Length)]),
                    Opacity = life
                };

                ParticleCanvas.Children.Add(ellipse);
                Canvas.SetLeft(ellipse, _cx - size / 2);
                Canvas.SetTop(ellipse,  _cy - size / 2);

                _particles.Add(new Particle
                {
                    Shape = ellipse,
                    X     = _cx,
                    Y     = _cy,
                    Vx    = Math.Cos(angle) * speed,
                    Vy    = Math.Sin(angle) * speed,
                    Life  = life,
                    Decay = decay,
                    Size  = size
                });
            }

            // Start physics NOW – particles exist in the list
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            const double gravity = 0.045;
            var dead = new List<Particle>();

            foreach (var p in _particles)
            {
                p.Vy   += gravity;
                p.X    += p.Vx;
                p.Y    += p.Vy;
                p.Life -= p.Decay;
                p.Vx   *= 0.990;
                p.Vy   *= 0.990;

                if (p.Life <= 0)
                {
                    dead.Add(p);
                    ParticleCanvas.Children.Remove(p.Shape);
                    continue;
                }

                Canvas.SetLeft(p.Shape, p.X - p.Size / 2);
                Canvas.SetTop(p.Shape,  p.Y - p.Size / 2);
                p.Shape.Opacity = Math.Max(0, p.Life);
            }

            foreach (var p in dead) _particles.Remove(p);
            if (_particles.Count == 0) _timer.Stop();
        }

        private void OnDotTick(object sender, EventArgs e)
        {
            var dots = new[] { Dot1, Dot2, Dot3 };
            foreach (var d in dots) AnimateOpacity(d, d.Opacity, 0.25, 300);
            AnimateOpacity(dots[_dotTick % 3], 0.25, 1.0, 200);
            _dotTick++;
        }

        private void BeginClose()
        {
            _dotTimer.Stop();
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, __) => { _timer.Stop(); Close(); };
            RootBorder.BeginAnimation(OpacityProperty, anim);
        }

        private static void AnimateOpacity(UIElement el, double from, double to, int ms)
        {
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void Delay(int ms, Action action)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, __) => { t.Stop(); action(); };
            t.Start();
        }
    }
}
