using BattleShip.Client.Services;
using BattleShip.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Net.Http.Json;

namespace BattleShip.Client
{
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
        public bool WasShot => IsHit || IsMiss;

        public Brush GetCurrentColor()
        {
            // 1. Клетки вокруг потопленных кораблей
            if (IsAroundSunk)
            {
                return Brushes.LightGray;
            }

            // 2. Потом потопленные клетки
            if (IsSunk)
            {
                return Brushes.DarkRed;
            }

            // 3. Потом попадания
            if (IsHit)
            {
                return Brushes.Red;
            }

            // 4. Потом обычные промахи
            if (IsMiss)
            {
                return Brushes.LightBlue;
            }

            // 5. Подсветка
            if (IsHighlighted && BaseColor is SolidColorBrush baseBrush)
            {
                var color = baseBrush.Color;
                var highlightedColor = Color.FromArgb(
                    255,
                    (byte)Math.Min(color.R + 40, 255),
                    (byte)Math.Min(color.G + 40, 255),
                    (byte)Math.Min(color.B + 40, 255));
                return new SolidColorBrush(highlightedColor);
            }

            return BaseColor;
        }
    }

    public partial class GameWindow : Window
    {
        // Константы
        private const int GridSize = 10;
        private const int CellSize = 35;

        // Состояния UI
        private enum GameClientState
        {
            PlacingShips,
            SearchingGame,
            InGame,
            GameFinished
        }
        private GameClientState _currentState = GameClientState.PlacingShips;

        // Сервисы
        private readonly INetworkService _networkService;
        private readonly GameLogic _gameLogic;

        // UI элементы
        private readonly Dictionary<string, Border> _playerCells = new();
        private readonly Dictionary<string, Border> _opponentCells = new();

        // Данные игры
        private string _playerName = "Игрок";
        private string _playerId;
        private string _opponentName = "Соперник";
        private bool _isMyTurn = false;
        private bool _isSearching = false;
        private int _unreadMessages = 0;
        private bool _isExitingFromGameOver = false;
        private bool _gameOverWindowShown = false;

        // Таймеры
        private readonly DispatcherTimer _turnTimer;
        private int _remainingTurnTime = 30;
        private readonly DispatcherTimer _messageTimer;
        private bool _showingSpecialMessage = false;

        private bool _turnTimerRunning = false;

        // JSON сериализация
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Наборы для отслеживания выстрелов
        private readonly HashSet<string> _playerShots = new();
        private readonly HashSet<string> _opponentShots = new();
        private readonly HashSet<string> _hitsOnPlayer = new();
        private readonly HashSet<string> _hitsOnOpponent = new();

        public GameWindow()
        {
            InitializeComponent();

            // Инициализация сервисов
            _networkService = new ServerNetworkManager();
            _gameLogic = new GameLogic();
            _networkService.SetShipsCallback(() => _gameLogic.GetShipsForServer());

            // Настройка UI
            InitializeGameBoards();

            // Настройка таймеров
            _turnTimer = new DispatcherTimer();
            _turnTimer.Interval = TimeSpan.FromSeconds(1);
            _turnTimer.Tick += TurnTimer_Tick;
            TurnTimerContainer.Visibility = Visibility.Collapsed;

            _messageTimer = new DispatcherTimer();
            _messageTimer.IsEnabled = false;

            // Настройка сетевых событий
            SetupNetworkEvents();

            // Настройка чата
            ChatWindowControl.MessageSent += ChatWindowControl_MessageSent;
            ChatWindowControl.Closed += ChatWindowControl_Closed;
            ChatWindowControl.UnreadCountChanged += ChatWindowControl_UnreadCountChanged;

            // Подключение к серверу
            ConnectToServer();
        }

        // ==================== ИНИЦИАЛИЗАЦИЯ И ОСНОВНЫЕ МЕТОДЫ ====================

        private void InitializeGameBoards()
        {
            InitializeBoard(YourBoardGrid, true);
            InitializeBoard(OpponentBoardGrid, false);
            UpdateShipsInfo();

            // Изначально поле противника заблокировано
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

            // Очищаем словарь клеток
            if (isYourBoard)
                _playerCells.Clear();
            else
                _opponentCells.Clear();

            // Создаем строки и столбцы
            for (int i = 0; i <= GridSize; i++)
            {
                boardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellSize) });
                boardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellSize) });
            }

            // Буквы для столбцов
            for (int col = 0; col < GridSize; col++)
            {
                var letter = new TextBlock
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

            // Цифры для строк
            for (int row = 0; row < GridSize; row++)
            {
                var number = new TextBlock
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

            // Создаем клетки
            for (int row = 0; row < GridSize; row++)
            {
                for (int col = 0; col < GridSize; col++)
                {
                    var cell = CreateCell(row, col, isYourBoard);
                    Grid.SetRow(cell, row + 1);
                    Grid.SetColumn(cell, col + 1);
                    boardGrid.Children.Add(cell);

                    var cellKey = $"{row},{col}";
                    if (isYourBoard)
                        _playerCells[cellKey] = cell;
                    else
                        _opponentCells[cellKey] = cell;
                }
            }
        }

        private Border CreateCell(int row, int col, bool isYourBoard)
        {
            var cellState = new CellState
            {
                BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60))
            };

            var cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110)),
                BorderThickness = new Thickness(1),
                Background = cellState.BaseColor,
                Tag = new Tuple<string, CellState>($"{row},{col}", cellState),
                Cursor = isYourBoard ? Cursors.Hand : Cursors.Arrow
            };

            // Добавляем обработчики событий
            if (isYourBoard)
            {
                cell.MouseLeftButtonDown += YourCell_MouseLeftButtonDown;
                cell.MouseRightButtonDown += YourCell_MouseRightButtonDown;
            }
            else
            {
                cell.MouseLeftButtonDown += OpponentCell_MouseLeftButtonDown;
            }

            cell.MouseEnter += Cell_MouseEnter;
            cell.MouseLeave += Cell_MouseLeave;

            return cell;
        }

        // ==================== СЕТЕВОЕ ВЗАИМОДЕЙСТВИЕ ====================

        private async void ConnectToServer()
        {
            _playerName = Application.Current.Properties.Contains("Username")
                ? Application.Current.Properties["Username"].ToString()
                : "Игрок";

            await _networkService.ConnectAsync(_playerName);
            _playerId = _networkService.PlayerId;
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

            _networkService.OnGameStarted += async (startMessage) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    _opponentName = !string.IsNullOrEmpty(startMessage.OpponentName)
                        ? startMessage.OpponentName
                        : "Соперник";

                    GameStatus.Text = $"Игра началась! Противник: {_opponentName}";
                    ConnectionStatus.Text = "В игре";

                    SetGameState(GameClientState.InGame);

                    // ОЧИЩАЕМ ЧАТ ПЕРЕД НОВОЙ ИГРОЙ
                    ChatWindowControl.ClearChat();
                    await LoadChatHistory();
                    ChatWindowControl.AddSystemMessage($"Игра началась. Ваш соперник: {_opponentName}");

                    // Загружаем историю чата если есть
                    await LoadChatHistory();

                    // ЗАПУСКАЕМ ПЕРИОДИЧЕСКОЕ ОБНОВЛЕНИЕ
                    _ = Task.Run(StartPeriodicUpdate);

                    // Загружаем начальное состояние досок
                    await LoadGameStateFromServer();

                    // Определяем чей ход
                    await Task.Delay(1000);
                    await UpdateTurnFromServer();

                    OpenChatButton.Visibility = Visibility.Visible;

                    // Очищаем историю выстрелов
                    _playerShots.Clear();
                    _opponentShots.Clear(); 
                    _hitsOnPlayer.Clear();
                    _hitsOnOpponent.Clear();
                });
            };

            _networkService.OnGameEnded += (endMessage) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    // Проверяем, не показывали ли уже окно
                    if (_gameOverWindowShown) return;
                    _gameOverWindowShown = true;

                    // Останавливаем все процессы
                    StopMyTurn();
                    SetGameState(GameClientState.GameFinished);

                    OpenChatButton.Visibility = Visibility.Collapsed;
                    ChatWindowControl.Visibility = Visibility.Collapsed;

                    string resultMessage = endMessage.Winner == "player"
                        ? "Вы победили! Поздравляем!"
                        : "Вы проиграли. Попробуйте еще раз!";
                    ChatWindowControl.AddSystemMessage(resultMessage);
                    

                    // Закрываем предыдущее окно если оно открыто
                    CloseGameOverWindowIfOpen();

                    var gameOverWindow = new GameOverWindow(
                        endMessage.Winner,
                        _opponentName,
                        endMessage.Stats)
                    {
                        Owner = this
                    };

                    // Подписываемся на закрытие окна
                    gameOverWindow.Closed += (s, args) =>
                    {
                        _gameOverWindowShown = false; // Сбрасываем флаг при закрытии
                    };

                    var dialogResult = gameOverWindow.ShowDialog();

                    // Сбрасываем флаг после закрытия
                    _gameOverWindowShown = false;

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

            _networkService.OnShootResult += async (result) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    Console.WriteLine($"=== UI: Получен результат выстрела ===");
                    Console.WriteLine($"Клетка: {result.Row},{result.Col}, Результат: {result.Result}");

                    var cellKey = $"{result.Row},{result.Col}";
                    _playerShots.Add(cellKey);

                    // Обновляем конкретную клетку
                    UpdateOpponentCellAfterShot(result.Row, result.Col, result.Result, result.ShipName);

                    // Загружаем полное состояние с сервера
                    await Task.Delay(300);
                    await LoadGameStateFromServer();

                    // Показываем сообщение
                    if (result.Result == "sunk")
                    {
                        ShowSpecialMessage($"Вы потопили {result.ShipName}!", 3000);
                    }
                    else if (result.Result == "hit")
                    {
                        ShowSpecialMessage("Попадание!", 2000);
                    }
                    else if (result.Result == "miss")
                    {
                        ShowSpecialMessage("Промах!", 1500);
                    }

                    // Сбрасываем таймер только при получении нового хода
                    if (result.ContinueTurn)
                    {
                        // Сбрасываем таймер на 30 секунд, но НЕ перезапускаем
                        ResetTurnTimer();
                    }
                    else
                    {
                        StopMyTurn();
                        await Task.Delay(1000);
                        await UpdateTurnFromServer();
                    }
                });
            };

            _networkService.OnOpponentShoot += async (shoot) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    var cellKey = $"{shoot.Row},{shoot.Col}";
                    Console.WriteLine($"=== UI: Выстрел противника в {cellKey} ===");

                    _opponentShots.Add(cellKey);

                    if (shoot.IsHit)
                    {
                        _hitsOnPlayer.Add(cellKey);
                        ShowSpecialMessage($"Противник попал в ваш корабль!", 3000);
                    }
                    else
                    {
                        ShowSpecialMessage("Противник промахнулся!", 2000);
                    }

                    // СРАЗУ обновляем ваше поле визуально
                    UpdatePlayerBoardVisual();

                    // НЕМЕДЛЕННО загружаем полное состояние с сервера
                    await Task.Delay(300);
                    await LoadGameStateFromServer();

                    await Task.Delay(1000);
                    await UpdateTurnFromServer();
                });
            };

            _networkService.OnGameStateUpdated += async (state) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    if (_showingSpecialMessage) return;

                    Console.WriteLine($"=== UI: Обновление состояния игры ===");
                    Console.WriteLine($"Статус: {state.Status}, Ход: {state.CurrentTurn}");

                    if (state.Status == "playing")
                    {
                        if (state.CurrentTurn == "player" && !_isMyTurn)
                        {
                            await SetPlayerTurn();
                        }
                        else if (state.CurrentTurn == "opponent" && _isMyTurn)
                        {
                            await SetOpponentTurn();
                        }
                    }
                    else if (state.Status == "finished")
                    {
                        StopMyTurn();
                        EnableOpponentBoard(false);
                        EnablePlayerBoard(false);
                        GameStatus.Text = "Игра окончена";
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
                        EnableOpponentBoard(false);
                        EnablePlayerBoard(false);
                        GameStatus.Text = "Сервер недоступен. Игра завершена.";
                        StopMyTurn();

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

                    // Добавляем сообщение в чат
                    ChatWindowControl.AddSystemMessage("💬 Чат отключен - противник покинул игру");

                    EnableOpponentBoard(false);
                    GameStatus.Text = "Противник отключился";
                    StopMyTurn();
                });
            };

            _networkService.OnChatMessage += (chatMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (chatMessage.IsSystem)
                        {
                            ChatWindowControl.AddSystemMessage(chatMessage.Message);
                        }
                        else
                        {
                            // Теперь sender - это имя игрока
                            bool isOwn = chatMessage.Sender == _playerName || chatMessage.Sender == "Вы";
                            string senderDisplayName = isOwn ? "Вы" : chatMessage.Sender;

                            ChatWindowControl.AddMessage(senderDisplayName, chatMessage.Message, isOwn);

                            // Показываем уведомление если чат закрыт
                            if (ChatWindowControl.Visibility != Visibility.Visible && !isOwn)
                            {
                                ShowSpecialMessage($"💬 Новое сообщение от {chatMessage.Sender}", 2000);

                                _unreadMessages++;
                                UpdateUnreadBadge();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Ошибка обработки сообщения чата: {ex.Message}");
                    }
                });
            };
        }

        private async Task LoadChatHistory()
        {
            try
            {
                if (!_networkService.IsInGame || string.IsNullOrEmpty(_networkService.GameId))
                    return;

                // Очищаем текущий чат
                ChatWindowControl.ClearChat();

                // Добавляем системное сообщение
                ChatWindowControl.AddSystemMessage("💬 Чат игры подключен");

                // Загружаем историю
                var history = await _networkService.GetChatHistoryAsync();

                if (history != null && history.Count > 0)
                {
                    Console.WriteLine($"📜 Загружено {history.Count} сообщений из истории");

                    // Сортируем по времени (старые -> новые)
                    var sortedHistory = history.OrderBy(h => h.Timestamp).ToList();

                    foreach (var message in sortedHistory)
                    {
                        if (message.IsSystem)
                        {
                            ChatWindowControl.AddSystemMessage(message.Message);
                        }
                        else
                        {
                            ChatWindowControl.AddMessage(
                                message.SenderName,
                                message.Message,
                                message.IsOwn);
                        }
                    }

                    // Добавляем разделитель
                    ChatWindowControl.AddSystemMessage("─── Новые сообщения ───");
                }
                else
                {
                    ChatWindowControl.AddSystemMessage("История чата пуста");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка загрузки истории чата: {ex.Message}");
                ChatWindowControl.AddSystemMessage("Не удалось загрузить историю чата");
            }
        }

        private void CloseGameOverWindowIfOpen()
        {
            
            foreach (Window window in Application.Current.Windows)
            {
                if (window is GameOverWindow gameOverWindow && window.Owner == this)
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Ошибка закрытия окна: {ex.Message}");
                    }
                }
            }
        }

        private async Task<bool> SurrenderOnExitAsync()
        {
            try
            {
                if (!_networkService.IsInGame || string.IsNullOrEmpty(_networkService.GameId))
                    return true;

                Console.WriteLine("🎮 Начинаем процедуру выхода из игры...");

                // Вариант 1: Через ServerNetworkManager если есть доступ к методу
                if (_networkService is ServerNetworkManager serverManager)
                {
                    // Предположим, что у ServerNetworkManager есть метод для сдачи
                    // Если нет - создадим его
                    return await serverManager.SurrenderAsync(_playerId);
                }

                // Вариант 2: Прямой вызов API
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.BaseAddress = new Uri("http://localhost:5214");
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var surrenderRequest = new
                {
                    PlayerId = _playerId
                };

                var response = await httpClient.PostAsJsonAsync(
                    $"/api/Game/{_networkService.GameId}/surrender",
                    surrenderRequest,
                    _jsonOptions);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("✅ Сдача выполнена успешно через API");
                    return true;
                }

                Console.WriteLine($"⚠️ API сдачи вернул ошибку: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка при выполнении сдачи: {ex.Message}");
                return false;
            }
            finally
            {
                // Всегда пытаемся покинуть игру через сетевой сервис
                try
                {
                    await _networkService.LeaveGameAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка при LeaveGameAsync: {ex.Message}");
                }
            }
        }

        private async Task LoadGameStateFromServer()
        {
            try
            {
                if (!_networkService.IsInGame || string.IsNullOrEmpty(_networkService.GameId) || string.IsNullOrEmpty(_playerId))
                    return;

                var boardState = await FetchBoardState();
                if (boardState != null)
                {
                    // Обновляем ОБЕ доски
                    UpdateBoardsFromServer(boardState);

                    // Синхронизируем выстрелы противника
                    await SyncOpponentShotsFromServer(boardState);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка загрузки состояния: {ex.Message}");
            }
        }

        private async Task SyncOpponentShotsFromServer(BoardResponse boardState)
        {
            try
            {
                if (boardState?.MyBoard?.Cells == null)
                    return;

                // СИНХРОНИЗИРУЕМ ВЫСТРЕЛЫ ПРОТИВНИКА С СЕРВЕРА
                foreach (var serverCell in boardState.MyBoard.Cells)
                {
                    var cellKey = $"{serverCell.X},{serverCell.Y}";

                    // Если клетка была отстреляна (по данным сервера)
                    if (serverCell.WasShot)
                    {
                        // Добавляем в список выстрелов противника
                        if (!_opponentShots.Contains(cellKey))
                        {
                            _opponentShots.Add(cellKey);
                            Console.WriteLine($"🎯 Сервер: противник стрелял в {cellKey}");
                        }

                        // Если это попадание (статус 2 или "Hit")
                        var status = GetStatusFromServerCell(serverCell);
                        if (status == "Hit" || status == "Sunk")
                        {
                            if (!_hitsOnPlayer.Contains(cellKey))
                            {
                                _hitsOnPlayer.Add(cellKey);
                                Console.WriteLine($"🔥 Сервер: попадание противника в {cellKey}");
                            }
                        }
                    }
                }

                // Сразу обновляем визуализацию
                UpdatePlayerBoardVisual();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка синхронизации выстрелов: {ex.Message}");
            }
        }

        private async Task<BoardResponse> FetchBoardState()
        {
            try
            {
                if (_networkService is ServerNetworkManager serverManager)
                {
                    return await serverManager.GetBoardState();
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка получения состояния доски: {ex.Message}");
                return null;
            }
        }

        private void UpdateBoardsFromServer(BoardResponse boardState)
        {
            try
            {
                // Обновляем свое поле
                if (boardState?.MyBoard?.Cells != null)
                {
                    foreach (var serverCell in boardState.MyBoard.Cells)
                    {
                        var cellKey = $"{serverCell.X},{serverCell.Y}";
                        if (_playerCells.TryGetValue(cellKey, out var cell))
                        {
                            UpdateCellFromServerData(cell, serverCell, true);
                        }
                    }
                }

                // Обновляем поле противника
                if (boardState?.OpponentBoard?.Cells != null)
                {
                    foreach (var serverCell in boardState.OpponentBoard.Cells)
                    {
                        var cellKey = $"{serverCell.X},{serverCell.Y}";
                        if (_opponentCells.TryGetValue(cellKey, out var cell))
                        {
                            UpdateCellFromServerData(cell, serverCell, false);
                        }
                    }
                }

                // Обновляем визуализацию
                UpdatePlayerBoardVisual();
                UpdateOpponentBoardVisual();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка обновления досок: {ex.Message}");
            }
        }

        private void UpdateCellFromServerData(Border cell, BoardCell serverCell, bool isPlayerCell)
        {
            try
            {
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;

                    // Получаем статус
                    var status = GetStatusFromServerCell(serverCell);

                    // Обновляем состояние клетки
                    cellState.IsHit = status == "Hit" || status == "Sunk";
                    cellState.IsMiss = status == "Miss";
                    cellState.IsSunk = status == "Sunk";
                    cellState.IsAroundSunk = status == "Miss" && serverCell.WasShot && !serverCell.HasShip;

                    if (isPlayerCell)
                    {
                        cellState.HasShip = serverCell.HasShip;

                        // 🔥 ВАЖНО: Синхронизируем выстрелы противника
                        var cellKey = $"{serverCell.X},{serverCell.Y}";
                        if (serverCell.WasShot && !_opponentShots.Contains(cellKey))
                        {
                            _opponentShots.Add(cellKey);
                        }
                        if (cellState.IsHit && !_hitsOnPlayer.Contains(cellKey))
                        {
                            _hitsOnPlayer.Add(cellKey);
                        }
                    }
                    else
                    {
                        // Для противника всегда скрываем корабли
                        cellState.HasShip = false;
                    }

                    // Обновляем цвет СРАЗУ
                    cell.Background = cellState.GetCurrentColor();

                    // Обновляем символ
                    if (isPlayerCell)
                        UpdatePlayerCellSymbol(cell, cellState);
                    else
                        UpdateOpponentCellSymbol(cell, cellState);

                    // Обновляем доступность для поля противника
                    if (!isPlayerCell)
                    {
                        cell.IsEnabled = !cellState.WasShot && !cellState.IsAroundSunk && _isMyTurn;
                    }

                    // Логирование для отладки
                    if (isPlayerCell && serverCell.WasShot)
                    {
                        Console.WriteLine($"   [ВАШЕ ПОЛЕ] ({serverCell.X},{serverCell.Y}): " +
                                         $"Status={status}, WasShot={serverCell.WasShot}, " +
                                         $"IsHit={cellState.IsHit}, IsMiss={cellState.IsMiss}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка обновления клетки: {ex.Message}");
            }
        }

        private string GetStatusFromServerCell(BoardCell cell)
        {
            return cell.Status ?? "Empty";
        }

        // ==================== УПРАВЛЕНИЕ ИГРОЙ ====================

        private void SetGameState(GameClientState newState)
        {
            _currentState = newState;
            UpdateButtonsState();

            switch (newState)
            {
                case GameClientState.PlacingShips:
                    GameStatus.Text = "Расставьте свои корабли";
                    EnablePlayerBoard(true);
                    EnableOpponentBoard(false);
                    StopMyTurn();
                    break;

                case GameClientState.SearchingGame:
                    GameStatus.Text = "🔍 Поиск случайного соперника...";
                    EnablePlayerBoard(false);
                    EnableOpponentBoard(false);
                    StopMyTurn();
                    break;

                case GameClientState.InGame:
                    GameStatus.Text = "Игра началась!";
                    EnablePlayerBoard(false);
                    break;

                case GameClientState.GameFinished:
                    GameStatus.Text = "Игра окончена";
                    EnablePlayerBoard(false);
                    EnableOpponentBoard(false);
                    StopMyTurn();
                    break;
            }
        }

        private void UpdateButtonsState()
        {
            switch (_currentState)
            {
                case GameClientState.PlacingShips:
                    RandomPlacementButton.IsEnabled = true;
                    ClearBoardButton.IsEnabled = true;
                    RandomOpponentButton.IsEnabled = _gameLogic.AllShipsPlaced;
                    PlayWithFriendButton.IsEnabled = _gameLogic.AllShipsPlaced;

                    RandomPlacementButton.Opacity = 1;
                    ClearBoardButton.Opacity = 1;
                    RandomOpponentButton.Opacity = _gameLogic.AllShipsPlaced ? 1 : 0.5;
                    PlayWithFriendButton.Opacity = _gameLogic.AllShipsPlaced ? 1 : 0.5;
                    break;

                case GameClientState.SearchingGame:
                case GameClientState.InGame:
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

        private async Task UpdateTurnFromServer()
        {
            try
            {
                var gameState = await _networkService.GetUpdatedGameStateAsync();
                if (gameState == null) return;

                if (gameState.Status == "playing")
                {
                    if (gameState.CurrentTurn == "player")
                    {
                        await SetPlayerTurn();
                    }
                    else
                    {
                        await SetOpponentTurn();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка проверки хода: {ex.Message}");
            }
        }

        private async Task<bool> IsMyTurn()
        {
            try
            {
                if (!_networkService.IsInGame)
                    return false;

                var gameState = await _networkService.GetUpdatedGameStateAsync();
                return gameState?.CurrentTurn == "player";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка проверки хода: {ex.Message}");
                return _isMyTurn;
            }
        }

        private async Task SetPlayerTurn()
        {
            if (!_isMyTurn)
            {
                if (!await IsMyTurn())
                {
                    await SetOpponentTurn();
                    return;
                }
            }

            ResetTurnTimer();
            StartMyTurn();
            await LoadGameStateFromServer();
        }

        private async Task SetOpponentTurn()
        {
            StopMyTurn();

            if (_isMyTurn)
            {
                if (await IsMyTurn())
                {
                    await SetPlayerTurn();
                    return;
                }
            }

            Dispatcher.Invoke(() =>
            {
                EnableOpponentBoard(false);
                GameStatus.Text = "Ход противника...";
            });

            await LoadGameStateFromServer();
        }

        // ==================== ТАЙМЕР ХОДА ====================

        private void TurnTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMyTurn) return;

            if (_remainingTurnTime > 0)
            {
                _remainingTurnTime--;
                UpdateTimerDisplay();
            }
            else
            {
                _turnTimer.Stop();
                _turnTimerRunning = false;
                TimeExpired();
            }
        }

        private void ResetTurnTimer()
        {
            Dispatcher.Invoke(() =>
            {
                _remainingTurnTime = 30;
                UpdateTimerDisplay();

                if (_isMyTurn && !_turnTimerRunning)
                {
                    _turnTimer.Start();
                    _turnTimerRunning = true;
                }
            });
        }


        private void UpdateTimerDisplay()
        {
            TurnTimeTextBlock.Text = $"{_remainingTurnTime} сек";

            if (_remainingTurnTime <= 10)
            {
                TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                TurnTimeTextBlock.Foreground = Brushes.White;
            }
            else if (_remainingTurnTime <= 20)
            {
                TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(241, 196, 15));
                TurnTimeTextBlock.Foreground = Brushes.Black;
            }
            else
            {
                TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                TurnTimeTextBlock.Foreground = Brushes.White;
            }
        }

        private void StartMyTurn()
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isMyTurn) 
                {
                    _isMyTurn = true;
                    _remainingTurnTime = 30;

                    if (!_turnTimerRunning)
                    {
                        _turnTimer.Start();
                        _turnTimerRunning = true;
                    }

                    TurnTimerContainer.Visibility = Visibility.Visible;
                    UpdateTimerDisplay();

                    EnableOpponentBoard(true);
                    GameStatus.Text = "Ваш ход! Выберите клетку на поле противника";
                }
            });
        }

        private void StopMyTurn()
        {
            Dispatcher.Invoke(() =>
            {
                if (_isMyTurn) 
                {
                    _isMyTurn = false;

                    if (_turnTimerRunning)
                    {
                        _turnTimer.Stop();
                        _turnTimerRunning = false;
                    }

                    TurnTimerContainer.Visibility = Visibility.Collapsed;

                    EnableOpponentBoard(false);
                    GameStatus.Text = "Ход противника...";
                }
            });
        }

        private async void TimeExpired()
        {
            Console.WriteLine("⏰ Время хода истекло!");

            if (_currentState == GameClientState.InGame && await IsMyTurn())
            {
                await SetOpponentTurn();
                ShowSpecialMessage("Время хода истекло!", 2000);
            }
        }

        // ==================== ОБРАБОТЧИКИ СОБЫТИЙ КЛЕТОК ====================

        private void YourCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentState != GameClientState.PlacingShips) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, _) = tag;
                var coords = coordsStr.Split(',');
                var row = int.Parse(coords[0]);
                var col = int.Parse(coords[1]);

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
            if (_currentState != GameClientState.PlacingShips) return;

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

        private async void OpponentCell_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            if (_currentState != GameClientState.InGame) return;

            if (!await IsMyTurn())
            {
                ShowSpecialMessage("Сейчас не ваш ход!", 2000);
                return;
            }

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, cellState) = tag;

                if (cellState.WasShot || cellState.IsAroundSunk)
                {
                    ShowSpecialMessage("Сюда уже стреляли!", 2000);
                    return;
                }

                var coords = coordsStr.Split(',');
                var row = int.Parse(coords[0]);
                var col = int.Parse(coords[1]);

                var success = await _networkService.ShootAsync(row, col);

                if (success)
                {
                    cell.IsEnabled = false;
                }
            }
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;
                cellState.IsHighlighted = true;
                cell.Background = cellState.GetCurrentColor();
            }
        }

        private void Cell_MouseLeave(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;
                cellState.IsHighlighted = false;
                cell.Background = cellState.GetCurrentColor();
            }
        }

        // ==================== ОБНОВЛЕНИЕ UI ====================

        private void UpdateYourBoard()
        {
            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                var coords = kvp.Key.Split(',');
                var row = int.Parse(coords[0]);
                var col = int.Parse(coords[1]);
                var cellKey = kvp.Key;

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (coordsStr, cellState) = tag;

                    var isCurrentShipCell = _gameLogic.GetCurrentShipBeingPlacedCells()
                        .Any(c => c.row == row && c.col == col);
                    var hasShip = _gameLogic.GetPlayerShipCells()
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

                    cell.Background = cellState.GetCurrentColor();
                    UpdatePlayerCellSymbol(cell, cellState);
                }
            }
        }

        private void UpdatePlayerBoardVisual()
        {
            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                var cellKey = kvp.Key;

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;

                    // Обновляем состояние клетки из сетевых данных
                    // (выстрелы противника уже добавлены в _opponentShots и _hitsOnPlayer)
                    cellState.IsHit = _hitsOnPlayer.Contains(cellKey);
                    cellState.IsMiss = _opponentShots.Contains(cellKey) && !_hitsOnPlayer.Contains(cellKey);

                    // Обновляем цвет сразу же
                    cell.Background = cellState.GetCurrentColor();

                    // Обновляем символ
                    UpdatePlayerCellSymbol(cell, cellState);
                }
            }

            Console.WriteLine($"🔄 Обновлено свое поле: {_opponentShots.Count} выстрелов противника, {_hitsOnPlayer.Count} попаданий");
        }

        private async void StartPeriodicUpdate()
        {
            while (_currentState == GameClientState.InGame)
            {
                await Task.Delay(2000); // Обновляем каждые 2 секунды

                try
                {
                    Console.WriteLine("⏱️ Периодическое обновление состояния...");

                    await LoadGameStateFromServer();

                    // Также обновляем поле противника
                    UpdateOpponentBoardVisual();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка периодического обновления: {ex.Message}");
                }
            }
        }

        private void UpdateOpponentBoardVisual()
        {
            foreach (var kvp in _opponentCells)
            {
                var cell = kvp.Value;
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;
                    cell.Background = cellState.GetCurrentColor();
                    UpdateOpponentCellSymbol(cell, cellState);

                    cell.IsEnabled = !cellState.WasShot && !cellState.IsAroundSunk && _isMyTurn;
                    cell.Cursor = cell.IsEnabled ? Cursors.Hand : Cursors.Arrow;
                }
            }
        }

        private void UpdatePlayerCellSymbol(Border cell, CellState cellState)
        {
            if (cell.Child is TextBlock textBlock)
            {
                if (cellState.IsAroundSunk && !cellState.HasShip)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsSunk && cellState.HasShip)
                {
                    textBlock.Text = "☠";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsHit && cellState.HasShip && !cellState.IsSunk)
                {
                    textBlock.Text = "✖";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsMiss && !cellState.HasShip && !cellState.IsAroundSunk)
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

        private void UpdateOpponentCellSymbol(Border cell, CellState cellState)
        {
            if (cell.Child is TextBlock textBlock)
            {
                if (cellState.IsAroundSunk && !cellState.HasShip)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsSunk && cellState.HasShip)
                {
                    textBlock.Text = "☠";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsHit && cellState.HasShip && !cellState.IsSunk)
                {
                    textBlock.Text = "✖";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                else if (cellState.IsMiss && !cellState.HasShip && !cellState.IsAroundSunk)
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

        private void UpdateOpponentCellAfterShot(int row, int col, string result, string shipName = "")
        {
            var cellKey = $"{row},{col}";

            if (!_opponentCells.TryGetValue(cellKey, out var cell)) return;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;

                switch (result)
                {
                    case "hit":
                        cellState.IsHit = true;
                        cellState.IsMiss = false;
                        cell.Background = Brushes.Red;
                        _hitsOnOpponent.Add(cellKey);
                        ShowCellSymbol(cell, "✖", Brushes.White);
                        break;

                    case "sunk":
                        cellState.IsHit = true;
                        cellState.IsSunk = true;
                        cell.Background = Brushes.DarkRed;
                        _hitsOnOpponent.Add(cellKey);
                        ShowCellSymbol(cell, "☠", Brushes.White);
                        Console.WriteLine($"💥 Потоплен корабль {shipName}!");
                        break;

                    case "miss":
                        cellState.IsMiss = true;
                        cellState.IsHit = false;
                        cell.Background = Brushes.LightGray;
                        ShowCellSymbol(cell, "○", Brushes.DarkGray);
                        break;
                }

                cell.IsEnabled = false;
            }
        }

        private void ShowCellSymbol(Border cell, string symbol, Brush color)
        {
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

        private void UpdateShipsInfo()
        {
            var placed4 = _gameLogic.PlayerShips.Count(s => s.Size == 4 && s.IsPlaced);
            var placed3 = _gameLogic.PlayerShips.Count(s => s.Size == 3 && s.IsPlaced);
            var placed2 = _gameLogic.PlayerShips.Count(s => s.Size == 2 && s.IsPlaced);
            var placed1 = _gameLogic.PlayerShips.Count(s => s.Size == 1 && s.IsPlaced);

            ShipsInfo.Text = $"Осталось расставить: {1 - placed4}x4, {2 - placed3}x3, {3 - placed2}x2, {4 - placed1}x1";
            GameStatus.Text = _gameLogic.GetCurrentShipInfo();
        }

        private void EnableOpponentBoard(bool enable)
        {
            foreach (var cell in _opponentCells.Values)
            {
                cell.IsEnabled = enable;
                cell.Cursor = enable ? Cursors.Hand : Cursors.Arrow;
            }
        }

        private void EnablePlayerBoard(bool enable)
        {
            foreach (var cell in _playerCells.Values)
            {
                cell.IsEnabled = enable;
            }
        }

        // ==================== КНОПКИ И УПРАВЛЕНИЕ ====================

        private void RandomPlacementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState != GameClientState.PlacingShips) return;

            _gameLogic.RandomlyPlaceShips();
            UpdateYourBoard();
            UpdateShipsInfo();
            UpdateButtonsState();
            ShowSpecialMessage("Корабли расставлены случайным образом!", 2000);
        }

        private void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
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

            SetGameState(GameClientState.SearchingGame);
            StartSearch();

            var gameId = await _networkService.CreateGameAsync("random");

            if (gameId != null)
            {
                CancelSearch();
                SetGameState(GameClientState.InGame);
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

            SetGameState(GameClientState.PlacingShips);
        }

        private void PlayWithFriendButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Режим игры с другом будет реализован позже",
                "Игра с другом",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_networkService.IsInGame && _currentState == GameClientState.InGame)
            {
                var result = MessageBox.Show("Вы в игре. Выйти из игры?\n\n" +
                                           "⚠️ ВНИМАНИЕ: Если вы выйдете сейчас, победа будет присвоена противнику!",
                                           "Подтверждение выхода",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                // Выполняем сдачу
                await SurrenderOnExitAsync();

                // Даем время на обработку
                await Task.Delay(1000);
            }

            ReturnToMainMenu();
        }

        private void ResetGameForNewRound()
        {
            // Сбрасываем флаг при начале новой игры
            _gameOverWindowShown = false;

            SetGameState(GameClientState.PlacingShips);

            // Сбрасываем таймер
            if (_turnTimerRunning)
            {
                _turnTimer.Stop();
                _turnTimerRunning = false;
            }
            _remainingTurnTime = 30;
            TurnTimerContainer.Visibility = Visibility.Collapsed;

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
            // Закрываем все окна GameOverWindow перед возвратом
            CloseGameOverWindowIfOpen();

            // Закрываем чат
            ChatWindowControl.Visibility = Visibility.Collapsed;
            ChatWindowControl.ClearChat();

            _networkService.LeaveGameAsync();
            _gameLogic.ClearBoard();
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();

            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }

        // ==================== ЧАТ ====================

        private async void ChatWindowControl_MessageSent(object sender, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                var playerName = Application.Current.Properties.Contains("Username")
                    ? Application.Current.Properties["Username"].ToString()
                    : _playerName;

                // Сразу добавляем сообщение в UI 
                ChatWindowControl.AddMessage("Вы", message, isOwn: true);

                // Отправляем на сервер если есть подключение и игра
                if (_networkService.IsInGame && _networkService.IsConnected)
                {
                    await _networkService.SendChatMessageAsync(message);
                    Console.WriteLine($"💬 Сообщение отправлено: {message}");
                }
                else
                {
                    ChatWindowControl.AddSystemMessage("⚠️ Нет подключения к серверу. Сообщение не отправлено.");
                }
            }
            catch (Exception ex)
            {
                ChatWindowControl.AddSystemMessage($"❌ Ошибка отправки: {ex.Message}");
                Console.WriteLine($"❌ Ошибка отправки сообщения: {ex.Message}");
            }
        }

        private void ChatWindowControl_Closed(object sender, EventArgs e)
        {
            ChatWindowControl.Visibility = Visibility.Collapsed;
        }

        private void ChatWindowControl_UnreadCountChanged(object sender, int count)
        {
            _unreadMessages = count;
            UpdateUnreadBadge();
        }

        private void OpenChatButton_Click(object sender, RoutedEventArgs e)
        {
            ChatWindowControl.Visibility = Visibility.Visible;
            ChatWindowControl.MarkAsRead();
            UpdateUnreadBadge();
            ChatWindowControl.FocusInput();
            ChatWindowControl.ScrollToBottom();
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

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

        private void ShowSpecialMessage(string message, int durationMilliseconds)
        {
            if (_messageTimer.IsEnabled)
            {
                _messageTimer.Stop();
            }

            _showingSpecialMessage = true;
            GameStatus.Text = message;

            _messageTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
            _messageTimer.Tick += (s, e) =>
            {
                _messageTimer.Stop();
                _showingSpecialMessage = false;

                if (_currentState == GameClientState.InGame)
                {
                    GameStatus.Text = _isMyTurn ? "Ваш ход!" : "Ход противника...";
                }
                else
                {
                    GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                }
            };

            _messageTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Обработка системных команд закрытия (Alt+F4, системное меню)
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(hwnd);

            if (hwndSource != null)
            {
                hwndSource.AddHook(new System.Windows.Interop.HwndSourceHook(WndProc));
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_CLOSE = 0xF060;

            if (msg == WM_SYSCOMMAND && (int)wParam == SC_CLOSE)
            {
                // Системная команда закрытия (Alt+F4, крестик в заголовке)
                if (_networkService.IsInGame && _currentState == GameClientState.InGame)
                {
                    Dispatcher.Invoke(async () =>
                    {
                        var result = MessageBox.Show("Вы в игре. Выйти из игры?\n\n" +
                                                   "⚠️ ВНИМАНИЕ: Если вы выйдете сейчас, победа будет присвоена противнику!",
                                                   "Подтверждение выхода",
                                                   MessageBoxButton.YesNo,
                                                   MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            await SurrenderOnExitAsync();
                            await Task.Delay(1000);
                            CloseGameOverWindowIfOpen();
                            Close();
                        }
                    });

                    handled = true; // Блокируем стандартную обработку
                    return IntPtr.Zero;
                }
            }

            return IntPtr.Zero;
        }


        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (_isExitingFromGameOver)
                {
                    base.OnClosing(e);
                    return;
                }

                if (_networkService.IsInGame && _currentState == GameClientState.InGame)
                {
                    // Показываем подтверждение
                    var result = MessageBox.Show("Вы в игре. Выйти из игры?\n\n" +
                                               "⚠️ ВНИМАНИЕ: Если вы выйдете сейчас, победа будет присвоена противнику!",
                                               "Подтверждение выхода",
                                               MessageBoxButton.YesNo,
                                               MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }

                    //  Выполняем принудительную сдачу
                    e.Cancel = true; // Временно блокируем закрытие

                    Task.Run(async () =>
                    {
                        await Dispatcher.Invoke(async () =>
                        {
                            await SurrenderOnExitAsync();

                            // Даем время серверу обработать сдачу
                            await Task.Delay(1000);

                            // Закрываем все дочерние окна
                            CloseGameOverWindowIfOpen();

                            // Теперь закрываем основное окно
                            Dispatcher.Invoke(() =>
                            {
                                e.Cancel = false;
                                Close();
                            });
                        });
                    });
                }
                else
                {
                    // Если не в игре - просто закрываем
                    CloseGameOverWindowIfOpen();
                    base.OnClosing(e);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при закрытии окна: {ex.Message}");
                CloseGameOverWindowIfOpen();
                base.OnClosing(e);
            }
        }
    }
}