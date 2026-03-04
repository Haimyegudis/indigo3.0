using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using IndiLogs_3._0.Services;

namespace IndiLogs_3._0.Views
{
    public partial class SnakeWindow : Window
    {
        private const int Size = 20; // Cell size
        private DispatcherTimer _timer = null!;

        // Game variables
        private List<Point> _snake = new();
        private Point _food;
        private int _score;
        private bool _isGameOver;

        // Smart movement management (to prevent bugs)
        private Point _currentDirection;
        private Queue<Point> _inputQueue = new(); // Movement instruction queue

        // High scores save path
        private string _scoreFile = AppPaths.SnakeScores;

        public SnakeWindow()
        {
            InitializeComponent();
            LoadHighScores();
            InitializeGame();
        }

        private void InitializeGame()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += GameLoop;
            StartNewGame();
        }

        private void StartNewGame()
        {
            // Set speed according to selected difficulty level
            if (DifficultySelector.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag.ToString(), out int speed))
            {
                _timer.Interval = TimeSpan.FromMilliseconds(speed);
            }
            else
            {
                _timer.Interval = TimeSpan.FromMilliseconds(100);
            }

            // Reset the snake (starts at center)
            _snake = new List<Point>
            {
                new Point(100, 100),
                new Point(80, 100),
                new Point(60, 100)
            };

            _score = 0;
            ScoreText.Text = "0";
            _isGameOver = false;
            Overlay.Visibility = Visibility.Collapsed;

            // Reset directions
            _currentDirection = new Point(1, 0); // Start moving right
            _inputQueue = new Queue<Point>();

            SpawnFood();
            Draw();
            _timer.Start();
        }

        // Game loop - runs every X milliseconds
        private void GameLoop(object? sender, EventArgs e)
        {
            if (_isGameOver) return;

            // 1. Dequeue the next direction from the queue (if the user pressed a key)
            // This is the fix for the rapid key-press bug!
            if (_inputQueue.Count > 0)
            {
                _currentDirection = _inputQueue.Dequeue();
            }

            // 2. Calculate the new head position
            Point currentHead = _snake[0];
            Point newHead = new Point(
                currentHead.X + (_currentDirection.X * Size),
                currentHead.Y + (_currentDirection.Y * Size)
            );

            // 3. Check wall collision
            if (newHead.X < 0 || newHead.X >= GameCanvas.ActualWidth ||
                newHead.Y < 0 || newHead.Y >= GameCanvas.ActualHeight)
            {
                EndGame();
                return;
            }

            // 4. Check self-collision
            // Check up to Count-1 because the tail is about to move, so entering its cell is allowed
            for (int i = 0; i < _snake.Count - 1; i++)
            {
                if (IsPointsEqual(_snake[i], newHead))
                {
                    EndGame();
                    return;
                }
            }

            // 5. Add the new head
            _snake.Insert(0, newHead);

            // 6. Check food consumption
            if (IsPointsEqual(newHead, _food))
            {
                _score++;
                ScoreText.Text = _score.ToString();
                SpawnFood();
                // Don't remove the tail -> the snake grows
            }
            else
            {
                // Normal movement -> remove the tail
                _snake.RemoveAt(_snake.Count - 1);
            }

            Draw();
        }

        // Using PreviewKeyDown ensures the window receives the key before other controls
        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_isGameOver)
            {
                if (e.Key == Key.R) StartNewGame();
                if (e.Key == Key.Escape) Close();
                return;
            }

            Point nextDir = new Point(0, 0);
            bool isArrowKey = true;

            switch (e.Key)
            {
                case Key.Up: nextDir = new Point(0, -1); break;
                case Key.Down: nextDir = new Point(0, 1); break;
                case Key.Left: nextDir = new Point(-1, 0); break;
                case Key.Right: nextDir = new Point(1, 0); break;
                case Key.Escape: Close(); return;
                default: isArrowKey = false; break;
            }

            if (!isArrowKey) return;

            // Prevent reset bug: mark the key as handled so the ComboBox doesn't react
            e.Handled = true;

            // Calculate the last known direction (from the queue or from the snake)
            Point lastKnownDir = _inputQueue.Count > 0 ? _inputQueue.Last() : _currentDirection;

            // Prevent U-turn (e.g., right then left)
            if ((lastKnownDir.X + nextDir.X == 0) && (lastKnownDir.Y + nextDir.Y == 0))
            {
                return;
            }

            // Limit queue size to prevent delay if keys are pressed frantically
            if (_inputQueue.Count < 2)
            {
                _inputQueue.Enqueue(nextDir);
            }
        }

        private void SpawnFood()
        {
            if (GameCanvas.ActualWidth == 0) return;

            Random r = new Random();
            int maxX = (int)(GameCanvas.ActualWidth / Size);
            int maxY = (int)(GameCanvas.ActualHeight / Size);

            while (true)
            {
                int x = r.Next(0, maxX) * Size;
                int y = r.Next(0, maxY) * Size;
                Point p = new Point(x, y);

                // Make sure the food doesn't spawn on the snake
                bool onSnake = false;
                foreach (var part in _snake)
                {
                    if (IsPointsEqual(part, p)) { onSnake = true; break; }
                }

                if (!onSnake)
                {
                    _food = p;
                    break;
                }
            }
        }

        private void Draw()
        {
            GameCanvas.Children.Clear();

            // Draw food
            Ellipse foodParams = new Ellipse
            {
                Width = Size,
                Height = Size,
                Fill = Brushes.Red,
                Effect = new DropShadowEffect { Color = Colors.Red, BlurRadius = 8, ShadowDepth = 0 }
            };
            Canvas.SetLeft(foodParams, _food.X);
            Canvas.SetTop(foodParams, _food.Y);
            GameCanvas.Children.Add(foodParams);

            // Draw snake
            for (int i = 0; i < _snake.Count; i++)
            {
                Rectangle rect = new Rectangle
                {
                    Width = Size,
                    Height = Size,
                    Fill = (i == 0) ? Brushes.Lime : Brushes.ForestGreen, // Head in a different color
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(rect, _snake[i].X);
                Canvas.SetTop(rect, _snake[i].Y);
                GameCanvas.Children.Add(rect);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }

        private void EndGame()
        {
            _isGameOver = true;
            _timer.Stop();
            FinalScoreText.Text = $"Final Score: {_score}";
            SaveScore();
            LoadHighScores();
            Overlay.Visibility = Visibility.Visible;
        }

        private void RestartBtn_Click(object? sender, RoutedEventArgs e) => StartNewGame();

        private void DifficultySelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // If the game is running and difficulty is changed, restart
            if (_timer != null) StartNewGame();
        }

        // --- High Scores Management ---

        private void SaveScore()
        {
            if (_score == 0) return;
            try
            {
                List<int> scores = new List<int>();
                if (File.Exists(_scoreFile))
                    scores = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(_scoreFile), AppConstants.SafeJsonSettings) ?? new List<int>();

                scores.Add(_score);
                scores = scores.OrderByDescending(s => s).Take(3).ToList(); // Keep top 3

                // Create directory if it doesn't exist
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_scoreFile));
                File.WriteAllText(_scoreFile, JsonConvert.SerializeObject(scores));
            }
            catch (Exception ex) { AppLogger.Error("Save scores failed", ex); }
        }

        private void LoadHighScores()
        {
            try
            {
                if (File.Exists(_scoreFile))
                {
                    var scores = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(_scoreFile), AppConstants.SafeJsonSettings);
                    HighScoresText.Text = string.Join("\n", scores.Select((s, i) => $"#{i + 1} : {s}"));
                }
            }
            catch (Exception ex) { AppLogger.Error("LoadHighScores failed", ex); HighScoresText.Text = "Error"; }
        }

        // Helper for comparing points (because Point uses Double)
        private bool IsPointsEqual(Point p1, Point p2)
        {
            return Math.Abs(p1.X - p2.X) < 0.1 && Math.Abs(p1.Y - p2.Y) < 0.1;
        }
    }
}