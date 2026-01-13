using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace BattleShip.Client
{
    public partial class GameWindow : Window
    {
        private const int GridSize = 10;
        private const int CellSize = 35;
        private bool _isSearching = false;
        private GameLogic _gameLogic;
        private Dictionary<string, Border> _playerCells = new Dictionary<string, Border>();

        public GameWindow()
        {
            InitializeComponent();
            _gameLogic = new GameLogic();
            InitializeGameBoards();
            
            // Блокируем кнопки поиска пока корабли не расставлены
            UpdateButtonsState();
        }

        private void InitializeGameBoards()
        {
            InitializeBoard(YourBoardGrid, true);
            InitializeBoard(OpponentBoardGrid, false);
            UpdateShipsInfo();
        }

        private void InitializeBoard(Grid boardGrid, bool isYourBoard)
        {
            // Очищаем поле
            boardGrid.Children.Clear();
            boardGrid.RowDefinitions.Clear();
            boardGrid.ColumnDefinitions.Clear();
            if (isYourBoard) _playerCells.Clear();

            // Создаем строки и столбцы (10x10 + заголовки)
            for (int i = 0; i <= GridSize; i++)
            {
                boardGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(CellSize) });
                boardGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(CellSize) });
            }

            // Добавляем буквы для столбцов (A-J)
            for (int col = 0; col < GridSize; col++)
            {
                TextBlock letter = new TextBlock
                {
                    Text = ((char)('А' + col)).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(letter, 0);
                Grid.SetColumn(letter, col + 1);
                boardGrid.Children.Add(letter);
            }

            // Добавляем цифры для строк (1-10)
            for (int row = 0; row < GridSize; row++)
            {
                TextBlock number = new TextBlock
                {
                    Text = (row + 1).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(number, row + 1);
                Grid.SetColumn(number, 0);
                boardGrid.Children.Add(number);
            }

            // Создаем игровые клетки
            for (int row = 0; row < GridSize; row++)
            {
                for (int col = 0; col < GridSize; col++)
                {
                    Border cell = CreateCell(row, col, isYourBoard);
                    Grid.SetRow(cell, row + 1);
                    Grid.SetColumn(cell, col + 1);
                    boardGrid.Children.Add(cell);
                    
                    if (isYourBoard)
                    {
                        _playerCells[$"{row},{col}"] = cell;
                    }
                }
            }
        }

        private Border CreateCell(int row, int col, bool isYourBoard)
        {
            Border cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                Tag = $"{row},{col}",
                Cursor = Cursors.Hand
            };

            if (isYourBoard)
            {
                // Для своего поля - клик для размещения кораблей
                cell.MouseLeftButtonDown += YourCell_MouseLeftButtonDown;
                cell.MouseRightButtonDown += YourCell_MouseRightButtonDown;
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }
            else
            {
                // Для поля противника - выстрелы
                cell.MouseLeftButtonDown += OpponentCell_MouseLeftButtonDown;
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }

            return cell;
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;
            if (!_isSearching)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(60, 70, 80));
            }
        }

        private void Cell_MouseLeave(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;
            var coords = cell.Tag.ToString().Split(',');
            int row = int.Parse(coords[0]);
            int col = int.Parse(coords[1]);
            
            // Проверяем, есть ли здесь корабль
            bool hasShip = false;
            var shipCells = _gameLogic.GetPlayerShipCells();
            foreach (var shipCell in shipCells)
            {
                if (shipCell.row == row && shipCell.col == col)
                {
                    hasShip = true;
                    break;
                }
            }
            
            // Проверяем, является ли это клеткой текущего расставляемого корабля
            var currentShipCells = _gameLogic.GetCurrentShipBeingPlacedCells();
            foreach (var shipCell in currentShipCells)
            {
                if (shipCell.row == row && shipCell.col == col)
                {
                    // Клетка текущего корабля (более светлый цвет)
                    cell.Background = new SolidColorBrush(Color.FromRgb(106, 137, 204));
                    return;
                }
            }
            
            if (hasShip)
            {
                // Обычный корабль
                cell.Background = new SolidColorBrush(Color.FromRgb(74, 105, 189));
            }
            else
            {
                // Пустая клетка
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
            }
        }

        private void YourCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSearching || _gameLogic.AllShipsPlaced) return;
            
            var cell = (Border)sender;
            var coords = cell.Tag.ToString().Split(',');
            int row = int.Parse(coords[0]);
            int col = int.Parse(coords[1]);

            // Пытаемся поставить клетку корабля
            if (_gameLogic.TryPlaceShipCell(row, col))
            {
                // Обновляем отображение
                UpdateYourBoard();
                UpdateShipsInfo();
                UpdateButtonsState();
            }
            else
            {
                // Не удалось поставить клетку
                GameStatus.Text = "Нельзя поставить корабль здесь!";
            }
        }

        private void YourCell_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSearching || _gameLogic.AllShipsPlaced) return;
            
            if (_gameLogic.IsPlacingShip())
            {
                // Отменяем расстановку текущего корабля
                _gameLogic.CancelCurrentShipPlacement();
                
                // Обновляем отображение
                UpdateYourBoard();
                UpdateShipsInfo();
                UpdateButtonsState();
            }
            else
            {
                // Удаляем последний поставленный корабль
                _gameLogic.RemoveLastCell();
                
                // Обновляем отображение
                UpdateYourBoard();
                UpdateShipsInfo();
                UpdateButtonsState();
            }
        }

        private void OpponentCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSearching || !_gameLogic.AllShipsPlaced) return;

            var cell = (Border)sender;
            var coords = cell.Tag.ToString().Split(',');
            int row = int.Parse(coords[0]);
            int col = int.Parse(coords[1]);

            // Визуальная обратная связь при выстреле
            cell.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            
            // Заглушка для выстрела
            GameStatus.Text = $"Выстрел по клетке {((char)('А' + col))}{row + 1}";
        }

        private void UpdateYourBoard()
        {
            // Обновляем цвета всех клеток
            foreach (var kvp in _playerCells)
            {
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
                
                // Проверяем, является ли это клеткой текущего расставляемого корабля
                bool isCurrentShipCell = false;
                var currentShipCells = _gameLogic.GetCurrentShipBeingPlacedCells();
                foreach (var shipCell in currentShipCells)
                {
                    if (shipCell.row == row && shipCell.col == col)
                    {
                        // Клетка текущего корабля (более светлый цвет)
                        kvp.Value.Background = new SolidColorBrush(Color.FromRgb(106, 137, 204));
                        kvp.Value.BorderBrush = new SolidColorBrush(Color.FromRgb(140, 170, 230));
                        kvp.Value.BorderThickness = new Thickness(2);
                        isCurrentShipCell = true;
                        break;
                    }
                }
                
                if (isCurrentShipCell) continue;
                
                // Проверяем, есть ли здесь обычный корабль
                bool hasShip = false;
                var shipCells = _gameLogic.GetPlayerShipCells();
                foreach (var shipCell in shipCells)
                {
                    if (shipCell.row == row && shipCell.col == col)
                    {
                        // Обычный корабль
                        kvp.Value.Background = new SolidColorBrush(Color.FromRgb(74, 105, 189));
                        kvp.Value.BorderBrush = new SolidColorBrush(Color.FromRgb(106, 137, 204));
                        kvp.Value.BorderThickness = new Thickness(2);
                        hasShip = true;
                        break;
                    }
                }
                
                if (!hasShip)
                {
                    // Пустая клетка
                    kvp.Value.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                    kvp.Value.BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110));
                    kvp.Value.BorderThickness = new Thickness(1);
                }
            }
        }

        private void UpdateShipsInfo()
        {
            int placed4 = 0, placed3 = 0, placed2 = 0, placed1 = 0;
            int total4 = 1, total3 = 2, total2 = 3, total1 = 4; // Стандартный набор кораблей
            
            foreach (var ship in _gameLogic.PlayerShips)
            {
                switch (ship.Size)
                {
                    case 4: if (ship.IsPlaced) placed4++; break;
                    case 3: if (ship.IsPlaced) placed3++; break;
                    case 2: if (ship.IsPlaced) placed2++; break;
                    case 1: if (ship.IsPlaced) placed1++; break;
                }
            }
            
            ShipsInfo.Text = $"Осталось расставить: {total4-placed4}x4, {total3-placed3}x3, {total2-placed2}x2, {total1-placed1}x1";
            
            // Показываем информацию о текущем корабле
            GameStatus.Text = _gameLogic.GetCurrentShipInfo();
        }

        private void UpdateButtonsState()
        {
            bool canSearch = _gameLogic.AllShipsPlaced;
            
            PlayWithFriendButton.IsEnabled = canSearch;
            RandomOpponentButton.IsEnabled = canSearch;
            
            if (!canSearch)
            {
                PlayWithFriendButton.Opacity = 0.5;
                RandomOpponentButton.Opacity = 0.5;
            }
            else
            {
                PlayWithFriendButton.Opacity = 1;
                RandomOpponentButton.Opacity = 1;
                GameStatus.Text = "Все корабли расставлены! Можете начинать игру.";
            }
        }

        private void RandomPlacementButton_Click(object sender, RoutedEventArgs e)
        {
            // Расставляем корабли случайным образом
            _gameLogic.RandomlyPlaceShips();
            
            // Обновляем отображение
            UpdateYourBoard();
            UpdateShipsInfo();
            UpdateButtonsState();
            
            GameStatus.Text = "Корабли расставлены случайным образом!";
        }

        private void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
            // Очищаем поле
            _gameLogic.ClearBoard();
            
            // Обновляем отображение
            UpdateYourBoard();
            UpdateShipsInfo();
            UpdateButtonsState();
            
            GameStatus.Text = "Поле очищено. Начинайте расстановку заново.";
        }
        

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                var result = MessageBox.Show("Поиск соперника будет прерван. Вы уверены?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            // Возвращаемся к выбору входа
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void PlayWithFriendButton_Click(object sender, RoutedEventArgs e)
        {
            // Заглушка для игры с другом
            MessageBox.Show(
                          "ЭТО СДЕЛАЕТ НИКИТА ",
                "Игра с другом",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Показываем модальное окно с "ссылкой"
            ShowFriendLinkWindow();
        }

        private void ShowFriendLinkWindow()
        {
            Window friendWindow = new Window
            {
                Title = "Пригласить друга",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(30, 60, 114))
            };

            StackPanel stackPanel = new StackPanel
            {
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Заголовок
            TextBlock title = new TextBlock
            {
                Text = "ПРИГЛАСИТЕ ДРУГА",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // "Ссылка"
            Border linkBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(74, 105, 189)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10, 0, 10)
            };

            TextBlock linkText = new TextBlock
            {
                Text = "https://battleship.ru/game/" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            linkBorder.Child = linkText;

            // Кнопки
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            Button copyButton = new Button
            {
                Content = "📋 Копировать ссылку",
                Width = 180,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            copyButton.Click += (s, args) =>
            {
                MessageBox.Show("ЭТО ТОЖЕ СДЕЛАЕТ НИКИТА",
                    "Успешно",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };

            Button closeButton = new Button
            {
                Content = "Закрыть",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            closeButton.Click += (s, args) => friendWindow.Close();

            buttonPanel.Children.Add(copyButton);
            buttonPanel.Children.Add(closeButton);

            stackPanel.Children.Add(title);
            stackPanel.Children.Add(linkBorder);
            stackPanel.Children.Add(buttonPanel);

            friendWindow.Content = stackPanel;
            friendWindow.ShowDialog();
        }

        private void RandomOpponentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                CancelSearch();
                return;
            }

            StartSearch();
        }

        private void StartSearch()
        {
            _isSearching = true;
            
            // Скрываем кнопки
            PlayWithFriendButton.Visibility = Visibility.Collapsed;
            RandomOpponentButton.Visibility = Visibility.Collapsed;
            
            // Показываем индикатор поиска
            SearchIndicator.Visibility = Visibility.Visible;
            
            // Меняем статус
            GameStatus.Text = "🔍 Поиск случайного соперника...";
            ConnectionStatus.Text = "Поиск...";
            
            // Делаем поле противника неактивным
            foreach (var child in OpponentBoardGrid.Children)
            {
                if (child is Border cell)
                {
                    cell.IsEnabled = false;
                    cell.Background = new SolidColorBrush(Color.FromRgb(70, 70, 70));
                }
            }
            
            // Симуляция поиска (5-10 секунд)
            SimulateSearch();
        }

        private void CancelSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CancelSearch();
        }

        private void CancelSearch()
        {
            _isSearching = false;
            
            // Показываем кнопки
            PlayWithFriendButton.Visibility = Visibility.Visible;
            RandomOpponentButton.Visibility = Visibility.Visible;
            
            // Скрываем индикатор поиска
            SearchIndicator.Visibility = Visibility.Collapsed;
            
            // Восстанавливаем статус
            GameStatus.Text = "Подготовка к игре - расставьте корабли на вашем поле";
            ConnectionStatus.Text = "Не подключено";
            
            // Восстанавливаем поле противника
            foreach (var child in OpponentBoardGrid.Children)
            {
                if (child is Border cell)
                {
                    cell.IsEnabled = true;
                    cell.Background = new SolidColorBrush(Color.FromRgb(50, 58, 70));
                }
            }
        }

        private async void SimulateSearch()
        {
            try
            {
                string[] searchingTexts = 
                {
                    "🔍 Поиск соперника...",
                    "🔍 Ищем достойного противника...",
                    "🔍 Сканируем игроков онлайн...",
                    "🔍 Соперник найден! Подключение..."
                };

                for (int i = 0; i < 20; i++) // Максимум 20 секунд поиска
                {
                    if (!_isSearching) break;

                    // Обновляем текст статуса каждые 3 секунды
                    if (i % 3 == 0 && i / 3 < searchingTexts.Length)
                    {
                        SearchStatus.Text = searchingTexts[i / 3];
                    }

                    // Случайная симуляция нахождения соперника (25% шанс после 3 секунд)
                    if (i > 3 && new Random().Next(1, 5) == 1)
                    {
                        // Нашли соперника
                        SearchStatus.Text = "🎮 Соперник найден! Начинаем игру...";
                        await System.Threading.Tasks.Task.Delay(1500);
                        
                        // Заглушка для начала игры
                        GameStatus.Text = "🎮 Игра началась! Ваш ход.";
                        ConnectionStatus.Text = "Подключено";
                        
                        MessageBox.Show("ЭТО СДЕЛАЕТ НИКИТА",
                            "Игра началась",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        
                        CancelSearch(); // Скрываем индикатор поиска
                        return;
                    }

                    await System.Threading.Tasks.Task.Delay(1000);
                }

                if (_isSearching)
                {
                    SearchStatus.Text = "😔 Соперник не найден. Попробуйте позже.";
                    await System.Threading.Tasks.Task.Delay(2000);
                    CancelSearch();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка поиска: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                CancelSearch();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isSearching)
            {
                var result = MessageBox.Show("Поиск соперника будет прерван. Вы уверены, что хотите выйти?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }
    }
}