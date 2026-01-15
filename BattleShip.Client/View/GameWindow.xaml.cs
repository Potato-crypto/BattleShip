using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;

namespace BattleShip.Client
{
    public partial class GameWindow : Window
    {
        private const int GridSize = 10;
        private const int CellSize = 35;
        private int _unreadMessages = 0;
        private bool _isSearching = false;
        private GameLogic _gameLogic;
        private INetworkService _networkService;
        private Dictionary<string, Border> _playerCells = new Dictionary<string, Border>();
        private Dictionary<string, Border> _opponentCells = new Dictionary<string, Border>();
    
        // Для отображения выстрелов
        private HashSet<string> _playerShots = new HashSet<string>();
        private HashSet<string> _opponentShots = new HashSet<string>();
        private HashSet<string> _hitsOnPlayer = new HashSet<string>();
        private HashSet<string> _hitsOnOpponent = new HashSet<string>();
    
        // Для задержки особых сообщений
        private bool _showingSpecialMessage = false;
        private System.Windows.Threading.DispatcherTimer _messageTimer;
        private string _opponentName = "Компьютер";
        private bool _isExitingFromGameOver = false;

        public GameWindow()
        {
            InitializeComponent();
    
            // Инициализация сетевого сервиса с имитацией
            _networkService = new LocalNetworkManager();
            SetupNetworkEvents();
    
            _gameLogic = new GameLogic();
            InitializeGameBoards();
    
            // Блокируем кнопки поиска пока корабли не расставлены
            UpdateButtonsState();
    
            // Подключаемся к "серверу" с именем игрока
            ConnectToServer();
    
            // Инициализируем таймер сообщений
            _messageTimer = new System.Windows.Threading.DispatcherTimer();
            _messageTimer.IsEnabled = false;
            
            // Настраиваем обработчики событий чата
            ChatWindowControl.MessageSent += ChatWindowControl_MessageSent;
            ChatWindowControl.Closed += ChatWindowControl_Closed;
            ChatWindowControl.UnreadCountChanged += ChatWindowControl_UnreadCountChanged;
        }
        private void ChatWindowControl_UnreadCountChanged(object sender, int count)
        {
            Dispatcher.Invoke(() =>
            {
                _unreadMessages = count;
                UpdateUnreadBadge();
            });
        }

// Метод для обновления отображения счетчика:
        private void UpdateUnreadBadge()
        {
            if (_unreadMessages > 0)
            {
                UnreadBadge.Visibility = Visibility.Visible;
                UnreadCountText.Text = _unreadMessages > 9 ? "9+" : _unreadMessages.ToString();
            }
            else
            {
                UnreadBadge.Visibility = Visibility.Collapsed;
            }
        }
        
        private async void ConnectToServer()
        {
            string playerName = Application.Current.Properties.Contains("Username") 
                ? Application.Current.Properties["Username"].ToString() 
                : "Игрок";
            
            await _networkService.ConnectAsync(playerName);
        }
        
        private void SetupNetworkEvents()
        {
            _networkService.OnConnectionChanged += (isConnected) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ConnectionStatus.Text = isConnected ? "Подключено" : "Не подключено";
                });
            };
            
            _networkService.OnGameStarted += (startMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _opponentName = startMessage.OpponentName; // Сохраняем имя противника
                    
                    GameStatus.Text = $"Игра началась! Противник: {_opponentName}";
                    ConnectionStatus.Text = "В игре";
                    
                    // Обновляем заголовок чата с именем противника
                    ChatWindowControl.SetOpponentName(_opponentName);
                    
                    // Добавляем системное сообщение в чат
                    ChatWindowControl.AddSystemMessage($"Игра началась. Ваш соперник: {_opponentName}");
                    
                    // Показываем кнопку открытия чата
                    OpenChatButton.Visibility = Visibility.Visible;
                    
                    // Сбрасываем поле противника (все клетки скрыты)
                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                    }
                    
                    // Очищаем историю выстрелов
                    _playerShots.Clear();
                    _hitsOnOpponent.Clear();
                    
                    // Разблокируем поле противника
                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.IsEnabled = true;
                    }
                });
            };
            
            _networkService.OnGameEnded += (endMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Скрываем кнопку чата
                    OpenChatButton.Visibility = Visibility.Collapsed;
                    
                    // Закрываем окно чата, если оно открыто
                    ChatWindowControl.Visibility = Visibility.Collapsed;
                    
                    // Добавляем сообщение в чат о завершении игры
                    string resultMessage = endMessage.Winner == "player" 
                        ? "Вы победили! Поздравляем!" 
                        : "Вы проиграли. Попробуйте еще раз!";
                    ChatWindowControl.AddSystemMessage(resultMessage);
                    
                    // Показываем модальное окно с результатами
                    GameOverWindow gameOverWindow = new GameOverWindow(
                        endMessage.Winner,
                        _opponentName,
                        endMessage.Stats);
                
                    gameOverWindow.Owner = this;
                    bool? dialogResult = gameOverWindow.ShowDialog();
                
                    // Обрабатываем выбор пользователя
                    if (dialogResult == true)
                    {
                        if (gameOverWindow.PlayAgain)
                        {
                            // Играть еще раз - сбрасываем игру
                            ResetGameForNewRound();
                        }
                        else
                        {
                            // Выйти в меню - закрываем текущее окно и открываем главное меню
                            _isExitingFromGameOver = true; // Устанавливаем флаг
                            ReturnToMainMenu();
                        }
                    }
                });
            };
            
            _networkService.OnShootResult += (result) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string cellKey = $"{result.Row},{result.Col}";
                    
                    // Добавляем выстрел в историю
                    _playerShots.Add(cellKey);
                    
                    // Обновляем поле противника
                    if (_opponentCells.ContainsKey(cellKey))
                    {
                        var cell = _opponentCells[cellKey];
                        
                        switch (result.Result)
                        {
                            case "hit":
                                cell.Background = Brushes.Red;
                                _hitsOnOpponent.Add(cellKey);
                                ShowSpecialMessage("Попадание! Стреляйте еще.", 2000); // 2 секунды
                                break;
                            case "sunk":
                                cell.Background = Brushes.DarkRed;
                                _hitsOnOpponent.Add(cellKey);
                                ShowSpecialMessage($"Потоплен корабль {result.ShipSize}x!", 3000); // 3 секунды
                                
                                // Помечаем клетки вокруг потопленного корабля
                                MarkCellsAroundSunkShip(result.Row, result.Col, result.ShipSize, false);
                                break;
                            case "miss":
                                cell.Background = Brushes.LightGray;
                                ShowSpecialMessage("Промах! Ход противника.", 2000);
                                break;
                            case "already_shot":
                                ShowSpecialMessage("Вы уже стреляли сюда!", 1000);
                                break;
                        }
                        
                        // Обновляем информацию о кораблях
                        if (result.RemainingShips == 0)
                        {
                            ShowSpecialMessage("Вы уничтожили все корабли противника!", 5000);
                        }
                    }
                });
            };
            
            _networkService.OnOpponentShoot += (shoot) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string cellKey = $"{shoot.Row},{shoot.Col}";
                    
                    // Добавляем выстрел противника в историю
                    _opponentShots.Add(cellKey);
                    
                    // Обновляем свое поле
                    if (_playerCells.ContainsKey(cellKey))
                    {
                        var cell = _playerCells[cellKey];
                        
                        // Проверяем, попал ли противник в наш корабль
                        bool isHit = _gameLogic.GetPlayerShipCells()
                            .Any(c => c.row == shoot.Row && c.col == shoot.Col);
                        
                        if (isHit)
                        {
                            cell.Background = Brushes.OrangeRed;
                            _hitsOnPlayer.Add(cellKey);
                            ShowSpecialMessage("Противник попал в ваш корабль!", 2000);
                        }
                        else
                        {
                            cell.Background = Brushes.LightBlue;
                            ShowSpecialMessage("Противник промахнулся! Ваш ход.", 2000);
                        }
                    }
                });
            };
            
            _networkService.OnGameStateUpdated += (state) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Не обновляем статус, если показываем специальное сообщение
                    if (_showingSpecialMessage) return;
                    
                    // Обновляем UI в соответствии с состоянием игры
                    UpdateUIForGameState(state);
                });
            };
                
            _networkService.OnError += (error) =>
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(error.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    ChatWindowControl.AddSystemMessage($"Ошибка: {error.Message}");
                });
            };
            
            // Подписываемся на события чата из сетевого сервиса
            _networkService.OnChatMessage += (chatMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ChatWindowControl.AddMessage(chatMessage.Sender, chatMessage.Message);
                });
            };
        }
        
        private void ChatWindowControl_MessageSent(object sender, string message)
        {
            // Получаем имя игрока
            string playerName = Application.Current.Properties.Contains("Username") 
                ? Application.Current.Properties["Username"].ToString() 
                : "Вы";
            
            // Добавляем сообщение в локальный чат
            ChatWindowControl.AddMessage(playerName, message, isOwn: true);
            
            // Отправляем сообщение через сетевой сервис
            if (_networkService.IsInGame)
            {
                _ = _networkService.SendChatMessageAsync(message);
            }
        }
        
        private void ChatWindowControl_Closed(object sender, EventArgs e)
        {
            // Скрываем окно чата
            ChatWindowControl.Visibility = Visibility.Collapsed;
        }
        
        private void OpenChatButton_Click(object sender, RoutedEventArgs e)
        {
            // Показываем окно чата
            ChatWindowControl.Visibility = Visibility.Visible;
            // Сбрасываем счетчик непрочитанных
            ChatWindowControl.MarkAsRead();
            UpdateUnreadBadge();
        }
        
        private void ResetGameForNewRound()
        {
            // Очищаем поле
            _gameLogic.ClearBoard();
            
            // Очищаем историю выстрелов
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();
            
            // Обновляем отображение своего поля
            UpdateYourBoard();
            
            // Сбрасываем поле противника (все клетки скрыты)
            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
            }
            
            // Разблокируем свое поле для расстановки
            foreach (var cell in _playerCells.Values)
            {
                cell.IsEnabled = true;
            }
            
            // Скрываем кнопку чата
            OpenChatButton.Visibility = Visibility.Collapsed;
            
            // Закрываем окно чата
            ChatWindowControl.Visibility = Visibility.Collapsed;
            
            // Очищаем чат
            ChatWindowControl.ClearChat();
            
            // Обновляем информацию о кораблях
            UpdateShipsInfo();
            UpdateButtonsState();
            
            // Выходим из текущей игры
            _networkService.LeaveGameAsync();
            
            GameStatus.Text = "Новая игра! Расставьте корабли.";
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }
        
        private void ReturnToMainMenu()
        {
            // Выходим из текущей игры
            _networkService.LeaveGameAsync();
            
            // Сбрасываем состояние
            _gameLogic.ClearBoard();
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();
            
            // Закрываем текущее окно и открываем главное меню
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }
        
        private void ShowSpecialMessage(string message, int durationMilliseconds)
        {
            // Отменяем предыдущий таймер, если есть
            if (_messageTimer != null)
            {
                _messageTimer.Stop();
                _messageTimer = null;
            }
            
            // Показываем сообщение
            _showingSpecialMessage = true;
            GameStatus.Text = message;
            
            // Создаем таймер для возврата к нормальному статусу
            _messageTimer = new System.Windows.Threading.DispatcherTimer();
            _messageTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
            _messageTimer.Tick += (s, e) =>
            {
                _messageTimer.Stop();
                _showingSpecialMessage = false;
                
                // Восстанавливаем нормальный статус
                if (_networkService.IsInGame)
                {
                    // Проверяем текущее состояние игры
                    if (_gameLogic.AllShipsPlaced)
                    {
                        // Определяем, чей сейчас ход
                        // Здесь нужно получить актуальное состояние из сервиса
                        // Для простоты покажем общее сообщение
                        GameStatus.Text = "Ваш ход!";
                    }
                }
                else
                {
                    GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                }
            };
            
            _messageTimer.Start();
        }
        
        private string GetCellCoordinate(int row, int col)
        {
            return $"{(char)('А' + col)}{row + 1}";
        }
                
        private void UpdateUIForGameState(GameStateMessage state)
        {
            // Не обновляем, если показываем специальное сообщение
            if (_showingSpecialMessage) return;
            
            switch (state.Status)
            {
                case "placing":
                    GameStatus.Text = "Расставьте свои корабли";
                    // Скрываем кнопку чата
                    OpenChatButton.Visibility = Visibility.Collapsed;
                    // Блокируем поле противника
                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.IsEnabled = false;
                    }
                    break;
                case "playing":
                    if (state.CurrentTurn == "player")
                    {
                        GameStatus.Text = "Ваш ход! Выберите клетку на поле противника";
                        // Показываем кнопку чата
                        OpenChatButton.Visibility = Visibility.Visible;
                        // Активируем поле противника
                        foreach (var cell in _opponentCells.Values)
                        {
                            cell.IsEnabled = true;
                            cell.Cursor = Cursors.Hand;
                        }
                    }
                    else
                    {
                        GameStatus.Text = "Ход противника...";
                        // Показываем кнопку чата
                        OpenChatButton.Visibility = Visibility.Visible;
                        // Блокируем поле противника
                        foreach (var cell in _opponentCells.Values)
                        {
                            cell.IsEnabled = false;
                            cell.Cursor = Cursors.Arrow;
                        }
                    }
                    break;
                case "finished":
                    // Блокируем оба поля
                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.IsEnabled = false;
                    }
                    foreach (var cell in _playerCells.Values)
                    {
                        cell.IsEnabled = false;
                    }
                    break;
            }
        }
        
        private void MarkCellsAroundSunkShip(int row, int col, int shipSize, bool isPlayerBoard)
        {
            // Простой алгоритм для пометки клеток вокруг потопленного корабля
            // В реальной игре нужно знать все клетки корабля, но для простоты пометим вокруг точки попадания
    
            var cellsToMark = new List<(int row, int col)>();
    
            // Создаем квадрат 3x3 вокруг точки попадания
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int checkRow = row + dr;
                    int checkCol = col + dc;
            
                    if (checkRow >= 0 && checkRow < GridSize && checkCol >= 0 && checkCol < GridSize)
                    {
                        cellsToMark.Add((checkRow, checkCol));
                    }
                }
            }
    
            // Помечаем клетки
            foreach (var (checkRow, checkCol) in cellsToMark)
            {
                string cellKey = $"{checkRow},{checkCol}";
        
                if (isPlayerBoard)
                {
                    if (_playerCells.ContainsKey(cellKey) && !_hitsOnPlayer.Contains(cellKey))
                    {
                        _playerCells[cellKey].Background = Brushes.LightGray;
                    }
                }
                else
                {
                    if (_opponentCells.ContainsKey(cellKey) && !_hitsOnOpponent.Contains(cellKey))
                    {
                        _opponentCells[cellKey].Background = Brushes.LightGray;
                        _playerShots.Add(cellKey); // Добавляем как выстрел (промах)
                    }
                }
            }
        }

        private void InitializeGameBoards()
        {
            InitializeBoard(YourBoardGrid, true);
            InitializeBoard(OpponentBoardGrid, false);
            UpdateShipsInfo();
    
            // Изначально поле противника должно быть полностью скрыто
            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
                cell.Cursor = Cursors.Arrow; // Курсор "стрелка", а не "рука"
            }
    
            // Свое поле тоже инициализируем
            UpdateYourBoard();
        }

        private void InitializeBoard(Grid boardGrid, bool isYourBoard)
        {
            // Очищаем поле
            boardGrid.Children.Clear();
            boardGrid.RowDefinitions.Clear();
            boardGrid.ColumnDefinitions.Clear();
            
            if (isYourBoard) 
                _playerCells.Clear();
            else 
                _opponentCells.Clear();

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
                    else
                    {
                        _opponentCells[$"{row},{col}"] = cell;
                        cell.IsEnabled = false; // Блокируем до начала игры
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
                Cursor = isYourBoard ? Cursors.Hand : Cursors.Arrow // Разный курсор для разных полей
            };

            if (isYourBoard)
            {
                // Для своего поля
                cell.MouseLeftButtonDown += YourCell_MouseLeftButtonDown;
                cell.MouseRightButtonDown += YourCell_MouseRightButtonDown;
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }
            else
            {
                // Для поля противника
                cell.MouseLeftButtonDown += OpponentCell_MouseLeftButtonDown;
                // Добавляем MouseLeave, чтобы сбрасывать цвет при уходе мыши
                // (если вдруг будет какая-то подсветка)
                cell.MouseLeave += (s, ev) => UpdateOpponentCellColor(cell, row, col, $"{row},{col}");
            }

            return cell;
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;
            var coords = cell.Tag.ToString().Split(',');
            int row = int.Parse(coords[0]);
            int col = int.Parse(coords[1]);
            string cellKey = $"{row},{col}";

            // Определяем, это поле игрока или противника
            bool isPlayerCell = _playerCells.ContainsKey(cellKey);

            if (isPlayerCell)
            {
                // Для своего поля - подсветка ТОЛЬКО при расстановке кораблей и если клетка пустая
                if (!_networkService.IsInGame && !_gameLogic.AllShipsPlaced)
                {
                    // Проверяем, пустая ли клетка (нет корабля и не в процессе расстановки)
                    bool isCurrentShipCell = _gameLogic.GetCurrentShipBeingPlacedCells()
                        .Any(c => c.row == row && c.col == col);
                    bool hasShip = _gameLogic.GetPlayerShipCells()
                        .Any(c => c.row == row && c.col == col);
            
                    if (!isCurrentShipCell && !hasShip && !_hitsOnPlayer.Contains(cellKey) && !_opponentShots.Contains(cellKey))
                    {
                        cell.Background = new SolidColorBrush(Color.FromRgb(60, 70, 80));
                    }
                }
            }
            // Для поля противника - НИКОГДА не подсвечиваем!
        }


        private void Cell_MouseLeave(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;
            var coords = cell.Tag.ToString().Split(',');
            int row = int.Parse(coords[0]);
            int col = int.Parse(coords[1]);
            string cellKey = $"{row},{col}";
    
            // Определяем, это поле игрока или противника
            bool isPlayerCell = _playerCells.ContainsKey(cellKey);
    
            if (isPlayerCell)
            {
                // Свое поле
                UpdatePlayerCellColor(cell, row, col, cellKey);
            }
            else
            {
                // Поле противника - ВСЕГДА восстанавливаем цвет на основе выстрелов
                // (это важно, чтобы сбросить любую подсветка)
                UpdateOpponentCellColor(cell, row, col, cellKey);
            }
        }
        
        private void UpdatePlayerCellColor(Border cell, int row, int col, string cellKey)
        {
            // Проверяем, является ли это клеткой текущего расставляемого корабля
            bool isCurrentShipCell = _gameLogic.GetCurrentShipBeingPlacedCells()
                .Any(c => c.row == row && c.col == col);
    
            if (isCurrentShipCell)
            {
                // Клетка текущего корабля (более светлый цвет)
                cell.Background = new SolidColorBrush(Color.FromRgb(106, 137, 204));
            }
            else
            {
                // Проверяем, есть ли здесь корабль
                bool hasShip = _gameLogic.GetPlayerShipCells()
                    .Any(c => c.row == row && c.col == col);
        
                if (hasShip)
                {
                    // Обычный корабль
                    cell.Background = new SolidColorBrush(Color.FromRgb(74, 105, 189));
                }
                else if (_hitsOnPlayer.Contains(cellKey))
                {
                    // Попадание противника
                    cell.Background = Brushes.OrangeRed;
                }
                else if (_opponentShots.Contains(cellKey))
                {
                    // Промах противника
                    cell.Background = Brushes.LightBlue;
                }
                else
                {
                    // Пустая клетка
                    cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }
        }
        
        private void UpdateOpponentCellColor(Border cell, int row, int col, string cellKey)
        {
            // Для поля противника показываем ТОЛЬКО результаты выстрелов
            if (_hitsOnOpponent.Contains(cellKey))
            {
                // Наше попадание
                cell.Background = Brushes.Red;
            }
            else if (_playerShots.Contains(cellKey))
            {
                // Наш промах
                cell.Background = Brushes.LightGray;
            }
            else
            {
                // Неизвестная клетка - всегда темная
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
            }
    
            // Убедимся, что граница тоже темная
            cell.BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110));
        }
        
        private void YourCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_networkService.IsInGame || _gameLogic.AllShipsPlaced) return;
    
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
        
                // Обновляем статус, если не показываем специальное сообщение
                if (!_showingSpecialMessage)
                {
                    GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                }
            }
            else
            {
                // Не удалось поставить клетку
                ShowSpecialMessage("Нельзя поставить корабль здесь!", 2000);
            }
        }

        private void YourCell_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_networkService.IsInGame || _gameLogic.AllShipsPlaced) return;
            
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

        private async void OpponentCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_networkService.IsInGame || !_gameLogic.AllShipsPlaced) return;

            var cell = (Border)sender;
            var coords = cell.Tag.ToString().Split(',');
            int row = int.Parse(coords[0]);
            int col = int.Parse(coords[1]);

            // Отправляем выстрел через сетевой сервис
            await _networkService.ShootAsync(row, col);
        }

        private void UpdateYourBoard()
        {
            // Обновляем цвета всех клеток своего поля
            foreach (var kvp in _playerCells)
            {
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
        
                UpdatePlayerCellColor(kvp.Value, row, col, kvp.Key);
            }
        }
        
        private void UpdateOpponentBoard()
        {
            // Обновляем цвета всех клеток поля противника
            foreach (var kvp in _opponentCells)
            {
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
        
                UpdateOpponentCellColor(kvp.Value, row, col, kvp.Key);
            }
        }

        private void UpdateShipsInfo()
        {
            int placed4 = 0, placed3 = 0, placed2 = 0, placed1 = 0;
            int total4 = 1, total3 = 2, total2 = 3, total1 = 4;
            
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
            
            ShowSpecialMessage("Корабли расставлены случайным образом!", 2000);
        }

        private async void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
            // Очищаем поле
            _gameLogic.ClearBoard();

            // Очищаем историю выстрелов
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();

            // Обновляем отображение своего поля
            UpdateYourBoard();

            // Сбрасываем поле противника (все клетки скрыты)
            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false; // Блокируем до начала игры
            }

            UpdateShipsInfo();
            UpdateButtonsState();

            ShowSpecialMessage("Поле очищено. Начинайте расстановку заново.", 3000);
        }
                
        private async void StartGameAgainstComputer()
        {
            if (!_gameLogic.AllShipsPlaced)
            {
                MessageBox.Show("Сначала расставьте все корабли!", "Внимание", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
    
            // Очищаем поле противника
            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = true; // Разблокируем для стрельбы
            }
    
            // Очищаем историю выстрелов
            _playerShots.Clear();
            _hitsOnOpponent.Clear();
    
            // Создаем игру против компьютера
            string gameId = await _networkService.CreateGameAsync("computer");
    
            if (!string.IsNullOrEmpty(gameId))
            {
                // Отправляем расстановку кораблей
                var shipsData = ConvertShipsToNetworkFormat();
                await _networkService.SendShipsPlacementAsync(shipsData);
        
                // Блокируем свое поле от изменений
                foreach (var cell in _playerCells.Values)
                {
                    cell.IsEnabled = false;
                }
            }
        }
        
        private List<ShipData> ConvertShipsToNetworkFormat()
        {
            var shipsData = new List<ShipData>();
            
            foreach (var ship in _gameLogic.PlayerShips)
            {
                if (ship.IsPlaced)
                {
                    var shipData = new ShipData
                    {
                        Size = ship.Size,
                        IsHorizontal = ship.IsHorizontal,
                        Cells = new List<CellData>()
                    };
                    
                    foreach (var cell in ship.Cells)
                    {
                        shipData.Cells.Add(new CellData { Row = cell.row, Col = cell.col });
                    }
                    
                    shipsData.Add(shipData);
                }
            }
            
            return shipsData;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_networkService.IsInGame)
            {
                var result = MessageBox.Show("Вы в игре. Выйти из игры и вернуться в меню?", 
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
                
                _networkService.LeaveGameAsync();
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
                "Режим игры с другом будет реализован позже",
                "Игра с другом",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void RandomOpponentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                CancelSearch();
                return;
            }
            
            if (!_gameLogic.AllShipsPlaced)
            {
                MessageBox.Show("Сначала расставьте все корабли!", "Внимание", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Начинаем игру против компьютера
            StartSearch();
            await Task.Delay(1500); // Имитация поиска
            StartGameAgainstComputer();
            CancelSearch();
        }

        private void StartSearch()
        {
            _isSearching = true;
            
            // Скрываем кнопки
            PlayWithFriendButton.Visibility = Visibility.Collapsed;
            RandomOpponentButton.Visibility = Visibility.Collapsed;
            OpenChatButton.Visibility = Visibility.Collapsed;
            
            // Показываем индикатор поиска
            SearchIndicator.Visibility = Visibility.Visible;
            
            // Меняем статус
            GameStatus.Text = "🔍 Поиск случайного соперника...";
            ConnectionStatus.Text = "Поиск...";
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
            OpenChatButton.Visibility = Visibility.Collapsed;
            
            // Скрываем индикатор поиска
            SearchIndicator.Visibility = Visibility.Collapsed;
            
            // Восстанавливаем статус
            GameStatus.Text = "Подготовка к игре - расставьте корабли на вашем поле";
            ConnectionStatus.Text = _networkService.IsConnected ? "Подключено" : "Не подключено";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Если выходим через окно завершения игры, не спрашиваем подтверждение
            if (_isExitingFromGameOver)
            {
                base.OnClosing(e);
                return;
            }
    
            if (_networkService.IsInGame)
            {
                var result = MessageBox.Show("Вы в игре. Выйти из игры?", 
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
