using System.Windows;
using System.Windows.Media.Animation;

namespace BattleShip.Client
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWindow();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWindow();
        }

        private void GoogleLoginButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Это сделает позже Наташа",
                "В разработке",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void EmailLoginButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Это сделает позже Наташа",
                "В разработке",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Это сделает позже Наташа",
                "В разработке",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CloseWindow()
        {
            // Анимация закрытия окна
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.2)
            };
            
            fadeOut.Completed += (s, _) => this.Close();
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}