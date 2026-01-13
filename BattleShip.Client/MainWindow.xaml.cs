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
            // Открываем окно авторизации как модальное
            AuthWindow authWindow = new AuthWindow();
            
            // Показываем модальное окно
            bool? result = authWindow.ShowDialog();
            
            // Если пользователь нажал "Играть как гость"
            if (result == true)
            {
                // Проверяем, установлено ли свойство гостя
                if (Application.Current.Properties.Contains("IsGuest") && 
                    (bool)Application.Current.Properties["IsGuest"])
                {
                    // Открываем GameWindow
                    GameWindow gameWindow = new GameWindow();
                    gameWindow.Show();
                    
                    // Закрываем MainWindow
                    this.Close();
                }
            }
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