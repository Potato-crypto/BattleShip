using System;
using System.Windows;
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
            
            // Открываем окно входа
            System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Открываем окно входа как модальное
                    LoginWindow loginWindow = new LoginWindow();
                    loginWindow.Owner = this;
                    loginWindow.ShowDialog();
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
            
            // Закрываем окно с результатом true
            this.DialogResult = true;
            this.Close();
        }
    }
}