using BattleShip.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
        public bool IsSunk { get; set; }
        public bool IsPlacing { get; set; }
        public bool IsAroundSunk { get; set; }

        public Brush GetCurrentColor()
        {
            if (IsAroundSunk) return Brushes.LightGray; // Клетки вокруг потопленного корабля

            if (IsSunk) return Brushes.DarkRed; // Потопленный корабль

            if (IsHit) return Brushes.Red; // Попадание в корабль

            if (IsMiss) return Brushes.LightSlateGray; // Промах

            if (IsHighlighted)
            {
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

        // Состояние игры клиента
        public enum GameClientState
        {
            PlacingShips,      // Расстановка кораблей (до поиска)
            SearchingGame,     // Поиск противника
            InGame,            // Игра идет
            GameFinished       // Игра окончена
        }

        private GameClientState _currentState = GameClientState.PlacingShips;

        // Таймер хода
        private DispatcherTimer _turnTimer;
        private int _remainingTurnTime = 30;
        private string _opponentName = "Соперник";
        private bool _isExitingFromGameOver = false;

        // Для отображения выстрелов
        private HashSet<string> _playerShots = new HashSet<string>();
        private HashSet<string> _opponentShots = new HashSet<string>();
        private HashSet<string> _hitsOnPlayer = new HashSet<string>();
        private HashSet<string> _hitsOnOpponent = new HashSet<string>();

        // Для задержки особых сообщений
        private bool _showingSpecialMessage = false;
        private DispatcherTimer _messageTimer;

        public GameWindow()
        {
            InitializeComponent();

            // Инициализация сетевого сервиса 
            _networkService = new ServerNetworkManager();

            _networkService.SetShipsCallback(() => _gameLogic.GetShipsForServer());

            SetupNetworkEvents();

            _gameLogic = new GameLogic();
            InitializeGameBoards();

            // Инициализация таймера хода
            _turnTimer = new DispatcherTimer();
            _turnTimer.Interval = TimeSpan.FromSeconds(1);
            _turnTimer.Tick += TurnTimer_Tick;
            TurnTimerContainer.Visibility = Visibility.Collapsed;

            // Блокируем кнопки поиска пока корабли не расставлены
            UpdateButtonsState();

            // Подключаемся к "серверу" с именем игрока
            ConnectToServer();

            // Инициализируем таймер сообщений
            _messageTimer = new DispatcherTimer();
            _messageTimer.IsEnabled = false;

            // Настраиваем обработчики событий чата
            ChatWindowControl.MessageSent += ChatWindowControl_MessageSent;
            ChatWindowControl.Closed += ChatWindowControl_Closed;
            ChatWindowControl.UnreadCountChanged += ChatWindowControl_UnreadCountChanged;
        }


        private void AddGameMessage(string message, bool isOpponentAction = false)
        {
            Dispatcher.Invoke(() =>
            {
                // Просто показываем в статусе на короткое время
                if (!_showingSpecialMessage && message.Length < 50)
                {
                    ShowSpecialMessage(message, 2000);
                }

                // Также можно добавить в чат
                ChatWindowControl.AddSystemMessage(message);
            });
        }

        // Обработчик тика таймера
        private void TurnTimer_Tick(object sender, EventArgs e)
        {
            if (_remainingTurnTime > 0)
            {
                _remainingTurnTime--;
                TurnTimeTextBlock.Text = $"⏱️ {_remainingTurnTime} сек";
                //TurnStatusTextBlock.Text = "Ваш ход";

                // Меняем цвет при малом времени
                if (_remainingTurnTime <= 10)
                {
                    TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Красный
                    TurnTimeTextBlock.Foreground = Brushes.White;
                }
                else if (_remainingTurnTime <= 20)
                {
                    TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Оранжевый
                    TurnTimeTextBlock.Foreground = Brushes.Black;
                }
                else
                {
                    TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Зеленый
                    TurnTimeTextBlock.Foreground = Brushes.White;
                }
            }
            else
            {
                // Время истекло - просто передаем ход
                _turnTimer.Stop();
                TurnTimeTextBlock.Text = "⏱️ 0 сек";
                //TurnStatusTextBlock.Text = "Время вышло";
                TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(149, 165, 166)); // Серый

                // Автоматически передаем ход противнику
                if (_networkService.IsInGame && _currentState == GameClientState.InGame)
                {
                    SetOpponentTurn();
                }
            }
        }

        // Запуск таймера хода
        private void StartTurnTimer()
        {
            _remainingTurnTime = 30;
            TurnTimerContainer.Visibility = Visibility.Visible;
            TurnTimeTextBlock.Text = $"⏱️ {_remainingTurnTime} сек";
            //TurnStatusTextBlock.Text = "Ваш ход";
            TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113));
            _turnTimer.Start();
        }

        // Остановка таймера хода
        private void StopTurnTimer()
        {
            _turnTimer.Stop();
            TurnTimerContainer.Visibility = Visibility.Collapsed;
        }

        // Установка хода противника
        private void SetOpponentTurn()
        {
            EnableOpponentBoard(false);
            GameStatus.Text = "Ход противника...";
            StopTurnTimer();
            UpdateAllBoards();
        }

        // Установка своего хода
        private void SetPlayerTurn()
        {
            EnableOpponentBoard(true);
            GameStatus.Text = "Ваш ход! Выберите клетку на поле противника";
            StartTurnTimer();
            UpdateAllBoards();
        }

        // Обновление состояния кнопок в зависимости от состояния игры
        private void UpdateButtonsState()
        {
            switch (_currentState)
            {
                case GameClientState.PlacingShips:
                    // Только в состоянии расстановки разрешаем менять корабли
                    RandomPlacementButton.IsEnabled = true;
                    ClearBoardButton.IsEnabled = true;
                    RandomOpponentButton.IsEnabled = _gameLogic.AllShipsPlaced;
                    PlayWithFriendButton.IsEnabled = _gameLogic.AllShipsPlaced;

                    RandomPlacementButton.Opacity = 1;
                    ClearBoardButton.Opacity = 1;

                    if (_gameLogic.AllShipsPlaced)
                    {
                        RandomOpponentButton.Opacity = 1;
                        PlayWithFriendButton.Opacity = 1;
                    }
                    else
                    {
                        RandomOpponentButton.Opacity = 0.5;
                        PlayWithFriendButton.Opacity = 0.5;
                    }
                    break;

                case GameClientState.SearchingGame:
                    // При поиске блокируем ВСЕ кнопки
                    RandomPlacementButton.IsEnabled = false;
                    ClearBoardButton.IsEnabled = false;
                    RandomOpponentButton.IsEnabled = false;
                    PlayWithFriendButton.IsEnabled = false;

                    RandomPlacementButton.Opacity = 0.5;
                    ClearBoardButton.Opacity = 0.5;
                    RandomOpponentButton.Opacity = 0.5;
                    PlayWithFriendButton.Opacity = 0.5;
                    break;

                case GameClientState.InGame:
                    // Во время игры блокируем кнопки расстановки
                    RandomPlacementButton.IsEnabled = false;
                    ClearBoardButton.IsEnabled = false;
                    RandomOpponentButton.IsEnabled = false;
                    PlayWithFriendButton.IsEnabled = false;

                    RandomPlacementButton.Opacity = 0.5;
                    ClearBoardButton.Opacity = 0.5;
                    RandomOpponentButton.Opacity = 0.5;
                    PlayWithFriendButton.Opacity = 0.5;
                    break;

                case GameClientState.GameFinished:
                    // После игры снова разрешаем
                    RandomPlacementButton.IsEnabled = true;
                    ClearBoardButton.IsEnabled = true;
                    RandomOpponentButton.IsEnabled = true;
                    PlayWithFriendButton.IsEnabled = true;

                    RandomPlacementButton.Opacity = 1;
                    ClearBoardButton.Opacity = 1;
                    RandomOpponentButton.Opacity = 1;
                    PlayWithFriendButton.Opacity = 1;
                    break;
            }
        }

        // Изменение состояния игры
        private void SetGameState(GameClientState newState)
        {
            _currentState = newState;
            UpdateButtonsState();
            UpdateAllBoards(); 

            switch (newState)
            {
                case GameClientState.PlacingShips:
                    GameStatus.Text = "Расставьте свои корабли";
                    EnablePlayerBoard(true);
                    EnableOpponentBoard(false);
                    StopTurnTimer();
                    break;

                case GameClientState.SearchingGame:
                    GameStatus.Text = "🔍 Поиск случайного соперника...";
                    EnablePlayerBoard(false);
                    EnableOpponentBoard(false);
                    StopTurnTimer();
                    break;

                case GameClientState.InGame:
                    GameStatus.Text = "Игра началась!";
                    EnablePlayerBoard(false);
                    UpdateAllBoards(); // Обновляем поля при начале игры
                    StopTurnTimer();
                    break;

                case GameClientState.GameFinished:
                    GameStatus.Text = "Игра окончена";
                    EnablePlayerBoard(false);
                    EnableOpponentBoard(false);
                    StopTurnTimer();
                    break;
            }
        }

        private void ChatWindowControl_UnreadCountChanged(object sender, int count)
        {
            Dispatcher.Invoke(() =>
            {
                _unreadMessages = count;
                UpdateUnreadBadge();
            });
        }

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

        private void EnableOpponentBoard(bool enable)
        {
            foreach (var cell in _opponentCells.Values)
            {
                cell.IsEnabled = enable;
                cell.Cursor = enable ? Cursors.Hand : Cursors.Arrow;
            }
        }

        // ✅ ДОБАВЛЕНО: Включение/отключение своего поля
        private void EnablePlayerBoard(bool enable)
        {
            foreach (var cell in _playerCells.Values)
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
                    if (isConnected && _currentState == GameClientState.PlacingShips)
                    {
                        SetGameState(GameClientState.PlacingShips);
                    }
                });
            };

            _networkService.OnGameStarted += (startMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _opponentName = !string.IsNullOrEmpty(startMessage.OpponentName)
                        ? startMessage.OpponentName
                        : "Соперник";

                    GameStatus.Text = $"Игра началась! Противник: {_opponentName}";
                    ConnectionStatus.Text = "В игре";

                    // ✅ Устанавливаем состояние "В игре"
                    SetGameState(GameClientState.InGame);

                    ChatWindowControl.AddSystemMessage($"Игра началась. Ваш соперник: {_opponentName}");
                    OpenChatButton.Visibility = Visibility.Visible;

                    _playerShots.Clear();
                    _hitsOnOpponent.Clear();
                });
            };

            _networkService.OnGameEnded += (endMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // ✅ Устанавливаем состояние "Игра окончена"
                    SetGameState(GameClientState.GameFinished);

                    OpenChatButton.Visibility = Visibility.Collapsed;
                    ChatWindowControl.Visibility = Visibility.Collapsed;

                    string resultMessage = endMessage.Winner == "player"
                        ? "Вы победили! Поздравляем!"
                        : "Вы проиграли. Попробуйте еще раз!";
                    ChatWindowControl.AddSystemMessage(resultMessage);

                    GameOverWindow gameOverWindow = new GameOverWindow(
                        endMessage.Winner,
                        _opponentName,
                        endMessage.Stats);

                    gameOverWindow.Owner = this;
                    bool? dialogResult = gameOverWindow.ShowDialog();

                    if (dialogResult == true)
                    {
                        if (gameOverWindow.PlayAgain)
                        {
                            ResetGameForNewRound();
                        }
                        else
                        {
                            _isExitingFromGameOver = true;
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
                    Console.WriteLine($"ContinueTurn: {result.ContinueTurn}");

                    _playerShots.Add(cellKey);

                    // Обновляем клетку противника
                    UpdateOpponentCellAfterShot(result.Row, result.Col, result.Result, result.ShipName);

                    if (result.Result == "sunk")
                    {
                        AddGameMessage($"Вы потопили {result.ShipName}!");
                    }
                    else if (result.Result == "hit")
                    {
                        AddGameMessage("Попадание!");
                    }
                    else if (result.Result == "miss")
                    {
                        AddGameMessage("Промах!");
                    }

                    // Проверяем, продолжается ли наш ход
                    if (result.ContinueTurn)
                    {
                        SetPlayerTurn(); // Продолжаем ход
                    }
                    else
                    {
                        SetOpponentTurn(); // Ход переходит противнику
                    }

                    // Проверяем конец игры
                    if (result.IsGameOver)
                    {
                        // Игра окончена - сервер отправит OnGameEnded
                    }
                });
            };

            _networkService.OnOpponentShoot += (shoot) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string cellKey = $"{shoot.Row},{shoot.Col}";

                    Console.WriteLine($"=== UI: Выстрел противника в {cellKey} ===");

                    _opponentShots.Add(cellKey);

                    if (shoot.IsHit)
                    {
                        _hitsOnPlayer.Add(cellKey);
                    }

                    // Обновляем свое поле
                    UpdatePlayerCellAfterShot(shoot.Row, shoot.Col, shoot.IsHit);

                    if (shoot.IsHit)
                    {
                        ShowSpecialMessage($"Противник попал в ваш корабль!", 3000);
                    }
                    else
                    {
                        ShowSpecialMessage("Противник промахнулся!", 2000);
                    }

                    // После выстрела противника - наш ход
                    SetPlayerTurn();
                });
            };

            _networkService.OnGameStateUpdated += (state) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_showingSpecialMessage) return;

                    if (state.Status == "playing")
                    {
                        if (state.CurrentTurn == "player")
                        {
                            SetPlayerTurn();
                        }
                        else
                        {
                            SetOpponentTurn();
                        }
                    }
                    else if (state.Status == "finished")
                    {
                        StopTurnTimer();
                        EnableOpponentBoard(false);
                        EnablePlayerBoard(false);
                    }
                });
            };

            _networkService.OnError += (error) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AlertService.ShowNetworkAlert(error);

                    if (error.Code == "SERVER_DISCONNECTED")
                    {
                        foreach (var cell in _opponentCells.Values)
                        {
                            cell.IsEnabled = false;
                        }

                        foreach (var cell in _playerCells.Values)
                        {
                            cell.IsEnabled = false;
                        }

                        GameStatus.Text = "Сервер недоступен. Игра завершена.";
                        StopTurnTimer();

                        Task.Delay(5000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (!_isExitingFromGameOver)
                                {
                                    ReturnToMainMenu();
                                }
                            });
                        });
                    }
                });
            };

            _networkService.OnOpponentDisconnected += (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ChatWindowControl.AddSystemMessage($"⚠️ {message}");
                    ShowSpecialMessage(message, 5000);

                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.IsEnabled = false;
                    }

                    GameStatus.Text = "Противник отключился";
                    StopTurnTimer();
                });
            };

            _networkService.OnChatMessage += (chatMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string playerName = Application.Current.Properties.Contains("Username")
                        ? Application.Current.Properties["Username"].ToString()
                        : "Вы";

                    if (chatMessage.IsSystem)
                    {
                        ChatWindowControl.AddSystemMessage(chatMessage.Message);
                    }
                    else
                    {
                        bool isOwn = !chatMessage.IsFromOpponent;
                        string senderDisplayName = isOwn ? "Вы" : chatMessage.Sender;

                        ChatWindowControl.AddMessage(senderDisplayName, chatMessage.Message, isOwn);
                    }

                    if (ChatWindowControl.Visibility != Visibility.Visible &&
                        !chatMessage.IsSystem &&
                        chatMessage.IsFromOpponent)
                    {
                        ShowSpecialMessage($"Новое сообщение от {chatMessage.Sender}", 2000);
                    }
                });
            };
        }

        private void UpdateAllBoards()
        {
            UpdatePlayerBoardVisual();
            UpdateOpponentBoard();
        }

        private void SyncCellStateWithData(CellState cellState, int row, int col, string cellKey, bool isPlayerCell)
        {
            if (isPlayerCell)
            {
                // Для своего поля: проверяем реальные данные
                bool hasShip = _gameLogic.GetPlayerShipCells()
                    .Any(c => c.row == row && c.col == col);

                cellState.HasShip = hasShip;
                cellState.IsHit = _hitsOnPlayer.Contains(cellKey);
                cellState.IsMiss = _opponentShots.Contains(cellKey);

                // Обновляем цвет в соответствии с состоянием
                if (cellState.IsHit)
                {
                    cellState.BaseColor = cellState.IsSunk ? Brushes.DarkRed : Brushes.Red;
                }
                else if (cellState.IsMiss)
                {
                    cellState.BaseColor = Brushes.LightBlue;
                }
                else if (hasShip)
                {
                    cellState.BaseColor = Brushes.DarkGray;
                }
                else
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }
            else
            {
                // Для поля противника: только выстрелы
                cellState.HasShip = false;
                cellState.IsHit = _hitsOnOpponent.Contains(cellKey);
                cellState.IsMiss = _playerShots.Contains(cellKey) && !cellState.IsHit;

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


        // Метод для обновления клетки на доске противника после выстрела
        private void UpdateOpponentCellAfterShot(int row, int col, string result, string shipName = "")
        {
            string cellKey = $"{row},{col}";

            if (!_opponentCells.TryGetValue(cellKey, out Border cell)) return;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;

                switch (result)
                {
                    case "hit":
                        cellState.IsHit = true;
                        cellState.IsMiss = false;
                        cellState.BaseColor = Brushes.Red;
                        _hitsOnOpponent.Add(cellKey);
                        ShowCellSymbol(cell, "✖", Brushes.White); // Символ попадания
                        break;

                    case "sunk":
                        cellState.IsHit = true;
                        cellState.IsSunk = true;
                        cellState.BaseColor = Brushes.DarkRed;
                        _hitsOnOpponent.Add(cellKey);
                        ShowCellSymbol(cell, "☠", Brushes.White); // Символ потопления

                        // Помечаем клетки вокруг потопленного корабля
                        MarkCellsAroundSunkShip(row, col, false);
                        break;

                    case "miss":
                        cellState.IsMiss = true;
                        cellState.IsHit = false;
                        cellState.BaseColor = Brushes.LightGray;
                        ShowCellSymbol(cell, "○", Brushes.DarkGray); // Символ промаха
                        break;
                }

                cell.Background = cellState.GetCurrentColor();
                cell.IsEnabled = false; // Делаем клетку неактивной после выстрела
            }
        }

        // Метод для обновления клетки на своей доске после выстрела противника
        private void UpdatePlayerCellAfterShot(int row, int col, bool isHit)
        {
            string cellKey = $"{row},{col}";

            if (!_playerCells.TryGetValue(cellKey, out Border cell)) return;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;

                if (isHit)
                {
                    cellState.IsHit = true;
                    cellState.BaseColor = Brushes.Red;
                    _hitsOnPlayer.Add(cellKey);
                    ShowCellSymbol(cell, "✖", Brushes.White);

                    // Проверяем, потоплен ли корабль
                    if (IsShipSunkAt(row, col, true))
                    {
                        cellState.IsSunk = true;
                        cellState.BaseColor = Brushes.DarkRed;
                        ShowCellSymbol(cell, "☠", Brushes.White);
                        MarkCellsAroundSunkShip(row, col, true);
                    }
                }
                else
                {
                    cellState.IsMiss = true;
                    cellState.BaseColor = Brushes.LightBlue;
                    ShowCellSymbol(cell, "○", Brushes.DarkGray);
                }

                cell.Background = cellState.GetCurrentColor();

                // Обновляем символ на поле
                UpdatePlayerCellSymbol(cell, cellState);
            }
        }

        // Помечаем клетки вокруг потопленного корабля
        private void MarkCellsAroundSunkShip(int centerRow, int centerCol, bool onPlayerBoard)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int row = centerRow + dx;
                    int col = centerCol + dy;

                    if (row >= 0 && row < 10 && col >= 0 && col < 10)
                    {
                        string cellKey = $"{row},{col}";

                        // Пропускаем саму клетку корабля
                        if (dx == 0 && dy == 0) continue;

                        if (onPlayerBoard)
                        {
                            if (_playerCells.TryGetValue(cellKey, out Border cell))
                            {
                                if (cell.Tag is Tuple<string, CellState> tag)
                                {
                                    var (_, cellState) = tag;

                                    // Если это не клетка корабля и еще не отмечена
                                    if (!cellState.HasShip && !cellState.IsMiss)
                                    {
                                        cellState.IsMiss = true;
                                        cellState.IsAroundSunk = true;
                                        cellState.BaseColor = Brushes.LightGray;
                                        cell.Background = cellState.GetCurrentColor();
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (_opponentCells.TryGetValue(cellKey, out Border cell))
                            {
                                if (cell.Tag is Tuple<string, CellState> tag)
                                {
                                    var (_, cellState) = tag;

                                    // Если еще не стреляли сюда
                                    if (!cellState.IsHit && !cellState.IsMiss)
                                    {
                                        cellState.IsMiss = true;
                                        cellState.IsAroundSunk = true;
                                        cellState.BaseColor = Brushes.LightGray;
                                        cell.Background = cellState.GetCurrentColor();
                                        cell.IsEnabled = false; // Делаем неактивной
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Проверка, потоплен ли корабль 
        private bool IsShipSunkAt(int row, int col, bool onPlayerBoard)
        {
            // В реальной реализации нужно проверять весь корабль
            // Здесь упрощенно - если все соседние клетки корабля попадания
            string cellKey = $"{row},{col}";

            if (onPlayerBoard)
            {
                // Проверяем все 4 направления
                int[] dx = { 0, 1, 0, -1 };
                int[] dy = { 1, 0, -1, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int nr = row + dx[i];
                    int nc = col + dy[i];
                    string neighborKey = $"{nr},{nc}";

                    if (nr >= 0 && nr < 10 && nc >= 0 && nc < 10)
                    {
                        if (_playerCells.TryGetValue(neighborKey, out Border neighborCell))
                        {
                            if (neighborCell.Tag is Tuple<string, CellState> neighborTag)
                            {
                                var (_, neighborState) = neighborTag;
                                if (neighborState.HasShip && !neighborState.IsHit)
                                {
                                    return false; // Есть неподбитая клетка корабля
                                }
                            }
                        }
                    }
                }
            }

            return true; // Все клетки корабля подбиты 
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

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;

                    // Синхронизируем состояние с реальными данными
                    SyncCellStateWithData(cellState, row, col, cellKey, true);

                    // Обновляем символ
                    UpdatePlayerCellSymbol(cell, cellState);

                    // Обновляем цвет
                    cell.Background = cellState.GetCurrentColor();
                }
            }

            Console.WriteLine($"Попаданий по вам: {_hitsOnPlayer.Count}, Промахов противника: {_opponentShots.Count}");
        }

        private void UpdatePlayerCellSymbol(Border cell, CellState cellState)
        {
            if (cell.Child is TextBlock textBlock)
            {
                if (cellState.IsHit && cellState.IsSunk)
                {
                    textBlock.Text = "☠";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsHit)
                {
                    textBlock.Text = "✖";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsMiss)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    textBlock.Visibility = Visibility.Hidden;
                }
            }
        }



        private async void ChatWindowControl_MessageSent(object sender, string message)
        {
            try
            {
                string playerName = Application.Current.Properties.Contains("Username")
                    ? Application.Current.Properties["Username"].ToString()
                    : "Вы";

                ChatWindowControl.AddMessage(playerName, message, isOwn: true);

                if (_networkService.IsInGame && _networkService.IsConnected)
                {
                    await _networkService.SendChatMessageAsync(message);
                }
                else
                {
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
            ChatWindowControl.Visibility = Visibility.Collapsed;
        }

        private void OpenChatButton_Click(object sender, RoutedEventArgs e)
        {
            ChatWindowControl.Visibility = Visibility.Visible;
            ChatWindowControl.MarkAsRead();
            UpdateUnreadBadge();
        }

        private void ResetGameForNewRound()
        {
            // ✅ Возвращаем в состояние расстановки кораблей
            SetGameState(GameClientState.PlacingShips);

            _gameLogic.ResetForNewGame();

            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();

            UpdateYourBoard();

            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
            }

            EnablePlayerBoard(true);

            OpenChatButton.Visibility = Visibility.Collapsed;
            ChatWindowControl.Visibility = Visibility.Collapsed;
            ChatWindowControl.ClearChat();

            UpdateShipsInfo();

            _networkService.LeaveGameAsync();

            GameStatus.Text = "Новая игра! Расставьте корабли.";
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }

        private void ReturnToMainMenu()
        {
            _networkService.LeaveGameAsync();
            _gameLogic.ClearBoard();
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }

        private void ShowSpecialMessage(string message, int durationMilliseconds)
        {
            if (_messageTimer != null)
            {
                _messageTimer.Stop();
                _messageTimer = null;
            }

            _showingSpecialMessage = true;
            GameStatus.Text = message;

            _messageTimer = new DispatcherTimer();
            _messageTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
            _messageTimer.Tick += (s, e) =>
            {
                _messageTimer.Stop();
                _showingSpecialMessage = false;

                // Восстанавливаем статус в зависимости от состояния
                if (_currentState == GameClientState.InGame)
                {
                    GameStatus.Text = "Ваш ход!";
                }
                else
                {
                    GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                }
            };

            _messageTimer.Start();
        }

        private void UpdateUIForGameState(GameStateMessage state)
        {
            if (_showingSpecialMessage) return;

            switch (state.Status)
            {
                case "placing":
                    GameStatus.Text = "Расставьте свои корабли";
                    OpenChatButton.Visibility = Visibility.Collapsed;
                    EnableOpponentBoard(false);
                    break;
                case "playing":
                    if (state.CurrentTurn == "player")
                    {
                        GameStatus.Text = "Ваш ход! Выберите клетку на поле противника";
                        OpenChatButton.Visibility = Visibility.Visible;
                        EnableOpponentBoard(true);
                    }
                    else
                    {
                        GameStatus.Text = "Ход противника...";
                        OpenChatButton.Visibility = Visibility.Visible;
                        EnableOpponentBoard(false);
                    }
                    break;
                case "finished":
                    EnableOpponentBoard(false);
                    EnablePlayerBoard(false);
                    break;
            }
        }

        private void InitializeGameBoards()
        {
            InitializeBoard(YourBoardGrid, true);
            InitializeBoard(OpponentBoardGrid, false);
            UpdateShipsInfo();

            // Инициализируем все клетки противника как неактивные
            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
                cell.Cursor = Cursors.Arrow;
            }

            UpdateYourBoard();
        }

        private void InitializeBoard(Grid boardGrid, bool isYourBoard)
        {
            boardGrid.Children.Clear();
            boardGrid.RowDefinitions.Clear();
            boardGrid.ColumnDefinitions.Clear();

            if (isYourBoard)
                _playerCells.Clear();
            else
                _opponentCells.Clear();

            for (int i = 0; i <= GridSize; i++)
            {
                boardGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(CellSize) });
                boardGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(CellSize) });
            }

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
                        cell.IsEnabled = false;
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
                Background = cellState.BaseColor,
                Tag = new Tuple<string, CellState>($"{row},{col}", cellState), // ← ВЕРНУЛИ старый Tag
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
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }

            return cell;
        }

        private void ShowCellSymbol(Border cell, string symbol, Brush color)
        {
            // Проверяем, есть ли уже TextBlock в клетке
            if (cell.Child is TextBlock existingTextBlock)
            {
                existingTextBlock.Text = symbol;
                existingTextBlock.Foreground = color;
                existingTextBlock.Visibility = Visibility.Visible;
                existingTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
                existingTextBlock.VerticalAlignment = VerticalAlignment.Center;
                existingTextBlock.FontSize = 16;
                existingTextBlock.FontWeight = FontWeights.Bold;
            }
            else
            {
                // Создаем новый TextBlock
                var textBlock = new TextBlock
                {
                    Text = symbol,
                    Foreground = color,
                    Visibility = Visibility.Visible,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold
                };
                cell.Child = textBlock;
            }
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, cellState) = tag;

                // Определяем, на чье поле навели
                bool isPlayerCell = _playerCells.ContainsKey(coordsStr);

                if (isPlayerCell)
                {
                    // Только для своего поля обновляем состояние
                    var coords = coordsStr.Split(',');
                    int row = int.Parse(coords[0]);
                    int col = int.Parse(coords[1]);

                    // Только подсветка, не меняем базовые данные
                    cellState.IsHighlighted = true;
                    cell.Background = cellState.GetCurrentColor();
                }
                else
                {
                    // Для поля противника - только подсветка
                    cellState.IsHighlighted = true;
                    cell.Background = cellState.GetCurrentColor();
                }
            }
        }

        private void Cell_MouseLeave(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, cellState) = tag;

                // Просто сбрасываем подсветку
                cellState.IsHighlighted = false;
                cell.Background = cellState.GetCurrentColor();
            }
        }

        private void UpdateCellState(CellState cellState, int row, int col, string cellKey, bool isPlayerCell)
        {
            if (isPlayerCell)
            {
                // Только для своего поля
                bool isCurrentShipCell = _gameLogic.GetCurrentShipBeingPlacedCells()
                    .Any(c => c.row == row && c.col == col);
                bool hasShip = _gameLogic.GetPlayerShipCells()
                    .Any(c => c.row == row && c.col == col);

                cellState.IsPlacing = isCurrentShipCell;
                cellState.HasShip = hasShip;
                cellState.IsHit = _hitsOnPlayer.Contains(cellKey);
                cellState.IsMiss = _opponentShots.Contains(cellKey);

                if (isCurrentShipCell)
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(106, 137, 204));
                }
                else if (hasShip)
                {
                    cellState.BaseColor = Brushes.DarkGray;
                }
                else if (cellState.IsHit)
                {
                    cellState.BaseColor = Brushes.Red;
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
                // ДЛЯ ПОЛЯ ПРОТИВНИКА: только выстрелы, без информации о кораблях!
                cellState.HasShip = false; // никогда не показываем корабли противника
                cellState.IsPlacing = false;
                cellState.IsHit = _hitsOnOpponent.Contains(cellKey);
                cellState.IsMiss = _playerShots.Contains(cellKey) && !_hitsOnOpponent.Contains(cellKey);

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

        private void YourCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ✅ Проверяем, можно ли расставлять корабли в текущем состоянии
            if (_currentState != GameClientState.PlacingShips) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, _) = tag;
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
                else
                {
                    ShowSpecialMessage("Нельзя поставить корабль здесь!", 2000);
                }
            }
        }

        private void YourCell_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ✅ Проверяем, можно ли изменять корабли в текущем состоянии
            if (_currentState != GameClientState.PlacingShips) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                if (_gameLogic.IsPlacingShip())
                {
                    _gameLogic.CancelCurrentShipPlacement();
                }
                else
                {
                    _gameLogic.RemoveLastCell();
                }

                UpdateYourBoard();
                UpdateShipsInfo();
                UpdateButtonsState();
            }
        }

        private async void OpponentCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ✅ Проверяем, можно ли стрелять в текущем состоянии
            if (_currentState != GameClientState.InGame) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, _) = tag;
                var coords = coordsStr.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                await _networkService.ShootAsync(row, col);
            }
        }

        private void UpdateYourBoard()
        {
            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
                string cellKey = kvp.Key;

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (coordsStr, cellState) = tag;

                    // Обновляем состояние
                    UpdateCellState(cellState, row, col, coordsStr, true);

                    // Обновляем цвет
                    cell.Background = cellState.GetCurrentColor();

                    // Обновляем символы для попаданий
                    UpdatePlayerCellSymbol(cell, cellState);
                }
            }
        }

        private void UpdateOpponentBoard()
        {
            Console.WriteLine("=== Обновление визуализации поля противника ===");

            foreach (var kvp in _opponentCells)
            {
                var cell = kvp.Value;
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
                string cellKey = kvp.Key;

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;

                    // Синхронизируем состояние с реальными данными
                    SyncCellStateWithData(cellState, row, col, cellKey, false);

                    // Обновляем символ
                    UpdateOpponentCellSymbol(cell, cellState);

                    // Обновляем цвет
                    cell.Background = cellState.GetCurrentColor();

                    // Обновляем активность клетки
                    cell.IsEnabled = !cellState.IsHit && !cellState.IsMiss &&
                                    _currentState == GameClientState.InGame &&
                                    IsMyTurn();
                }
            }
        }

        private void UpdateOpponentCellSymbol(Border cell, CellState cellState)
        {
            if (cell.Child is TextBlock textBlock)
            {
                if (cellState.IsHit && cellState.IsSunk)
                {
                    textBlock.Text = "☠";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsHit)
                {
                    textBlock.Text = "✖";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsMiss)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    textBlock.Visibility = Visibility.Hidden;
                }
            }
        }

        private bool IsMyTurn()
        {
            // Проверяем, чей сейчас ход
            return GameStatus.Text.Contains("Ваш ход") ||
                   GameStatus.Text.Contains("Попадание") ||
                   (_currentState == GameClientState.InGame && GameStatus.Text.Contains("Ход противника") == false);
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

            ShipsInfo.Text = $"Осталось расставить: {total4 - placed4}x4, {total3 - placed3}x3, {total2 - placed2}x2, {total1 - placed1}x1";
            GameStatus.Text = _gameLogic.GetCurrentShipInfo();
        }

        private void RandomPlacementButton_Click(object sender, RoutedEventArgs e)
        {
            // ✅ Проверяем, можно ли расставлять корабли
            if (_currentState != GameClientState.PlacingShips) return;

            _gameLogic.RandomlyPlaceShips();
            UpdateYourBoard();
            UpdateShipsInfo();
            UpdateButtonsState();
            ShowSpecialMessage("Корабли расставлены случайным образом!", 2000);
        }

        private async void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
            // ✅ Проверяем, можно ли очищать поле
            if (_currentState != GameClientState.PlacingShips) return;

            _gameLogic.ClearBoard();
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();
            UpdateYourBoard();

            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
            }

            UpdateShipsInfo();
            UpdateButtonsState();
            ShowSpecialMessage("Поле очищено. Начинайте расстановку заново.", 3000);
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

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void PlayWithFriendButton_Click(object sender, RoutedEventArgs e)
        {
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

            // Переходим в состояние поиска игры
            SetGameState(GameClientState.SearchingGame);

            StartSearch();

            // Запускаем поиск игры
            var gameId = await _networkService.CreateGameAsync("random");

            if (gameId != null)
            {
                // Немедленно нашли игру
                CancelSearch();
                SetGameState(GameClientState.InGame);
            }
            else
            {
                // В лобби - ждем
                Console.WriteLine("⏳ В лобби, ждем противника...");
            }
        }

        private void StartSearch()
        {
            _isSearching = true;
            PlayWithFriendButton.Visibility = Visibility.Collapsed;
            RandomOpponentButton.Visibility = Visibility.Collapsed;
            OpenChatButton.Visibility = Visibility.Collapsed;
            SearchIndicator.Visibility = Visibility.Visible;
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
            PlayWithFriendButton.Visibility = Visibility.Visible;
            RandomOpponentButton.Visibility = Visibility.Visible;
            OpenChatButton.Visibility = Visibility.Collapsed;
            SearchIndicator.Visibility = Visibility.Collapsed;

            // Возвращаем в состояние расстановки кораблей
            SetGameState(GameClientState.PlacingShips);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
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