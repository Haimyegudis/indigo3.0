// BILINGUAL-HEADER-START
// EN: File: IndigoInvadersWindow.Gameplay.cs - Auto-added bilingual header.
// HE: File: IndigoInvadersWindow.Gameplay.cs - Auto-added bilingual header.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IndiLogs_3._0.Views
{
    public partial class IndigoInvadersWindow
    {
        // --- Aliens ---

        private void SpawnInvaders()
        {
            _invaders.Clear();
            _alienSpeedX = 1.5 + (_level * 0.5); // Speed increases each level
            _alienDirection = 1;

            double startX = 50;
            double startY = 50;
            double gap = 15;
            double width = 30;
            double height = 20;

            for (int row = 0; row < AlienRows; row++)
            {
                for (int col = 0; col < AlienCols; col++)
                {
                    var color = row == 0 ? Brushes.Purple : (row == 1 ? Brushes.MediumOrchid : Brushes.Violet);

                    var alienBody = new Rectangle
                    {
                        Width = width,
                        Height = height,
                        Fill = color,
                        RadiusX = 5,
                        RadiusY = 5,
                        Tag = "Alien"
                    };

                    Canvas.SetLeft(alienBody, startX + col * (width + gap));
                    Canvas.SetTop(alienBody, startY + row * (height + gap));

                    GameCanvas.Children.Add(alienBody);
                    _invaders.Add(new Invader { UIElement = alienBody });
                }
            }
        }

        private void MoveAliens()
        {
            bool hitEdge = false;
            double rightEdge = GameCanvas.ActualWidth - 40;

            foreach (var invader in _invaders)
            {
                double x = Canvas.GetLeft(invader.UIElement);
                if ((_alienDirection == 1 && x > rightEdge) || (_alienDirection == -1 && x < 10))
                {
                    hitEdge = true;
                    break; // One touching the wall is enough
                }
            }

            if (hitEdge)
            {
                _alienDirection *= -1;
                foreach (var invader in _invaders)
                {
                    double y = Canvas.GetTop(invader.UIElement);
                    Canvas.SetTop(invader.UIElement, y + AlienDropDistance);

                    // If the aliens reached too far down
                    if (y + AlienDropDistance > Canvas.GetTop(_player) - 30)
                    {
                        GameOver();
                        return;
                    }
                }
                // Small acceleration on each descent
                _alienSpeedX *= 1.05;
            }
            else
            {
                foreach (var invader in _invaders)
                {
                    double x = Canvas.GetLeft(invader.UIElement);
                    Canvas.SetLeft(invader.UIElement, x + (_alienSpeedX * _alienDirection));
                }
            }
        }

        private void AlienShootLogic()
        {
            // Shooting chance increases as fewer aliens remain and as the level gets higher
            int chance = 100 - (_level * 2);
            if (chance < 20) chance = 20;

            var random = new Random();
            if (random.Next(0, chance) == 0 && _invaders.Count > 0)
            {
                // Pick a random alien to shoot
                var shooter = _invaders[random.Next(_invaders.Count)];
                ShootAlienBullet(shooter.UIElement);
            }
        }

        // --- Bullets & Collisions ---

        private void ShootPlayerBullet()
        {
            var bullet = new Rectangle { Width = 4, Height = 10, Fill = Brushes.Cyan };
            Canvas.SetLeft(bullet, Canvas.GetLeft(_player) + 18);
            Canvas.SetTop(bullet, Canvas.GetTop(_player) - 10);
            GameCanvas.Children.Add(bullet);
            _playerBullets.Add(bullet);
        }

        private void ShootAlienBullet(Rectangle alien)
        {
            var bullet = new Rectangle { Width = 4, Height = 10, Fill = Brushes.Red };
            Canvas.SetLeft(bullet, Canvas.GetLeft(alien) + 13);
            Canvas.SetTop(bullet, Canvas.GetTop(alien) + 20);
            GameCanvas.Children.Add(bullet);
            _alienBullets.Add(bullet);
        }

        private void MoveBullets()
        {
            // Player Bullets (Up)
            for (int i = _playerBullets.Count - 1; i >= 0; i--)
            {
                var b = _playerBullets[i];
                double y = Canvas.GetTop(b);
                if (y < 0)
                {
                    GameCanvas.Children.Remove(b);
                    _playerBullets.RemoveAt(i);
                }
                else
                {
                    Canvas.SetTop(b, y - BulletSpeed);
                }
            }

            // Alien Bullets (Down)
            for (int i = _alienBullets.Count - 1; i >= 0; i--)
            {
                var b = _alienBullets[i];
                double y = Canvas.GetTop(b);
                if (y > GameCanvas.ActualHeight)
                {
                    GameCanvas.Children.Remove(b);
                    _alienBullets.RemoveAt(i);
                }
                else
                {
                    Canvas.SetTop(b, y + (BulletSpeed * 0.6)); // Alien bullets are slower
                }
            }
        }

        private void CheckCollisions()
        {
            Rect playerRect = new Rect(Canvas.GetLeft(_player), Canvas.GetTop(_player), _player.Width, _player.Height);

            // 1. Player bullets hitting aliens
            for (int i = _playerBullets.Count - 1; i >= 0; i--)
            {
                var bullet = _playerBullets[i];
                Rect bulletRect = new Rect(Canvas.GetLeft(bullet), Canvas.GetTop(bullet), bullet.Width, bullet.Height);
                bool hit = false;

                for (int j = _invaders.Count - 1; j >= 0; j--)
                {
                    var alien = _invaders[j].UIElement;
                    Rect alienRect = new Rect(Canvas.GetLeft(alien), Canvas.GetTop(alien), alien.Width, alien.Height);

                    if (bulletRect.IntersectsWith(alienRect))
                    {
                        // Alien explosion
                        GameCanvas.Children.Remove(alien);
                        _invaders.RemoveAt(j);
                        hit = true;
                        _score += 10 * _level;
                        break;
                    }
                }

                if (hit)
                {
                    GameCanvas.Children.Remove(bullet);
                    _playerBullets.RemoveAt(i);
                    UpdateUI();
                }
            }

            // 2. Alien bullets hitting the player
            for (int i = _alienBullets.Count - 1; i >= 0; i--)
            {
                var bullet = _alienBullets[i];
                Rect bulletRect = new Rect(Canvas.GetLeft(bullet), Canvas.GetTop(bullet), bullet.Width, bullet.Height);

                if (bulletRect.IntersectsWith(playerRect))
                {
                    GameCanvas.Children.Remove(bullet);
                    _alienBullets.RemoveAt(i);
                    PlayerHit();
                }
            }

            // 3. Aliens touching the player
            foreach (var invader in _invaders)
            {
                var alien = invader.UIElement;
                Rect alienRect = new Rect(Canvas.GetLeft(alien), Canvas.GetTop(alien), alien.Width, alien.Height);
                if (alienRect.IntersectsWith(playerRect))
                {
                    GameOver();
                    return;
                }
            }
        }

        private void PlayerHit()
        {
            _lives--;
            UpdateUI();

            // Hit effect (flash)
            _player.Opacity = 0.5;
            var dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            dt.Tick += (s, e) => { _player.Opacity = 1; dt.Stop(); };
            dt.Start();

            if (_lives <= 0)
            {
                GameOver();
            }
        }
    }
}
