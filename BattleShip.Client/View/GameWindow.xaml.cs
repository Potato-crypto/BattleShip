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

    // Вспомогательный класс для хранения состояние клетки
    public class CellState
    {
        public Brush BaseColor { get; set; }
        public bool IsHighlighted { get; set; }
        public bool HasShip { get; set; }
        public bool IsHit { get; set; }
        public bool IsMiss { get; set; }
        public bool IsPlacing { get; set; }

        public Brush GetCurrentColor()
        {
            if (IsHighlighted)
            {
                // Для подсвеченной клетки - более светлая версия базового цвета
                if (BaseColor is SolidColorBrush baseBrush)
                {
                    var color = baseBrush.Color;
                    var highlightedColor = Color.FromArgb(
                        255,
                        (byte)Math.Min(color.R + 40, 255),
                        (byte)Math.Min(color.G + 40, 255),
                        (byte)Math.Min(color.B + 40, 255));

                    return new SolidColorBrush(highlightedColor);
                }
            }
            return BaseColor;
        }
    }

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

            // Инициализация сетевого сервиса 
            _networkService = new ServerNetworkManager();
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

        private void ClearOpponentBoard()
        {
            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.ToolTip = null;
            }
        }

        private void EnableOpponentBoard(bool enable)
        {
            foreach (var cell in _opponentCells.Values)
            {
                cell.IsEnabled = enable;
            }
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
                    // ServerNetworkManager передает opponentId, нужно получить имя
                    _opponentName = !string.IsNullOrEmpty(startMessage.OpponentName)
                        ? startMessage.OpponentName
                        : "Соперник"; // или получить имя по ID из сервера

                    GameStatus.Text = $"Игра началась! Противник: {_opponentName}";
                    ConnectionStatus.Text = "В игре";

                    // Добавляем системное сообщение
                    ChatWindowControl.AddSystemMessage($"Игра началась. Ваш соперник: {_opponentName}");

                    // Показываем кнопку чата
                    OpenChatButton.Visibility = Visibility.Visible;

                    // Очищаем поле противника
                    //ClearOpponentBoard();

                    // Очищаем историю выстрелов
                    _playerShots.Clear();
                    _hitsOnOpponent.Clear();

                    // Разблокируем поле противника
                    EnableOpponentBoard(true);
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

                    Console.WriteLine($"=== UI: Обработка выстрела ===");
                    Console.WriteLine($"Клетка: {cellKey}");
                    Console.WriteLine($"Result: {result.Result}");
                    Console.WriteLine($"CellStatus: {result.CellStatus}");
                    Console.WriteLine($"NextTurn: {result.NextTurn}");

                    // Добавляем выстрел в историю
                    _playerShots.Add(cellKey);

                    if (_opponentCells.ContainsKey(cellKey))
                    {
                        var cell = _opponentCells[cellKey];

                        // Обновляем цвет на основе CellStatus с сервера
                        if (result.CellStatus == "Sunk")
                        {
                            cell.Background = Brushes.DarkRed;
                            _hitsOnOpponent.Add(cellKey);
                            ShowSpecialMessage($"Потоплен корабль {result.ShipName}!", 3000);
                        }
                        else if (result.CellStatus == "Hit")
                        {
                            cell.Background = Brushes.Red;
                            _hitsOnOpponent.Add(cellKey);
                            ShowSpecialMessage("Попадание! Стреляйте еще.", 2000);
                        }
                        else if (result.CellStatus == "Miss")
                        {
                            cell.Background = Brushes.LightGray;
                            ShowSpecialMessage("Промах! Ход противника.", 2000);
                        }
                        else
                        {
                            // Fallback на старую логику
                            if (result.Result == "sunk")
                            {
                                cell.Background = Brushes.DarkRed;
                                _hitsOnOpponent.Add(cellKey);
                                ShowSpecialMessage($"Потоплен корабль {result.ShipSize}x!", 3000);
                            }
                            else if (result.Result == "hit")
                            {
                                cell.Background = Brushes.Red;
                                _hitsOnOpponent.Add(cellKey);
                                ShowSpecialMessage("Попадание! Стреляйте еще.", 2000);
                            }
                            else
                            {
                                cell.Background = Brushes.LightGray;
                                ShowSpecialMessage("Промах! Ход противника.", 2000);
                            }
                        }

                        // Обновляем состояние клетки в Tag
                        if (cell.Tag is Tuple<string, CellState> tag)
                        {
                            var (_, cellState) = tag;
                            cellState.IsHit = result.CellStatus == "Hit" || result.CellStatus == "Sunk";
                            cellState.IsMiss = result.CellStatus == "Miss";
                            cellState.BaseColor = cell.Background;
                        }
                    }

                    // Обновляем статус игры
                    if (result.NextTurn == "player")
                    {
                        GameStatus.Text = "Попадание! Ваш ход продолжается.";
                        EnableOpponentBoard(true); // Поле противника активно
                    }
                    else
                    {
                        GameStatus.Text = "Промах! Ход противника...";
                        EnableOpponentBoard(false); // Блокируем поле противника
                    }
                });

            };

            _networkService.OnOpponentShoot += (shoot) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string cellKey = $"{shoot.Row},{shoot.Col}";

                    Console.WriteLine($"=== UI: Выстрел противника в {cellKey} ===");

                    // Просто добавляем выстрел в историю
                    _opponentShots.Add(cellKey);

                    // Проверяем попадание
                    bool isHit = _gameLogic.GetPlayerShipCells()
                        .Any(c => c.row == shoot.Row && c.col == shoot.Col);

                    if (isHit)
                    {
                        _hitsOnPlayer.Add(cellKey);
                        CheckIfShipSunk(shoot.Row, shoot.Col);
                        ShowSpecialMessage("Противник попал в ваш корабль!", 2000);
                    }
                    else
                    {
                        ShowSpecialMessage("Противник промахнулся! Ваш ход.", 2000);
                    }

                    UpdatePlayerBoardVisual();

                    // Обновляем статус
                    GameStatus.Text = "Ваш ход!";
                    EnableOpponentBoard(true);
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

            _networkService.OnOpponentDisconnected += (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Показываем в чате
                    ChatWindowControl.AddSystemMessage($"⚠️ {message}");

                    // Показываем уведомление
                    ShowSpecialMessage(message, 5000);

                    // Блокируем поле противника
                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.IsEnabled = false;
                    }

                    // Обновляем статус
                    GameStatus.Text = "Противник отключился";
                    // Больше ничего - OnGameEnded вызовется из ServerNetworkManager
                });
            };

            // Подписываемся на события чата из сетевого сервиса
            _networkService.OnChatMessage += (chatMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Получаем имя игрока
                    string playerName = Application.Current.Properties.Contains("Username")
                        ? Application.Current.Properties["Username"].ToString()
                        : "Вы";

                    // Если это системное сообщение
                    if (chatMessage.IsSystem)
                    {
                        ChatWindowControl.AddSystemMessage(chatMessage.Message);
                    }
                    else
                    {
                        // Определяем, наше ли это сообщение
                        bool isOwn = !chatMessage.IsFromOpponent;
                        string senderDisplayName = isOwn ? "Вы" : chatMessage.Sender;

                        ChatWindowControl.AddMessage(senderDisplayName, chatMessage.Message, isOwn);
                    }

                    // Если чат закрыт и сообщение не наше - показываем уведомление
                    if (ChatWindowControl.Visibility != Visibility.Visible &&
                        !chatMessage.IsSystem &&
                        chatMessage.IsFromOpponent)
                    {
                        // Можно мигнуть кнопкой чата или показать уведомление
                        ShowSpecialMessage($"Новое сообщение от {chatMessage.Sender}", 2000);
                    }
                });
            };
        }

        private void UpdatePlayerBoardVisual()
        {
            Console.WriteLine("=== Обновление визуализации своего поля ===");

            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
                string cellKey = kvp.Key;

                bool hasShip = _gameLogic.GetPlayerShipCells()
                    .Any(c => c.row == row && c.col == col);
                bool isHit = _hitsOnPlayer.Contains(cellKey);
                bool isMiss = _opponentShots.Contains(cellKey);

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;
                    cellState.HasShip = hasShip;
                    cellState.IsHit = isHit;
                    cellState.IsMiss = isMiss;

                    // Определяем цвет
                    if (isHit)
                    {
                        cellState.BaseColor = Brushes.OrangeRed;
                    }
                    else if (isMiss)
                    {
                        cellState.BaseColor = Brushes.LightBlue;
                    }
                    else if (hasShip)
                    {
                        cellState.BaseColor = new SolidColorBrush(Color.FromRgb(74, 105, 189));
                    }
                    else
                    {
                        cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                    }

                    cell.Background = cellState.GetCurrentColor();
                }
                else
                {
                    // Старая логика
                    if (isHit)
                    {
                        cell.Background = Brushes.OrangeRed;
                    }
                    else if (isMiss)
                    {
                        cell.Background = Brushes.LightBlue;
                    }
                    else if (hasShip)
                    {
                        cell.Background = new SolidColorBrush(Color.FromRgb(74, 105, 189));
                    }
                    else
                    {
                        cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                    }
                }
            }

            Console.WriteLine($"Попаданий по вам: {_hitsOnPlayer.Count}, Промахов противника: {_opponentShots.Count}");
        }

        private void UpdateShipsRemaining(int remainingShips)
        {
            Dispatcher.Invoke(() =>
            {
                // Обновляем GameStatus 
                if (remainingShips == 0)
                {
                    GameStatus.Text = "🎯 Все корабли потоплены!";
                    GameStatus.Foreground = Brushes.Red;
                }
                else
                {
                    GameStatus.Text = $"Кораблей осталось: {remainingShips}";
                    GameStatus.Foreground = remainingShips <= 3 ? Brushes.Orange : Brushes.Green;
                }
            });
        }

        private void CheckIfShipSunk(int hitRow, int hitCol)
        {
            // Проверяем потоплен ли корабль
            var shipCells = _gameLogic.GetShipCells(hitRow, hitCol);

            if (shipCells.Count > 0)
            {
                // Проверяем все ли клетки корабля подбиты
                bool allCellsHit = shipCells.All(cell =>
                    _hitsOnPlayer.Contains($"{cell.row},{cell.col}"));

                if (allCellsHit)
                {
                    ShowSpecialMessage($"Противник потопил ваш корабль ({shipCells.Count} клеток)!", 3000);

                    // Помечаем клетки вокруг потопленного корабля
                    MarkCellsAroundSunkShip(shipCells, isPlayerBoard: true);

                    // Обновляем отображение
                    UpdateYourBoard();
                }
                else
                {
                    ShowSpecialMessage("Противник попал в ваш корабль!", 2000);
                }
            }
        }

        private void DebugCellState(string cellKey, string action)
        {
            Console.WriteLine($"=== DEBUG UI: {action} ===");
            Console.WriteLine($"Клетка: {cellKey}");

            if (_playerCells.ContainsKey(cellKey))
            {
                var cell = _playerCells[cellKey];
                Console.WriteLine($"  Тип: Своя клетка");
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;
                    Console.WriteLine($"  Состояние: HasShip={cellState.HasShip}, IsHit={cellState.IsHit}, IsMiss={cellState.IsMiss}, IsSunk={cellState.IsPlacing}");
                    Console.WriteLine($"  Цвет: {cell.Background}");
                }
            }
            else if (_opponentCells.ContainsKey(cellKey))
            {
                var cell = _opponentCells[cellKey];
                Console.WriteLine($"  Тип: Клетка противника");
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;
                    Console.WriteLine($"  Состояние: IsHit={cellState.IsHit}, IsMiss={cellState.IsMiss}");
                    Console.WriteLine($"  Цвет: {cell.Background}");
                }
            }
        }

        private void MarkCellsAroundSunkShip(List<(int row, int col)> shipCells, bool isPlayerBoard)
        {
            foreach (var (row, col) in shipCells)
            {
                // Помечаем все клетки вокруг каждой клетки корабля
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int checkRow = row + dr;
                        int checkCol = col + dc;

                        if (checkRow >= 0 && checkRow < GridSize &&
                            checkCol >= 0 && checkCol < GridSize)
                        {
                            string cellKey = $"{checkRow},{checkCol}";

                            // Пропускаем сами клетки корабля
                            if (shipCells.Contains((checkRow, checkCol)))
                                continue;

                            if (isPlayerBoard)
                            {
                                // Для своего поля - помечаем как промах противника
                                if (!_opponentShots.Contains(cellKey))
                                {
                                    _opponentShots.Add(cellKey);

                                    // Обновляем состояние клетки
                                    if (_playerCells.ContainsKey(cellKey) &&
                                        _playerCells[cellKey].Tag is Tuple<string, CellState> tag)
                                    {
                                        var (_, cellState) = tag;
                                        cellState.IsMiss = true;
                                        _playerCells[cellKey].Background = cellState.GetCurrentColor();
                                    }
                                }
                            }
                            else
                            {
                                // Для поля противника - помечаем как наш промах
                                if (!_playerShots.Contains(cellKey))
                                {
                                    _playerShots.Add(cellKey);

                                    if (_opponentCells.ContainsKey(cellKey) &&
                                        _opponentCells[cellKey].Tag is Tuple<string, CellState> tag)
                                    {
                                        var (_, cellState) = tag;
                                        cellState.IsMiss = true;
                                        _opponentCells[cellKey].Background = cellState.GetCurrentColor();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private async void ChatWindowControl_MessageSent(object sender, string message)
        {
            try
            {
                // Получаем имя игрока
                string playerName = Application.Current.Properties.Contains("Username")
                    ? Application.Current.Properties["Username"].ToString()
                    : "Вы";

                // Сразу показываем свое сообщение в чате
                ChatWindowControl.AddMessage(playerName, message, isOwn: true);

                // Отправляем через сеть
                if (_networkService.IsInGame && _networkService.IsConnected)
                {
                    await _networkService.SendChatMessageAsync(message);
                }
                else
                {
                    // Если нет подключения, показываем ошибку
                    ChatWindowControl.AddSystemMessage("Нет подключения к серверу. Сообщение не отправлено.");
                }
            }
            catch (Exception ex)
            {
                ChatWindowControl.AddSystemMessage($"Ошибка отправки: {ex.Message}");
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
            var cellState = new CellState
            {
                HasShip = false,
                IsHit = false,
                IsMiss = false,
                IsPlacing = false,
                BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60))
            };

            Border cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110)),
                BorderThickness = new Thickness(1),
                Background = cellState.BaseColor, // Используем из состояния
                Tag = new Tuple<string, CellState>($"{row},{col}", cellState), // Храним координаты И состояние
                Cursor = isYourBoard ? Cursors.Hand : Cursors.Arrow
            };

            if (isYourBoard)
            {
                cell.MouseLeftButtonDown += YourCell_MouseLeftButtonDown;
                cell.MouseRightButtonDown += YourCell_MouseRightButtonDown;
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }
            else
            {
                cell.MouseLeftButtonDown += OpponentCell_MouseLeftButtonDown;
                cell.MouseLeave += Cell_MouseLeave; // Добавляем для поля противника тоже
            }

            return cell;
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, cellState) = tag;
                var coords = coordsStr.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                //  Сначала обновляем состояние, потом подсвечиваем!
                if (_playerCells.ContainsKey(coordsStr))
                {
                    UpdateCellState(cellState, row, col, coordsStr, isPlayerCell: true);
                }
                else
                {
                    UpdateCellState(cellState, row, col, coordsStr, isPlayerCell: false);
                }

                // Теперь подсвечиваем
                cellState.IsHighlighted = true;
                cell.Background = cellState.GetCurrentColor();
            }
        }

        private void Cell_MouseLeave(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, cellState) = tag;

                // Убираем подсветку
                cellState.IsHighlighted = false;
                cell.Background = cellState.GetCurrentColor(); // Используем текущий цвет состояния
            }
        }

        private void UpdateCellState(CellState cellState, int row, int col, string cellKey, bool isPlayerCell)
        {
            if (isPlayerCell)
            {
                // Для своего поля
                bool isCurrentShipCell = _gameLogic.GetCurrentShipBeingPlacedCells()
                    .Any(c => c.row == row && c.col == col);
                bool hasShip = _gameLogic.GetPlayerShipCells()
                    .Any(c => c.row == row && c.col == col);

                cellState.IsPlacing = isCurrentShipCell;
                cellState.HasShip = hasShip;
                cellState.IsHit = _hitsOnPlayer.Contains(cellKey);
                cellState.IsMiss = _opponentShots.Contains(cellKey);

                // Определяем базовый цвет
                if (isCurrentShipCell)
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(106, 137, 204));
                }
                else if (hasShip)
                {
                    cellState.BaseColor = Brushes.DarkGray; // Или ваш цвет корабля
                }
                else if (cellState.IsHit)
                {
                    cellState.BaseColor = Brushes.OrangeRed;
                }
                else if (cellState.IsMiss)
                {
                    cellState.BaseColor = Brushes.LightBlue;
                }
                else
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }
            else
            {
                // Для поля противника
                cellState.IsHit = _hitsOnOpponent.Contains(cellKey);
                cellState.IsMiss = _playerShots.Contains(cellKey);

                if (cellState.IsHit)
                {
                    cellState.BaseColor = Brushes.Red;
                }
                else if (cellState.IsMiss)
                {
                    cellState.BaseColor = Brushes.LightGray;
                }
                else
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }
        }

        private void UpdateOpponentCellColor(Border cell, int row, int col, string cellKey)
        {
            // Для поля противника показываем только результаты выстрелов
            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;

                if (_hitsOnOpponent.Contains(cellKey))
                {
                    // Наше попадание
                    cellState.BaseColor = Brushes.Red;
                    cellState.IsHit = true;
                }
                else if (_playerShots.Contains(cellKey))
                {
                    // Наш промах
                    cellState.BaseColor = Brushes.LightGray;
                    cellState.IsMiss = true;
                }
                else
                {
                    // Неизвестная клетка - всегда темная
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                    cellState.IsHit = false;
                    cellState.IsMiss = false;
                }

                // Обновляем цвет клетки
                cell.Background = cellState.GetCurrentColor();
            }
            else
            {
                // Старый формат
                if (_hitsOnOpponent.Contains(cellKey))
                {
                    cell.Background = Brushes.Red;
                }
                else if (_playerShots.Contains(cellKey))
                {
                    cell.Background = Brushes.LightGray;
                }
                else
                {
                    cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }

            // Убедимся, что граница тоже темная
            cell.BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110));
        }

        private void YourCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_networkService.IsInGame || _gameLogic.AllShipsPlaced) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, _) = tag; // Берем только координаты
                var coords = coordsStr.Split(',');
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
            else
            {
                // Запасной вариант на случай старого формата Tag
                var coordsStr = cell.Tag.ToString();
                var coords = coordsStr.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                if (_gameLogic.TryPlaceShipCell(row, col))
                {
                    UpdateYourBoard();
                    UpdateShipsInfo();
                    UpdateButtonsState();

                    if (!_showingSpecialMessage)
                    {
                        GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                    }
                }
            }
        }

        private void YourCell_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_networkService.IsInGame || _gameLogic.AllShipsPlaced) return;

            var cell = (Border)sender;

            // Получаем координаты из Tag
            if (cell.Tag is Tuple<string, CellState> tag)
            {
                // Просто проверяем логику, координаты не нужны для отмены
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
        }

        private async void OpponentCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_networkService.IsInGame || !_gameLogic.AllShipsPlaced) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, _) = tag;
                var coords = coordsStr.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                // Отправляем выстрел через сетевой сервис
                await _networkService.ShootAsync(row, col);
            }
        }

        private void UpdateYourBoard()
        {
            // Обновляем все клетки своего поля через новый метод
            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                // Получаем состояние клетки из Tag
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (coordsStr, cellState) = tag;

                    // Обновляем состояние клетки
                    UpdateCellState(cellState, row, col, coordsStr, isPlayerCell: true);

                    // Обновляем цвет (учитывая подсветку если есть)
                    cell.Background = cellState.GetCurrentColor();
                }
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
