using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace BattleShip.Client
{
    public partial class AuthWindow : Window
    {
        public AuthWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Анимация появления окна
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.3)
            };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        // Метод для перемещения окна
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // Метод для закрытия окна
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // Анимация нажатия кнопки
            DoubleAnimation scaleAnimation = new DoubleAnimation
            {
                To = 0.95,
                Duration = TimeSpan.FromSeconds(0.1),
                AutoReverse = true
            };

            LoginButton.BeginAnimation(WidthProperty, scaleAnimation);
            
            // Открываем окно входа как модальное (блокирует только AuthWindow)
            System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    LoginWindow loginWindow = new LoginWindow();
                    loginWindow.Owner = this; // Устанавливаем владельца
                    loginWindow.ShowDialog(); // Блокирует только AuthWindow
                });
            });
        }

        private void GuestButton_Click(object sender, RoutedEventArgs e)
        {
            // Создаем случайное имя для гостя
            string guestName = $"Гость_{new Random().Next(1000, 9999)}";
            
            // Сохраняем информацию о гостевом режиме
            Application.Current.Properties["IsGuest"] = true;
            Application.Current.Properties["Username"] = guestName;
            
            // Закрываем окно
            this.Close();
        }
    }
}