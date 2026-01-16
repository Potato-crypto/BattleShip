using System.Windows;

namespace BattleShip.Client
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            // Открываем окно авторизации как НЕмодальное (не блокирующее)
            AuthWindow authWindow = new AuthWindow();
            
            // Подписываемся на событие закрытия окна
            authWindow.Closed += (s, args) =>
            {
                // Проверяем, был ли выбран гость
                if (Application.Current.Properties.Contains("IsGuest") && 
                    (bool)Application.Current.Properties["IsGuest"])
                {
                    // Открываем GameWindow
                    GameWindow gameWindow = new GameWindow();
                    gameWindow.Show();
                    
                    // Закрываем MainWindow
                    this.Close();
                }
            };
            
            // Показываем как немодальное окно
            authWindow.Show();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Вы уверены, что хотите выйти?", 
                "Выход", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }
    }
}