// BILINGUAL-HEADER-START
// EN: File: IndigoInvadersWindow.xaml.cs - Auto-added bilingual header.
// HE: File: IndigoInvadersWindow.xaml.cs - Auto-added bilingual header.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IndiLogs_3._0.Views
{
    public partial class IndigoInvadersWindow : Window
    {
        // Constants and sizing
        private const double PlayerSpeed = 7;
        private const double BulletSpeed = 12;
        private const double AlienDropDistance = 15;
        private const int AlienRows = 4;
        private const int AlienCols = 9;

        // Timer and controls
        private DispatcherTimer _gameTimer = null!;
        private bool _moveLeft, _moveRight, _isShooting;
        private bool _gameRunning = false;

        // Game objects
        private Rectangle _player = null!;
        private List<Rectangle> _playerBullets = new();
        private List<Rectangle> _alienBullets = new();
        private List<Invader> _invaders = new();

        // Game status
        private int _score = 0;
        private int _lives = 3;
        private int _level = 1;
        private double _alienSpeedX = 2;
        private int _alienDirection = 1; // 1 right, -1 left
        private DateTime _lastShotTime = DateTime.MinValue;

        public IndigoInvadersWindow()
        {
            InitializeComponent();
            GenerateStars();

            _gameTimer = new DispatcherTimer();
            _gameTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            _gameTimer.Tick += GameLoop;
        }

        private void StartGame_Click(object? sender, RoutedEventArgs e)
        {
            ResetGame();
        }

        private void ResetGame()
        {
            GameCanvas.Children.Clear();
            _playerBullets.Clear();
            _alienBullets.Clear();
            _invaders.Clear();

            _score = 0;
            _lives = 3;
            _level = 1;
            UpdateUI();

            CreatePlayer();
            SpawnInvaders();

            Overlay.Visibility = Visibility.Collapsed;
            _gameRunning = true;
            _gameTimer.Start();
            this.Focus();
        }

        private void NextLevel()
        {
            _level++;
            _playerBullets.ForEach(b => GameCanvas.Children.Remove(b));
            _playerBullets.Clear();
            _alienBullets.ForEach(b => GameCanvas.Children.Remove(b));
            _alienBullets.Clear();

            SpawnInvaders();
            UpdateUI();
        }

        protected override void OnClosed(EventArgs e)
        {
            _gameRunning = false;
            _gameTimer.Stop();
            base.OnClosed(e);
        }

        private void GameOver()
        {
            _gameRunning = false;
            _gameTimer.Stop();
            OverlayTitle.Text = "GAME OVER";
            OverlayMessage.Text = $"Final Score: {_score}";
            Overlay.Visibility = Visibility.Visible;
        }

        // --- Game Logic Loop ---

        private void GameLoop(object? sender, EventArgs e)
        {
            if (!_gameRunning) return;

            MovePlayer();
            MoveBullets();
            MoveAliens();
            AlienShootLogic();
            CheckCollisions();

            if (_invaders.Count == 0)
            {
                NextLevel();
            }
        }

        // --- Player ---

        private void CreatePlayer()
        {
            _player = new Rectangle
            {
                Width = 40,
                Height = 20,
                Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)), // Primary Blue
                RadiusX = 3,
                RadiusY = 3
            };

            // Small cannon above the player
            var cannon = new Rectangle { Width = 6, Height = 8, Fill = Brushes.LightBlue };

            Canvas.SetLeft(_player, (GameCanvas.ActualWidth / 2) - 20);
            Canvas.SetTop(_player, GameCanvas.ActualHeight - 50);

            GameCanvas.Children.Add(_player);
        }

        private void MovePlayer()
        {
            double currentLeft = Canvas.GetLeft(_player);

            if (_moveLeft && currentLeft > 0)
                Canvas.SetLeft(_player, currentLeft - PlayerSpeed);

            if (_moveRight && currentLeft < (GameCanvas.ActualWidth - _player.Width))
                Canvas.SetLeft(_player, currentLeft + PlayerSpeed);

            if (_isShooting && (DateTime.Now - _lastShotTime).TotalMilliseconds > 400)
            {
                ShootPlayerBullet();
                _lastShotTime = DateTime.Now;
            }
        }

        private void UpdateUI()
        {
            ScoreText.Text = _score.ToString();
            LivesText.Text = _lives.ToString();
            LevelText.Text = $"LEVEL {_level}";
        }

        // --- Input Handling ---

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left) _moveLeft = true;
            if (e.Key == Key.Right) _moveRight = true;
            if (e.Key == Key.Space) _isShooting = true;
            if (e.Key == Key.Escape) Close();
        }

        private void Window_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left) _moveLeft = false;
            if (e.Key == Key.Right) _moveRight = false;
            if (e.Key == Key.Space) _isShooting = false;
        }

        // --- Utils ---

        private void GenerateStars()
        {
            Random r = new Random();
            for (int i = 0; i < 50; i++)
            {
                Ellipse star = new Ellipse
                {
                    Width = 2,
                    Height = 2,
                    Fill = Brushes.White,
                    Opacity = r.NextDouble()
                };
                Canvas.SetLeft(star, r.Next(0, 800));
                Canvas.SetTop(star, r.Next(0, 600));
                StarFieldCanvas.Children.Add(star);
            }
        }

        private class Invader
        {
            public Rectangle UIElement { get; set; } = null!;
        }
    }
}