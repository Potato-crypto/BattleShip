using BattleShip.Client.Services;
using System;
using System.Windows;
using System.Windows.Media;

namespace BattleShip.Client
{
    public partial class AlertWindow : Window
    {
        private bool _isClosing = false;

        public AlertWindow(string title, string message, AlertType type)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            // Настраиваем в зависимости от типа
            switch (type)
            {
                case AlertType.Info:
                    IconText.Text = "ℹ️";
                    HeaderBorder.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Синий
                    break;
                case AlertType.Warning:
                    IconText.Text = "⚠️";
                    HeaderBorder.Background = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Желтый
                    break;
                case AlertType.Error:
                    IconText.Text = "❌";
                    HeaderBorder.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Красный
                    break;
                case AlertType.Success:
                    IconText.Text = "✅";
                    HeaderBorder.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Зеленый
                    break;
            }

            // Анимация появления
            this.Loaded += (s, e) =>
            {
                this.Opacity = 0;
                this.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1,
                        TimeSpan.FromSeconds(0.3)));
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWindow();
        }

        private void CloseWindow()
        {
            if (_isClosing) return;
            _isClosing = true;

            var animation = new System.Windows.Media.Animation.DoubleAnimation(0,
                TimeSpan.FromSeconds(0.2));
            animation.Completed += (s, _) =>
            {
                this.DialogResult = true;
                this.Close();
            };
            this.BeginAnimation(OpacityProperty, animation);
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        // ДОБАВЛЕНО: Защита от повторного закрытия
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;
                CloseWindow();
            }
            base.OnClosing(e);
        }
    }
}