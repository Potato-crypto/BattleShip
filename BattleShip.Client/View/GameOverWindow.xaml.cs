using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BattleShip.Client
{
    public partial class GameOverWindow : Window
    {
        public bool PlayAgain { get; private set; } = false;
        
        public GameOverWindow(string winner, string opponentName, PlayerStats stats)
        {
            InitializeComponent();
            
            // Настраиваем окно в зависимости от результата
            InitializeWindow(winner, opponentName, stats);
            
            // Анимация появления
            this.Loaded += (s, e) => 
            {
                this.Opacity = 0;
                this.BeginAnimation(OpacityProperty, 
                    new System.Windows.Media.Animation.DoubleAnimation(1, 
                        TimeSpan.FromSeconds(0.3)));
            };
        }
        
        private void InitializeWindow(string winner, string opponentName, PlayerStats stats)
        {
            bool isPlayerWinner = winner == "player";
            
            // Настраиваем заголовок и иконку
            if (isPlayerWinner)
            {
                TitleText.Text = "ПОБЕДА!";
                ResultText.Text = "Вы победили!";
                IconText.Text = "🏆";
                ResultIcon.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Зеленый
            }
            else
            {
                TitleText.Text = "ПОРАЖЕНИЕ";
                ResultText.Text = $"Победил: {opponentName}";
                IconText.Text = "💀";
                ResultIcon.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Красный
            }
            
            // Добавляем статистику
            AddStatItem("🔫 Выстрелов:", stats.TotalShots.ToString());
            AddStatItem("🎯 Попаданий:", stats.Hits.ToString());
            AddStatItem("❌ Промахов:", stats.Misses.ToString());
            AddStatItem("📊 Точность:", $"{stats.Accuracy:F1}%");
            
            if (stats.TotalShots > 0)
            {
                double efficiency = (double)stats.Hits / stats.TotalShots * 100;
                AddStatItem("⭐ Эффективность:", $"{efficiency:F1}%");
            }
        }
        
        private void AddStatItem(string label, string value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(100) });
            
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(labelText, 0);
            
            var valueText = new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(valueText, 1);
            
            grid.Children.Add(labelText);
            grid.Children.Add(valueText);
            
            StatsPanel.Children.Add(grid);
        }
        
        private void PlayAgainButton_Click(object sender, RoutedEventArgs e)
        {
            PlayAgain = true;
            CloseWindow();
        }
        
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            PlayAgain = false;
            CloseWindow();
        }
        
        private void CloseWindow()
        {
            // Анимация закрытия окна
            var animation = new System.Windows.Media.Animation.DoubleAnimation(0, 
                TimeSpan.FromSeconds(0.2));
            animation.Completed += (s, _) => this.DialogResult = true;
            this.BeginAnimation(OpacityProperty, animation);
        }
        
        // Для красоты добавим возможность перетаскивания окна
        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }
    }
}

